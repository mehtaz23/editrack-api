# Implementation Plan: EDI Transaction Ingest

**Branch**: `001-edi-transaction-ingest` | **Date**: 2025-07-23 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-edi-transaction-ingest/spec.md`

---

## Summary

EdiTrack's first production capability: accept raw EDI X12 payloads via a JSON envelope
`POST /api/ingest`, validate them at the API boundary, persist them durably to PostgreSQL
using EF Core, and return a structured acknowledgment carrying a UUID v7 transaction ID,
correlation ID, and initial status `Received`. Every attempt — accepted or rejected — emits
a structured log entry (Serilog) with safe metadata only (no raw payload). DB write failures
return `503 Service Unavailable` with a consistent error shape; the caller owns retries.

The local development model intentionally keeps the **app process** running natively (via
`dotnet run`), while only its dependency (PostgreSQL) runs in Docker via `compose.yml`.
This allows fast iteration, debugger attachment, and migration commands without any
container rebuild cycle.

---

## Technical Context

**Language/Version**: C# 13 / ASP.NET Core (.NET 10)
**Primary Dependencies**: EF Core (Npgsql provider), Serilog, Microsoft.AspNetCore.OpenApi,
Scalar.AspNetCore, xUnit, Moq, Testcontainers.PostgreSql
**Storage**: PostgreSQL — `EdiTransaction` table; UUID v7 PK; `varchar(50)` status;
`text` payload column
**Testing**: xUnit + Moq (unit) + WebApplicationFactory + Testcontainers.PostgreSql (integration)
**Target Platform**: Native `dotnet run` for the app; Docker Compose for Postgres only
**Performance Goals**: Acknowledgment returned within 5 seconds of request receipt (SC-005)
**Constraints**: Zero build warnings (`--warnaserror`); all secrets via environment variables;
no raw payload in logs; 503 on DB failure with no auto-retry
**Scale/Scope**: Single ingest endpoint (MVP); no deduplication, no auth, no rate limiting

---

## Constitution Check

*GATE: Must pass before implementation begins. Violations must be resolved, not deferred.*

- [x] **Clean Code (I)**: Controller action, service method, and validator will each stay
      ≤30 lines; no `.Result`/`.Wait()` — all DB and logging calls are `async`/`await`
- [x] **API Contract (II)**: `IngestRequest` and `IngestAcknowledgment` are explicit DTO
      classes in `Dtos/`; controller declares `[ProducesResponseType(200)]`,
      `[ProducesResponseType(400)]`, `[ProducesResponseType(503)]`; error envelope reused
      across all failure responses
- [x] **Testing (III — NON-NEGOTIABLE)**: Unit tests for `IngestService` (success, validation
      bypass, DB failure branches); integration tests for POST /api/ingest happy path,
      validation rejections, and 503 path via `WebApplicationFactory` +
      `Testcontainers.PostgreSql`; test project lives in `/tests/EdiTrack.Api.Tests/`
- [x] **Observability (IV)**: Serilog via `ILogger<T>` injection; one structured log entry per
      attempt (accepted + rejected); no payload content; `GET /health` endpoint added to
      satisfy the constitution's baseline requirement for new projects
- [x] **Security (V)**: Connection string read from `DATABASE_URL` env var (or
      `ConnectionStrings:DefaultConnection` in `appsettings.Development.json` for local dev
      only — not a secret in production); input validation at controller boundary via data
      annotations; no hardcoded credentials; `.env.example` provided
- [x] **Dependencies (VI)**: All new NuGet packages listed below with justification; no
      package added speculatively; BCL alternatives considered for each
- [x] **CI (VII)**: `ci.yml` and `cd.yml` stubs created at `.github/workflows/`;
      `dotnet test` step covers both unit and integration tests
- [x] **Solution Structure**: Production code in `/src/EdiTrack.Api/`; tests in
      `/tests/EdiTrack.Api.Tests/`; namespaces follow `EdiTrack.Api.*` convention

---

## Local Dev Setup

### Strategy

The application runs as a native `dotnet run` process. Only PostgreSQL runs in Docker.
This avoids container rebuild cycles during development and allows full IDE debugger
attachment and hot reload.

```
Developer machine
  ├── dotnet run  →  EdiTrack.Api  (http://localhost:5250)
  └── docker compose up -d  →  postgres:16  (localhost:5432)
```

### compose.yml (repo root)

Defines a single `postgres` service. No application container. The file is named
`compose.yml` (Docker Compose v2 convention, not `docker-compose.yml`).

```yaml
# compose.yml — local development dependencies only.
# Run: docker compose up -d
# The EdiTrack.Api application runs natively via `dotnet run`.

services:
  postgres:
    image: postgres:16-alpine
    container_name: editrack-postgres
    restart: unless-stopped
    environment:
      POSTGRES_DB: editrack
      POSTGRES_USER: editrack
      POSTGRES_PASSWORD: editrack_dev
    ports:
      - "5432:5432"
    volumes:
      - postgres_data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U editrack -d editrack"]
      interval: 5s
      timeout: 5s
      retries: 5

volumes:
  postgres_data:
```

> **Security note**: The password `editrack_dev` is intentionally a local-only dev
> credential. It is NOT used in any other environment. The `.env.example` documents
> the `DATABASE_URL` variable that overrides this for production and CI.

### appsettings.Development.json

Points to the compose-hosted Postgres instance. This file is committed — it contains no
secrets (dev credentials are local-only and documented in `.env.example`).

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore.Database.Command": "Information"
    }
  },
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=editrack;Username=editrack;Password=editrack_dev"
  }
}
```

`Program.cs` resolves the connection string via:

```csharp
// Prefer DATABASE_URL env var (CI / production); fall back to appsettings value (local dev)
var connectionString = builder.Configuration["DATABASE_URL"]
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("No database connection string configured.");
```

### .env.example (repo root)

```dotenv
# Copy to .env and populate for local overrides.
# DATABASE_URL overrides the appsettings.Development.json connection string.
# Leave unset to use the compose-hosted default for local development.
DATABASE_URL=
ALLOWED_CORS_ORIGINS=http://localhost:3000
```

### EF Core Migration Workflow

Prerequisites: `dotnet tool install --global dotnet-ef`

```bash
# 1. Start the compose dependency
docker compose up -d

# 2. Add the initial migration (run from repo root)
dotnet ef migrations add InitialCreate \
  --project src/EdiTrack.Api \
  --startup-project src/EdiTrack.Api

# 3. Apply migration to the compose-hosted Postgres
dotnet ef database update \
  --project src/EdiTrack.Api \
  --startup-project src/EdiTrack.Api

# 4. (Optional) view the generated SQL without applying it
dotnet ef migrations script \
  --project src/EdiTrack.Api \
  --startup-project src/EdiTrack.Api
```

Migrations are stored at `src/EdiTrack.Api/Migrations/` and committed to the repository.

### Daily Dev Workflow

```bash
# One-time: spin up Postgres
docker compose up -d

# Run the API (hot reload)
dotnet watch run --project src/EdiTrack.Api

# Run all tests (no compose required — Testcontainers manages its own Postgres)
dotnet test

# Stop Postgres when done
docker compose down
```

---

## Project Structure

### Documentation (this feature)

```text
specs/001-edi-transaction-ingest/
├── spec.md              ← feature spec (existing)
├── plan.md              ← this file
├── research.md          ← Phase 0 output (see below)
├── data-model.md        ← Phase 1 output (see below)
├── quickstart.md        ← Phase 1 output (see below)
├── contracts/           ← Phase 1 output (see below)
│   └── ingest-api.md
└── tasks.md             ← Phase 2 output (/speckit.tasks — NOT produced by /speckit.plan)
```

### Source Code Layout

```text
editrack-api/
├── compose.yml                              ← NEW: local dev Postgres only
├── .env.example                             ← NEW: env var documentation
├── .github/
│   └── workflows/
│       ├── ci.yml                           ← NEW: build / test / format gate
│       └── cd.yml                           ← NEW: deployment stub
├── src/
│   └── EdiTrack.Api/
│       ├── Controllers/
│       │   └── IngestController.cs          ← NEW: POST /api/ingest
│       ├── Dtos/
│       │   ├── IngestRequest.cs             ← NEW: request DTO + data annotations
│       │   ├── IngestAcknowledgment.cs      ← NEW: success response DTO
│       │   └── ErrorResponse.cs             ← NEW: consistent error envelope
│       ├── Domain/
│       │   ├── Entities/
│       │   │   └── EdiTransaction.cs        ← NEW: EF Core entity
│       │   └── Enums/
│       │       └── TransactionStatus.cs     ← NEW: C# enum
│       ├── Infrastructure/
│       │   └── Data/
│       │       └── EdiTrackDbContext.cs     ← NEW: DbContext + Fluent API config
│       ├── Migrations/                      ← NEW: EF Core generated migrations
│       ├── Services/
│       │   ├── IIngestService.cs            ← NEW: service interface
│       │   └── IngestService.cs             ← NEW: service implementation
│       ├── appsettings.json                 ← EXISTING (add Serilog config)
│       ├── appsettings.Development.json     ← EXISTING (add ConnectionStrings)
│       ├── Program.cs                       ← EXISTING (significant additions)
│       └── EdiTrack.Api.csproj              ← EXISTING (new PackageReferences)
└── tests/
    └── EdiTrack.Api.Tests/                  ← NEW: test project
        ├── Unit/
        │   └── Services/
        │       └── IngestServiceTests.cs    ← NEW: unit tests (Moq)
        ├── Integration/
        │   └── IngestEndpointTests.cs       ← NEW: integration tests (Testcontainers)
        ├── Helpers/
        │   └── PostgresFixture.cs           ← NEW: shared Testcontainers lifecycle
        └── EdiTrack.Api.Tests.csproj        ← NEW: test project file
```

---

## NuGet Packages

All new packages listed per Principle VI with justification and BCL alternative assessment.

### Production packages (EdiTrack.Api.csproj)

| Package | Version (target) | Justification | BCL alternative? |
|---|---|---|---|
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 9.x | EF Core PostgreSQL provider; documented in spec Technical Decisions | No BCL equivalent; required by spec decision |
| `Microsoft.EntityFrameworkCore.Design` | 9.x | EF Core CLI tooling for `dotnet ef migrations` commands | No — tooling package only |
| `Serilog.AspNetCore` | 9.x | Structured logging as required by constitution Principle IV | `Microsoft.Extensions.Logging` is the interface (used at injection sites); Serilog is the implementation mandated by the constitution |
| `Serilog.Sinks.Console` | 5.x | JSON-formatted console output for local dev and container stdout | Included via `Serilog.AspNetCore` transitively; made explicit for clarity |

> **Note on EF Core version**: Target EF Core 9.x (stable, aligns with Npgsql 9.x provider)
> on .NET 10. EF Core 10 preview packages exist but the 9.x provider is the current stable
> release targeting Npgsql 9.0.

### Test packages (EdiTrack.Api.Tests.csproj)

| Package | Justification |
|---|---|
| `Microsoft.NET.Test.Sdk` | Required test host |
| `xunit` | Mandated by constitution Principle III |
| `xunit.runner.visualstudio` | IDE test runner integration |
| `Moq` | Mandated by constitution Principle III |
| `Microsoft.AspNetCore.Mvc.Testing` | `WebApplicationFactory` for integration tests; no viable BCL alternative |
| `Testcontainers.PostgreSql` | Spin up ephemeral Postgres for integration tests (see rationale below) |

### Testcontainers vs Compose Postgres for Integration Tests

**Recommendation: Testcontainers.**

| Concern | Testcontainers | Compose Postgres |
|---|---|---|
| Test isolation | Fresh DB container per fixture | Shared; requires manual schema teardown |
| CI compatibility | Works out of the box (Docker socket) | Requires `docker compose up` as a CI pre-step |
| Onboarding friction | Zero — `dotnet test` just works | Developer must remember to start compose first |
| Debugger / app dev | N/A (tests only) | Still useful for manual `dotnet run` sessions |

The compose Postgres **remains valuable** for interactive local development (running the
app, inspecting data with a DB client, testing migrations). Integration tests use
Testcontainers for their own isolated lifecycle so that `dotnet test` is always a
single-command, no-prereqs operation.

---

## Implementation Phases

Tasks are ordered by dependency. Each phase produces a buildable, testable increment.

---

### Phase 1 — Scaffold Test Project and Solution Wiring

**Goal**: Establish the `EdiTrack.Api.Tests` project and wire it into the solution before
writing any feature code. Tests fail fast if project structure drifts.

**Tasks**:

1. Create `tests/EdiTrack.Api.Tests/EdiTrack.Api.Tests.csproj` with target framework
   `net10.0`, NuGet references: `xunit`, `xunit.runner.visualstudio`,
   `Microsoft.NET.Test.Sdk`, `Moq`, `Microsoft.AspNetCore.Mvc.Testing`,
   `Testcontainers.PostgreSql`; add a `<ProjectReference>` to `EdiTrack.Api.csproj`.
2. Add `EdiTrack.Api.Tests` to `EdiTrack.sln` via `dotnet sln add`.
3. Verify `dotnet build` and `dotnet test` succeed (no tests yet — just verifies wiring).
4. Remove the `WeatherForecast` scaffolding from `Program.cs` (placeholder code from
   the dotnet new template; not part of this feature).

---

### Phase 2 — Local Dev Infrastructure

**Goal**: Developer can run `docker compose up -d` and have a working Postgres before any
application code is written.

**Tasks**:

1. Create `compose.yml` at repo root with the PostgreSQL service definition (see Local Dev
   Setup section above).
2. Create `.env.example` at repo root documenting `DATABASE_URL` and
   `ALLOWED_CORS_ORIGINS`.
3. Add `compose.yml` to `.dockerignore` exclusions if not already covered by the existing
   `**/docker-compose*` glob (verify the glob covers `compose.yml`; if not, add
   `compose.yml` explicitly).
4. Verify `docker compose up -d` starts the Postgres container cleanly and the health
   check passes.

---

### Phase 3 — Domain Model and EF Core Setup

**Goal**: The `EdiTransaction` entity is modelled, the `DbContext` is configured, and
the initial migration is generated and applied to the compose Postgres.

**Tasks**:

1. Add NuGet packages to `EdiTrack.Api.csproj`:
   - `Npgsql.EntityFrameworkCore.PostgreSQL` (9.x)
   - `Microsoft.EntityFrameworkCore.Design` (9.x)

2. Create `src/EdiTrack.Api/Domain/Enums/TransactionStatus.cs`:
   ```csharp
   namespace EdiTrack.Api.Domain.Enums;

   public enum TransactionStatus
   {
       Received
   }
   ```

3. Create `src/EdiTrack.Api/Domain/Entities/EdiTransaction.cs`:
   - Properties: `Id` (`Guid`, UUID v7 PK), `SenderId` (`string`), `ReceiverId` (`string`),
     `TransactionType` (`string`), `CorrelationId` (`string`), `Payload` (`string`),
     `Status` (`TransactionStatus`), `ReceivedAt` (`DateTimeOffset`)
   - No default value constructor magic — all set explicitly in the service layer

4. Create `src/EdiTrack.Api/Infrastructure/Data/EdiTrackDbContext.cs`:
   - Inherit `DbContext`
   - `DbSet<EdiTransaction> Transactions`
   - `OnModelCreating`: configure `EdiTransaction` entity:
     - `HasKey(t => t.Id)`
     - `Property(t => t.Status).HasConversion<string>().HasMaxLength(50)`
     - `Property(t => t.SenderId).IsRequired().HasMaxLength(50)` (spec doesn't cap length
       but 50 is a reasonable EDI ISA segment field width)
     - `Property(t => t.ReceiverId).IsRequired().HasMaxLength(50)`
     - `Property(t => t.TransactionType).IsRequired().HasMaxLength(50)`
     - `Property(t => t.CorrelationId).IsRequired().HasMaxLength(100)`
     - `Property(t => t.Payload).IsRequired()` (column type: `text` — no max length)
     - `Property(t => t.ReceivedAt).IsRequired()`

5. Register `EdiTrackDbContext` in `Program.cs`:
   ```csharp
   var connectionString = builder.Configuration["DATABASE_URL"]
       ?? builder.Configuration.GetConnectionString("DefaultConnection")
       ?? throw new InvalidOperationException("No database connection string configured.");

   builder.Services.AddDbContext<EdiTrackDbContext>(opts =>
       opts.UseNpgsql(connectionString));
   ```

6. Update `appsettings.Development.json` with the `ConnectionStrings` block (see Local Dev
   Setup section).

7. Generate and apply the initial EF Core migration (see migration workflow commands above).
   Commit the generated `Migrations/` directory.

8. Verify `dotnet build --warnaserror` succeeds with zero warnings.

---

### Phase 4 — DTOs and Validation

**Goal**: Request and response shapes are defined as explicit DTO classes in `Dtos/`,
validated at the API boundary before any service logic is invoked.

**Tasks**:

1. Create `src/EdiTrack.Api/Dtos/IngestRequest.cs`:
   ```csharp
   // JSON body for POST /api/ingest
   public sealed class IngestRequest
   {
       [Required, MinLength(1)] public string SenderId { get; init; } = string.Empty;
       [Required, MinLength(1)] public string ReceiverId { get; init; } = string.Empty;
       [Required, MinLength(1)] public string TransactionType { get; init; } = string.Empty;
       public string? CorrelationId { get; init; }
       [Required, MinLength(1)] public string Payload { get; init; } = string.Empty;
   }
   ```

2. Create `src/EdiTrack.Api/Dtos/IngestAcknowledgment.cs`:
   ```csharp
   // Returned for a successfully accepted ingest submission.
   public sealed class IngestAcknowledgment
   {
       public Guid TransactionId { get; init; }
       public string CorrelationId { get; init; } = string.Empty;
       public string SenderId { get; init; } = string.Empty;
       public string ReceiverId { get; init; } = string.Empty;
       public string TransactionType { get; init; } = string.Empty;
       public DateTimeOffset ReceivedAt { get; init; }
       public string Status { get; init; } = string.Empty;
   }
   ```

3. Create `src/EdiTrack.Api/Dtos/ErrorResponse.cs` (consistent error envelope):
   ```csharp
   // Uniform error shape for 400 and 503 responses.
   public sealed class ErrorResponse
   {
       public string Message { get; init; } = string.Empty;
       public IReadOnlyDictionary<string, string[]>? Errors { get; init; }
   }
   ```

4. Confirm that ASP.NET Core's built-in model validation (`[ApiController]` attribute on
   the controller) will automatically return `400 Bad Request` with validation errors when
   data annotations fail — this removes the need for manual `ModelState` checking in the
   action method. The default `ValidationProblemDetails` response shape should be replaced
   with `ErrorResponse` via a custom `InvalidModelStateResponseFactory` registered in
   `Program.cs`.

---

### Phase 5 — EDI Payload Validation

**Goal**: The service layer can determine whether a string resembles a valid EDI X12
document. This is a deliberate thin check — deep parsing is a future feature.

**Decision**: An EDI X12 document must begin with the `ISA` interchange segment.
The minimal validation rule is: `payload.TrimStart()` starts with `ISA*`. This satisfies
FR-003 ("does not resemble an EDI X12 document") without parsing segment counts or field
values. The spec notes that whitespace trimming should occur before the check (edge case).

**Tasks**:

1. Add a private static helper method `IsEdiX12Shaped(string payload)` to `IngestService`:
   - Returns `true` if `payload.AsSpan().TrimStart()` starts with `ISA*`
     (ASCII comparison, case-sensitive — ISA segment headers are uppercase by spec)
   - Method is ≤5 lines; no external NuGet package required

---

### Phase 6 — Service Layer

**Goal**: `IngestService` orchestrates validation, ID generation, DB persistence, and
returns a result the controller maps to HTTP responses.

**Tasks**:

1. Create `src/EdiTrack.Api/Services/IIngestService.cs`:
   ```csharp
   public interface IIngestService
   {
       Task<IngestResult> IngestAsync(IngestRequest request, CancellationToken ct = default);
   }
   ```
   where `IngestResult` is a discriminated union-style record:
   ```csharp
   public abstract record IngestResult
   {
       public sealed record Success(IngestAcknowledgment Acknowledgment) : IngestResult;
       public sealed record ValidationFailure(string Message) : IngestResult;
       public sealed record PersistenceFailure(string Message) : IngestResult;
   }
   ```

2. Create `src/EdiTrack.Api/Services/IngestService.cs`:
   - Constructor injects: `EdiTrackDbContext`, `ILogger<IngestService>`
   - `IngestAsync` logic:
     a. If `payload.TrimStart()` does not start with `ISA*` → return
        `IngestResult.ValidationFailure("Payload does not resemble an EDI X12 document.")`
     b. Resolve `correlationId`: use `request.CorrelationId` if non-null/non-empty,
        otherwise generate `Guid.NewGuid().ToString("N")`
     c. Build `EdiTransaction` with `Id = Guid.CreateVersion7()`, `ReceivedAt =
        DateTimeOffset.UtcNow`, `Status = TransactionStatus.Received`
     d. `_context.Transactions.Add(entity)`
     e. Wrap `await _context.SaveChangesAsync(ct)` in `try/catch (DbUpdateException ex)`:
        - On catch: log the exception at `Error` level (no payload), return
          `IngestResult.PersistenceFailure("A transient database error occurred. Please retry.")`
     f. Log success at `Information` level (structured fields: correlationId, senderId,
        receiverId, transactionType, transactionId, outcome = "Accepted")
     g. Return `IngestResult.Success(acknowledgment)`

3. Register in `Program.cs`:
   ```csharp
   builder.Services.AddScoped<IIngestService, IngestService>();
   ```

---

### Phase 7 — Controller

**Goal**: `IngestController` exposes `POST /api/ingest`, maps `IngestResult` to HTTP
responses, and declares all response types for OpenAPI accuracy.

**Tasks**:

1. Create `src/EdiTrack.Api/Controllers/IngestController.cs`:
   ```csharp
   [ApiController]
   [Route("api")]
   public sealed class IngestController : ControllerBase
   ```
   - Constructor injects `IIngestService`, `ILogger<IngestController>`
   - Action: `[HttpPost("ingest")]` named `IngestAsync`
   - Attributes:
     - `[ProducesResponseType<IngestAcknowledgment>(StatusCodes.Status200OK)]`
     - `[ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]`
     - `[ProducesResponseType<ErrorResponse>(StatusCodes.Status503ServiceUnavailable)]`
   - Logic:
     ```
     var result = await _service.IngestAsync(request, ct);
     return result switch
     {
         IngestResult.Success s    => Ok(s.Acknowledgment),
         IngestResult.ValidationFailure v => BadRequest(new ErrorResponse { Message = v.Message }),
         IngestResult.PersistenceFailure p => StatusCode(503, new ErrorResponse { Message = p.Message }),
         _                         => StatusCode(500, new ErrorResponse { Message = "Unexpected error." })
     };
     ```
   - Log the rejection at `Warning` level for `ValidationFailure` (service already logs
     success; controller logs rejection — keeps logging at the right layer)

2. Enable `[ApiController]` automatic 400 response but override the response body shape
   to use `ErrorResponse` (not `ValidationProblemDetails`) via
   `builder.Services.Configure<ApiBehaviorOptions>(opts => opts.InvalidModelStateResponseFactory = ...)`.

3. Confirm `app.MapControllers()` is added to `Program.cs`.

---

### Phase 8 — Serilog Integration

**Goal**: Structured JSON log output via Serilog, configured at host startup, using
`ILogger<T>` at all injection sites (no Serilog static references in application code).

**Tasks**:

1. Add NuGet packages to `EdiTrack.Api.csproj`:
   - `Serilog.AspNetCore` (9.x)
   - `Serilog.Sinks.Console` (5.x)

2. In `Program.cs`, before `WebApplication.CreateBuilder`:
   ```csharp
   Log.Logger = new LoggerConfiguration()
       .WriteTo.Console(new JsonFormatter())
       .CreateBootstrapLogger();
   ```

3. Call `builder.Host.UseSerilog((ctx, services, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration).WriteTo.Console(new JsonFormatter()))`.

4. Add Serilog request logging middleware: `app.UseSerilogRequestLogging()`.

5. Configure `appsettings.json` `Serilog` section for minimum levels; silence EF Core
   command logging in Production but leave `Information` in Development.

---

### Phase 9 — Health Endpoint

**Goal**: `GET /health` endpoint required by constitution Principle IV.

**Tasks**:

1. Add `builder.Services.AddHealthChecks()` to `Program.cs` (no DB health check at this
   stage — basic liveness only; DB checks are a future observability feature).
2. Add `app.MapHealthChecks("/health")` to the middleware pipeline.
3. Verify `GET /health` returns `200 Healthy`.

---

### Phase 10 — OpenAPI and Scalar UI

**Goal**: OpenAPI spec and Scalar UI are accurate and accessible in development.

**Tasks**:

1. Verify `MapOpenApi()` and `MapScalarApiReference()` remain in `Program.cs` (already
   present in the scaffold).
2. Confirm `/openapi/v1.json` reflects the new `POST /api/ingest` endpoint with all
   declared response types.
3. Confirm Scalar UI mounts at `/scalar` in Development (already configured; verify).
4. Update `EdiTrack.Api.http` to replace the WeatherForecast example with a valid
   `POST /api/ingest` example request (useful for manual dev testing).

---

### Phase 11 — CI Workflow

**Goal**: GitHub Actions pipeline enforces build, test, and format gates on every PR
and push to `main`.

**Tasks**:

1. Create `.github/workflows/ci.yml`:
   - Triggers: `push` to `main`, `pull_request` targeting `main`
   - Steps: `dotnet restore` → `dotnet build --no-restore --warnaserror` →
     `dotnet test --no-build` → `dotnet format --verify-no-changes`
   - Uses `ubuntu-latest` runner
   - Sets `ASPNETCORE_ENVIRONMENT=Test` so `appsettings.Test.json` can override
     connection strings in integration tests (Testcontainers does not need this but
     it isolates config)

2. Create `.github/workflows/cd.yml`:
   ```yaml
   # Deployment via ArgoCD and Kubernetes — deferred to a future phase.
   # This stub exists to satisfy constitution Principle VII.
   name: CD
   on:
     push:
       branches: [main]
   jobs:
     placeholder:
       runs-on: ubuntu-latest
       steps:
         - name: Deployment not yet configured
           run: echo "CD pipeline is a stub. Production deployment is deferred."
   ```

---

### Phase 12 — Unit Tests

**Goal**: `IngestService` is fully unit-tested in isolation using Moq.

**Test file**: `tests/EdiTrack.Api.Tests/Unit/Services/IngestServiceTests.cs`

**Test cases**:

| Test | Scenario | Expected |
|---|---|---|
| `IngestAsync_ValidRequest_ReturnsSuccess` | Valid ISA* payload, all fields present | `IngestResult.Success`; acknowledgment has UUID v7 ID, `Status = "Received"`, non-null `CorrelationId` |
| `IngestAsync_CorrelationIdOmitted_GeneratesOne` | `CorrelationId` null in request | `IngestResult.Success`; `Acknowledgment.CorrelationId` is non-null and non-empty |
| `IngestAsync_EmptyPayload_ReturnsValidationFailure` | `Payload = ""` (after controller admits it) | `IngestResult.ValidationFailure` |
| `IngestAsync_NonEdiPayload_ReturnsValidationFailure` | `Payload = "not EDI"` | `IngestResult.ValidationFailure` |
| `IngestAsync_PayloadWithLeadingWhitespace_AcceptsWhenEdishaped` | `Payload = "  ISA*..."` | `IngestResult.Success` (edge case: trim before check) |
| `IngestAsync_DbUpdateException_ReturnsPersistenceFailure` | Mock `SaveChangesAsync` throws `DbUpdateException` | `IngestResult.PersistenceFailure` |
| `IngestAsync_Success_LogsExpectedFields` | Valid request | Logger called with `CorrelationId`, `SenderId`, `ReceiverId`, `TransactionType`, `outcome = "Accepted"` |
| `IngestAsync_ValidationFailure_NoDbSave` | Non-EDI payload | `SaveChangesAsync` is never called (verify via Moq `Verify`) |

**Notes**:
- Mock `EdiTrackDbContext` using Moq or use EF Core's `UseInMemoryDatabase` provider for
  the service-level tests. Prefer an in-memory EF context for service tests (avoids complex
  Moq setup for `DbSet`) — using EF `InMemory` for unit tests is acceptable when the test
  is scoped to service logic rather than DB behaviour (DB behaviour is covered in Phase 13).
- `DbUpdateException` branch test uses a mock `EdiTrackDbContext` subclass or `UseInMemoryDatabase`
  with a forced exception via a Moq shim.

---

### Phase 13 — Integration Tests

**Goal**: `POST /api/ingest` is tested end-to-end against a real PostgreSQL instance
managed by Testcontainers, using `WebApplicationFactory`.

**Test file**: `tests/EdiTrack.Api.Tests/Integration/IngestEndpointTests.cs`

**Test cases**:

| Test | Scenario | Assertions |
|---|---|---|
| `PostIngest_ValidRequest_Returns200WithAcknowledgment` | Full valid body | `200`; response body deserialises to `IngestAcknowledgment`; `TransactionId` is non-empty Guid; `Status = "Received"`; `CorrelationId` present |
| `PostIngest_ValidRequest_PersistsTransaction` | Full valid body | Record exists in DB via `EdiTrackDbContext` after response |
| `PostIngest_MissingRequiredField_Returns400` | `senderId` omitted | `400`; response body is `ErrorResponse` shape |
| `PostIngest_EmptyPayload_Returns400` | `payload = ""` | `400` |
| `PostIngest_NonEdiPayload_Returns400` | `payload = "not EDI"` | `400` |
| `PostIngest_CorrelationIdOmitted_GeneratedInResponse` | No `correlationId` in body | `200`; `CorrelationId` in response is non-empty |
| `PostIngest_ValidRequest_NoRecordOnRejectedAttempt` | Rejected call followed by count check | DB has 0 `EdiTransaction` records after rejected request |

**Infrastructure** (`tests/EdiTrack.Api.Tests/Helpers/PostgresFixture.cs`):

- Implements `IAsyncLifetime` (xUnit)
- Starts `PostgreSqlContainer` via Testcontainers on `InitializeAsync`
- Creates a `WebApplicationFactory<Program>` override that replaces `DATABASE_URL` with
  the Testcontainers connection string
- Runs EF Core `database.EnsureCreatedAsync()` (not `MigrateAsync` — test schema creation
  via EnsureCreated is faster and sufficient for test isolation; full migration paths are
  validated by the dev migration workflow)
- Stops the container on `DisposeAsync`
- Shared across tests in the class via `IClassFixture<PostgresFixture>`

**Test isolation**: Each test class gets its own `PostgresFixture` instance (fresh container).
Tests within a class can rely on the DB being clean at fixture start. No `TruncateAsync`
between individual tests is needed since the container is per-class.

---

## Test Strategy Summary

| Layer | Framework | Scope |
|---|---|---|
| Unit | xUnit + Moq + EF InMemory | `IngestService` logic branches |
| Integration | xUnit + WebApplicationFactory + Testcontainers.PostgreSql | `POST /api/ingest` end-to-end, real Postgres |
| Manual / Exploratory | `EdiTrack.Api.http` + Scalar UI | Local `dotnet run` + compose Postgres |

**CI gate**: `dotnet test` runs both unit and integration test assemblies. Testcontainers
requires Docker to be available on the CI runner (GitHub Actions `ubuntu-latest` has Docker
pre-installed; no extra CI config needed).

---

## Sequence Diagram — Happy Path

```
Caller → POST /api/ingest (JSON)
         ↓
  [ApiController model binding + data annotations]
         ↓ valid request (senderId, receiverId, transactionType, payload present)
  IngestController.IngestAsync()
         ↓
  IngestService.IngestAsync()
         ├─ IsEdiX12Shaped(payload) → true
         ├─ Resolve correlationId (use provided or generate)
         ├─ Build EdiTransaction (Guid.CreateVersion7(), DateTimeOffset.UtcNow, Status.Received)
         ├─ DbContext.Transactions.Add(entity)
         ├─ await SaveChangesAsync()  ← DB write
         ├─ ILogger.LogInformation(...)  ← structured log (no payload)
         └─ return IngestResult.Success(acknowledgment)
         ↓
  IngestController → 200 OK (IngestAcknowledgment JSON)
```

---

## Sequence Diagram — DB Write Failure (503 Path)

```
IngestService.IngestAsync()
  ├─ IsEdiX12Shaped → true
  ├─ Build EdiTransaction
  ├─ try { await SaveChangesAsync() } catch (DbUpdateException ex)
  │    ├─ ILogger.LogError(ex, ...)  ← no payload in log
  │    └─ return IngestResult.PersistenceFailure(message)
  └─ (no record written)
         ↓
  IngestController → 503 Service Unavailable (ErrorResponse JSON)
```

---

## Open Decisions

| # | Decision | Resolution |
|---|---|---|
| OD-1 | EF Core version (9 vs 10 preview) | Use EF Core 9.x / Npgsql 9.x (stable). EF Core 10 is preview on .NET 10; upgrade deferred. |
| OD-2 | Integration test isolation strategy | Testcontainers (per-class fixture) — no compose dependency needed for `dotnet test` |
| OD-3 | EDI X12 "shaped" validation rule | `payload.TrimStart()` starts with `ISA*` (case-sensitive). Deep parsing deferred. |
| OD-4 | `EdiTransaction.CorrelationId` nullability | Stored as non-null `string` in DB (service always generates one if missing). DB schema: `NOT NULL`. |
| OD-5 | `IngestResult` type | Nested abstract record hierarchy in `Services/` — avoids returning raw HTTP codes from service |

---

## Out of Scope

The following are explicitly excluded from this feature per the spec:

- Authentication / authorisation
- EDI X12 deep parsing or segment-level validation
- Deduplication of repeated payloads
- JSONB promotion of the payload column
- Retry logic in the API layer (503 = caller retries)
- CORS configuration (deferred to when a frontend origin is known)
- Multi-tenancy or per-sender routing
