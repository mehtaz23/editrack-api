# Tasks: EDI Transaction Ingest

**Feature Branch**: `001-edi-transaction-ingest`
**Input**: Design documents from `/specs/001-edi-transaction-ingest/`
**Prerequisites**: spec.md ✅ | plan.md ✅ | research.md ✅ | data-model.md ✅ | contracts/ingest-api.md ✅ | quickstart.md ✅

**Tests**: Per the EdiTrack API Constitution (Principle III), testing is NON-NEGOTIABLE.
Unit tests for `IngestService` (xUnit + Moq + EF Core InMemory) and integration tests via
`WebApplicationFactory` + `Testcontainers.PostgreSql` are mandatory — not optional extras.

**Organization**: Tasks are grouped by user story so each story can be implemented, tested,
and delivered independently. Phases 3–5 map to US1/US2/US3 in spec.md priority order.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no data dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Exact file paths are included in every task description

---

## Phase 1: Setup — Scaffold, Test Project & Dev Infrastructure

**Purpose**: Establish the `EdiTrack.Api.Tests` project, wire it into the solution, remove
the WeatherForecast scaffold, and spin up the local Postgres dependency. No feature code
yet — just a green build, a wired test runner, and a healthy database container.

- [ ] T001 Create `tests/EdiTrack.Api.Tests/EdiTrack.Api.Tests.csproj` targeting `net10.0` with NuGet references: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `Moq`, `Microsoft.AspNetCore.Mvc.Testing`, `Testcontainers.PostgreSql`; add `<ProjectReference Include="..\..\src\EdiTrack.Api\EdiTrack.Api.csproj" />` inside an `<ItemGroup>`
- [ ] T002 Add the test project to the solution: run `dotnet sln EdiTrack.sln add tests/EdiTrack.Api.Tests/EdiTrack.Api.Tests.csproj`; verify `dotnet build` and `dotnet test` both succeed (no tests exist yet — just confirms solution wiring is correct and the test runner initialises without error)
- [ ] T003 Remove WeatherForecast scaffold from `src/EdiTrack.Api/Program.cs`: delete the `summaries` string array, the entire `app.MapGet("/weatherforecast", ...)` lambda, and the `record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)` definition; run `dotnet build --warnaserror` to confirm zero warnings after removal
- [ ] T004 [P] Create `compose.yml` at the repo root (not `docker-compose.yml` — Docker Compose v2 convention) defining a single service `postgres` using image `postgres:16-alpine`, container name `editrack-postgres`, `restart: unless-stopped`, env vars `POSTGRES_DB=editrack`, `POSTGRES_USER=editrack`, `POSTGRES_PASSWORD=editrack_dev`, port `5432:5432`, named volume `postgres_data:/var/lib/postgresql/data`, and a healthcheck `CMD-SHELL pg_isready -U editrack -d editrack` with `interval: 5s`, `timeout: 5s`, `retries: 5`; add the `volumes:` top-level key declaring `postgres_data:`
- [ ] T005 [P] Create `.env.example` at the repo root with two commented, documented variables: `DATABASE_URL=` (empty default — overrides `appsettings.Development.json` connection string in CI and production) and `ALLOWED_CORS_ORIGINS=http://localhost:3000`; include inline comments explaining each variable
- [ ] T006 Inspect `.dockerignore` at the repo root; if the existing glob `**/docker-compose*` does not match the literal filename `compose.yml`, add an explicit `compose.yml` entry to prevent the file from being copied into the Docker build context
- [ ] T007 Run `docker compose up -d`; confirm `docker compose ps` reports `editrack-postgres` as `running (healthy)`; run `dotnet build --warnaserror` one final time to confirm the solution is in a clean state before any domain code is written

**Checkpoint**: Solution builds with zero warnings. Test runner executes (no tests yet). Postgres container is healthy. WeatherForecast scaffold is gone.

---

## Phase 2: Foundational — Domain Model, EF Core & DTOs

**Purpose**: Define the `EdiTransaction` entity, `TransactionStatus` enum, `EdiTrackDbContext`,
and all three DTO classes. Generate and apply the `InitialCreate` migration. This phase MUST
be complete before any user story work begins — all subsequent phases depend on these types.

**⚠️ CRITICAL**: No user story implementation can begin until this phase is complete.

- [ ] T008 Add NuGet packages to `src/EdiTrack.Api/EdiTrack.Api.csproj`: `Npgsql.EntityFrameworkCore.PostgreSQL` version `9.*` and `Microsoft.EntityFrameworkCore.Design` version `9.*`; run `dotnet restore` and confirm no errors; verify `dotnet build --warnaserror` still passes
- [ ] T009 [P] Create `src/EdiTrack.Api/Domain/Enums/TransactionStatus.cs`; namespace `EdiTrack.Api.Domain.Enums`; define `public enum TransactionStatus { Received }` — `Received` is the only valid status for this feature; future statuses (`Processing`, `Delivered`, `Failed`) will be added via later migrations; do NOT add them speculatively now
- [ ] T010 Create `src/EdiTrack.Api/Domain/Entities/EdiTransaction.cs`; namespace `EdiTrack.Api.Domain.Entities`; define a `public sealed class EdiTransaction` with the following `public` properties (no constructor — all values are assigned explicitly in the service layer): `Guid Id`, `string SenderId`, `string ReceiverId`, `string TransactionType`, `string CorrelationId`, `string Payload`, `TransactionStatus Status`, `DateTimeOffset ReceivedAt`; initialise all string properties to `string.Empty` to satisfy the nullable reference types compiler
- [ ] T011 Create `src/EdiTrack.Api/Infrastructure/Data/EdiTrackDbContext.cs`; namespace `EdiTrack.Api.Infrastructure.Data`; inherit `DbContext`; expose `public DbSet<EdiTransaction> Transactions => Set<EdiTransaction>()`; override `OnModelCreating` with full Fluent API configuration: `HasKey(t => t.Id)`, `Property(t => t.Status).HasConversion<string>().HasMaxLength(50).IsRequired()`, `Property(t => t.SenderId).IsRequired().HasMaxLength(50)`, `Property(t => t.ReceiverId).IsRequired().HasMaxLength(50)`, `Property(t => t.TransactionType).IsRequired().HasMaxLength(50)`, `Property(t => t.CorrelationId).IsRequired().HasMaxLength(100)`, `Property(t => t.Payload).IsRequired()` (no max length — stored as PostgreSQL `text` column), `Property(t => t.ReceivedAt).IsRequired()`; add constructor `EdiTrackDbContext(DbContextOptions<EdiTrackDbContext> options) : base(options) {}`
- [ ] T012 Update `src/EdiTrack.Api/Program.cs` to register `EdiTrackDbContext`: add the connection string resolution block `var connectionString = builder.Configuration["DATABASE_URL"] ?? builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("No database connection string configured.");` then `builder.Services.AddDbContext<EdiTrackDbContext>(opts => opts.UseNpgsql(connectionString));`; add `using EdiTrack.Api.Infrastructure.Data;` and `using Microsoft.EntityFrameworkCore;`
- [ ] T013 Update `src/EdiTrack.Api/appsettings.Development.json` to add the `"ConnectionStrings"` section: `"ConnectionStrings": { "DefaultConnection": "Host=localhost;Port=5432;Database=editrack;Username=editrack;Password=editrack_dev" }` — this file is committed to the repository; the password is a local-dev-only credential and is explicitly NOT used in production (production uses the `DATABASE_URL` env var)
- [ ] T014 Generate the initial EF Core migration: run `dotnet ef migrations add InitialCreate --project src/EdiTrack.Api --startup-project src/EdiTrack.Api` (requires `dotnet tool install --global dotnet-ef` if not already installed); then apply it: run `dotnet ef database update --project src/EdiTrack.Api --startup-project src/EdiTrack.Api`; verify the `EdiTransactions` table exists by running `psql -h localhost -p 5432 -U editrack -d editrack -c "\dt"`; commit the generated `src/EdiTrack.Api/Migrations/` directory to the repository
- [ ] T015 [P] Create `src/EdiTrack.Api/Dtos/IngestRequest.cs`; namespace `EdiTrack.Api.Dtos`; define `public sealed class IngestRequest` with `init`-only properties and data annotation attributes: `[Required, MinLength(1)] public string SenderId { get; init; } = string.Empty;`, `[Required, MinLength(1)] public string ReceiverId { get; init; } = string.Empty;`, `[Required, MinLength(1)] public string TransactionType { get; init; } = string.Empty;`, `public string? CorrelationId { get; init; }` (optional — server generates one if absent), `[Required, MinLength(1)] public string Payload { get; init; } = string.Empty;`; add `using System.ComponentModel.DataAnnotations;`
- [ ] T016 [P] Create `src/EdiTrack.Api/Dtos/IngestAcknowledgment.cs`; namespace `EdiTrack.Api.Dtos`; define `public sealed class IngestAcknowledgment` with `init`-only properties: `public Guid TransactionId { get; init; }`, `public string CorrelationId { get; init; } = string.Empty;`, `public string SenderId { get; init; } = string.Empty;`, `public string ReceiverId { get; init; } = string.Empty;`, `public string TransactionType { get; init; } = string.Empty;`, `public DateTimeOffset ReceivedAt { get; init; }`, `public string Status { get; init; } = string.Empty;` — this shape is the `200 OK` response body per the API contract
- [ ] T017 [P] Create `src/EdiTrack.Api/Dtos/ErrorResponse.cs`; namespace `EdiTrack.Api.Dtos`; define `public sealed class ErrorResponse` with `init`-only properties: `public string Message { get; init; } = string.Empty;` (always present) and `public IReadOnlyDictionary<string, string[]>? Errors { get; init; }` (nullable — present for `400` field-level validation failures, absent for `503` transient failures); this single shape is the uniform error envelope for ALL non-2xx responses

**Checkpoint**: `dotnet build --warnaserror` passes with zero warnings. `EdiTransactions` table exists in compose Postgres. All entity and DTO types are compiled and usable.

---

## Phase 3: User Story 1 — Accept Valid EDI Submissions (Priority: P1) 🎯 MVP

**Goal**: A caller can submit a minimally well-formed EDI X12 payload with required metadata
and receive a structured acknowledgment containing a stable UUID v7 transaction ID, a
correlation ID, the received timestamp, and status `Received`. The transaction is persisted
durably to PostgreSQL before the response is returned.

**Independent Test**: `POST /api/ingest` with a JSON body containing `senderId`, `receiverId`,
`transactionType`, and a payload starting with `ISA*` returns `200 OK` with a deserializable
`IngestAcknowledgment` where `transactionId` is a non-empty GUID, `correlationId` is non-null,
`status == "Received"`, and the record is findable in the `EdiTransactions` table.

- [ ] T018 [US1] Create `src/EdiTrack.Api/Services/IIngestService.cs`; namespace `EdiTrack.Api.Services`; define `public interface IIngestService { Task<IngestResult> IngestAsync(IngestRequest request, CancellationToken ct = default); }`; in the same file define the `IngestResult` discriminated union as `public abstract record IngestResult` with three public sealed nested subtypes: `Success(IngestAcknowledgment Acknowledgment)`, `ValidationFailure(string Message)`, `PersistenceFailure(string Message)`; add `using EdiTrack.Api.Dtos;`
- [ ] T019 [US1] Create `src/EdiTrack.Api/Services/IngestService.cs`; namespace `EdiTrack.Api.Services`; implement `IIngestService`; constructor signature: `IngestService(EdiTrackDbContext context, ILogger<IngestService> logger)`; add `private static bool IsEdiX12Shaped(string payload) => payload.AsSpan().TrimStart().StartsWith("ISA*", StringComparison.Ordinal);`; implement `IngestAsync`: (a) if `!IsEdiX12Shaped(request.Payload)` return `new IngestResult.ValidationFailure("Payload does not resemble an EDI X12 document.")`, (b) resolve `correlationId`: use `request.CorrelationId` if not null or whitespace, else `Guid.NewGuid().ToString("N")`, (c) construct `EdiTransaction` with `Id = Guid.CreateVersion7()`, `SenderId = request.SenderId`, `ReceiverId = request.ReceiverId`, `TransactionType = request.TransactionType`, `CorrelationId = correlationId`, `Payload = request.Payload`, `Status = TransactionStatus.Received`, `ReceivedAt = DateTimeOffset.UtcNow`, (d) `_context.Transactions.Add(entity)`, (e) wrap `await _context.SaveChangesAsync(ct)` in `try { ... } catch (DbUpdateException ex) { _logger.LogError(ex, "Persistence failure {CorrelationId} {SenderId} {ReceiverId}", correlationId, request.SenderId, request.ReceiverId); return new IngestResult.PersistenceFailure("A transient database error occurred. Please retry."); }`, (f) on success: `_logger.LogInformation("Ingest accepted {CorrelationId} {SenderId} {ReceiverId} {TransactionType} {TransactionId} {Outcome}", correlationId, request.SenderId, request.ReceiverId, request.TransactionType, entity.Id, "Accepted")`, return `new IngestResult.Success(new IngestAcknowledgment { TransactionId = entity.Id, CorrelationId = correlationId, SenderId = entity.SenderId, ReceiverId = entity.ReceiverId, TransactionType = entity.TransactionType, ReceivedAt = entity.ReceivedAt, Status = entity.Status.ToString() })` — NEVER log `request.Payload` anywhere in this method
- [ ] T020 [US1] Update `src/EdiTrack.Api/Program.cs`: add `builder.Services.AddControllers();` (to enable MVC controller discovery), add `builder.Services.AddScoped<IIngestService, IngestService>();` (registers the service for DI), and add `app.MapControllers();` to the HTTP pipeline (after `app.UseHttpsRedirection()`); add required `using EdiTrack.Api.Services;`
- [ ] T021 [US1] Create `src/EdiTrack.Api/Controllers/IngestController.cs`; namespace `EdiTrack.Api.Controllers`; annotate with `[ApiController]` and `[Route("api")]`; declare as `public sealed class IngestController : ControllerBase`; constructor injects `IIngestService service` and `ILogger<IngestController> logger`; add action: `[HttpPost("ingest")] [ProducesResponseType<IngestAcknowledgment>(StatusCodes.Status200OK)] [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)] [ProducesResponseType<ErrorResponse>(StatusCodes.Status503ServiceUnavailable)] public async Task<IActionResult> IngestAsync([FromBody] IngestRequest request, CancellationToken ct)`; implement body with pattern-match: `var result = await _service.IngestAsync(request, ct); return result switch { IngestResult.Success s => Ok(s.Acknowledgment), IngestResult.ValidationFailure v => BadRequest(new ErrorResponse { Message = v.Message }), IngestResult.PersistenceFailure p => StatusCode(StatusCodes.Status503ServiceUnavailable, new ErrorResponse { Message = p.Message }), _ => StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse { Message = "Unexpected error." }) };`; log `Warning` for `ValidationFailure` arm: `_logger.LogWarning("Ingest rejected {Outcome} {Message}", "ValidationFailure", v.Message)`
- [ ] T022 [US1] Register the custom `InvalidModelStateResponseFactory` in `src/EdiTrack.Api/Program.cs` via `builder.Services.Configure<ApiBehaviorOptions>(opts => { opts.InvalidModelStateResponseFactory = ctx => { var errors = ctx.ModelState.Where(e => e.Value?.Errors.Count > 0).ToDictionary(e => e.Key, e => e.Value!.Errors.Select(x => x.ErrorMessage).ToArray()); return new BadRequestObjectResult(new ErrorResponse { Message = "One or more validation errors occurred.", Errors = errors }); }; });` — this replaces ASP.NET Core's default `ValidationProblemDetails` (RFC 7807) shape with the uniform `ErrorResponse` envelope for automatic model-binding `400` responses; add `using Microsoft.AspNetCore.Mvc;` and `using EdiTrack.Api.Dtos;`
- [ ] T023 [US1] Smoke-test the US1 happy path: start `dotnet run --project src/EdiTrack.Api`; run `curl -s -X POST http://localhost:5250/api/ingest -H "Content-Type: application/json" -d '{"senderId":"ACME","receiverId":"GLOBEX","transactionType":"850","payload":"ISA*00*          *00*          *ZZ*ACME           *ZZ*GLOBEX         *250101*0900*^*00501*000000001*0*P*>~"}'`; confirm `200 OK` with `transactionId` (non-empty GUID), `correlationId` (non-empty), `status: "Received"`; run `psql -h localhost -p 5432 -U editrack -d editrack -c 'SELECT id, sender_id, status FROM "EdiTransactions";'` to confirm the record is persisted; verify the auto-generated `correlationId` path by omitting that field from the request body

**Checkpoint**: `POST /api/ingest` returns `200 OK` with a full `IngestAcknowledgment`. The transaction is durable in Postgres. US1 is independently shippable as an MVP.

---

## Phase 4: User Story 2 — Reject Invalid Submissions Safely (Priority: P2)

**Goal**: Submissions with missing required metadata, an empty payload, or a payload that does
not resemble EDI X12 are rejected with a structured `ErrorResponse` and leave no transaction
record in the database. The `503` path is wired for DB write failures.

**Independent Test**: (1) POST with missing `senderId` → `400` + `ErrorResponse` with `errors.senderId` populated, count = 0. (2) POST with `payload = ""` → `400`, count = 0. (3) POST with `payload = "not EDI"` → `400` + `message` field only, count = 0.

- [ ] T024 [US2] Verify `IsEdiX12Shaped` in `src/EdiTrack.Api/Services/IngestService.cs` handles all specified edge cases: empty string `""` → false, whitespace-only `"   "` → false, `"not EDI"` → false, `"ISA*..."` → true, `"  ISA*..."` (leading whitespace) → true (the `TrimStart()` call handles the whitespace edge case per spec); if the implementation from T019 used `TrimStart()` on `string` rather than `AsSpan().TrimStart()`, update it to `payload.AsSpan().TrimStart().StartsWith("ISA*", StringComparison.Ordinal)` for allocation-free comparison; confirm `dotnet build --warnaserror` passes after any adjustment
- [ ] T025 [US2] Verify the `ValidationFailure` arm of the controller pattern-match in `src/EdiTrack.Api/Controllers/IngestController.cs` returns `400 BadRequest` with `new ErrorResponse { Message = "Payload does not resemble an EDI X12 document." }` (no `Errors` field — service-level validation returns a plain message, not field-level errors); test manually: `curl -X POST http://localhost:5250/api/ingest -H "Content-Type: application/json" -d '{"senderId":"ACME","receiverId":"GLOBEX","transactionType":"850","payload":"not EDI"}'`; confirm response body is `{"message":"Payload does not resemble an EDI X12 document."}` with no `errors` key
- [ ] T026 [US2] Verify the `InvalidModelStateResponseFactory` configured in T022 produces `{ "message": "One or more validation errors occurred.", "errors": { "senderId": ["..."] } }` for missing required fields; test: `curl -X POST http://localhost:5250/api/ingest -H "Content-Type: application/json" -d '{"receiverId":"GLOBEX","transactionType":"850","payload":"ISA*..."}'`; confirm `400` with `ErrorResponse` shape — `message` present, `errors.senderId` array with at least one entry; confirm response is NOT RFC 7807 `application/problem+json` format
- [ ] T027 [US2] Verify the `PersistenceFailure → 503` path is correctly wired in `src/EdiTrack.Api/Controllers/IngestController.cs`: confirm `StatusCode(StatusCodes.Status503ServiceUnavailable, new ErrorResponse { Message = p.Message })` returns `503` (not `500`) with a body matching `{"message":"A transient database error occurred. Please retry."}`; this path will be fully exercised by unit test T048 in Phase 7 — no manual test required here since stopping Postgres mid-request is impractical; confirm `[ProducesResponseType<ErrorResponse>(StatusCodes.Status503ServiceUnavailable)]` is declared on the action
- [ ] T028 [US2] End-to-end rejection verification: run three manual curl requests — (1) missing `senderId`, (2) `payload = ""`, (3) `payload = "this is not EDI"`; confirm each returns `400`; after all three, run `psql -h localhost -p 5432 -U editrack -d editrack -c 'SELECT COUNT(*) FROM "EdiTransactions";'` and confirm count is still `0` (no phantom records from any rejected attempt)

**Checkpoint**: All three rejection scenarios return `400` with the correct `ErrorResponse` shape. `503` path is correctly declared and wired. No records created on rejection. US2 is independently verifiable.

---

## Phase 5: User Story 3 — Trace Every Ingest Attempt (Priority: P3)

**Goal**: Every ingest attempt — whether accepted or rejected — produces a structured JSON log
entry on stdout with correlation ID, sender ID, receiver ID, transaction type (when known),
outcome, and timestamp. Raw payload content MUST NEVER appear in any log output.

**Independent Test**: Run one accepted and one rejected submission. Inspect stdout. Accepted entry must contain `CorrelationId`, `SenderId`, `ReceiverId`, `TransactionType`, `TransactionId`, `Outcome = "Accepted"`. Rejected entry must contain `CorrelationId`, `Outcome`. Neither entry may contain the `Payload` field or its value.

- [ ] T029 Add Serilog NuGet packages to `src/EdiTrack.Api/EdiTrack.Api.csproj`: `Serilog.AspNetCore` version `9.*` and `Serilog.Sinks.Console` version `5.*`; run `dotnet restore`; confirm `dotnet build --warnaserror` passes
- [ ] T030 [US3] Configure the bootstrap logger in `src/EdiTrack.Api/Program.cs` as the very first statement before `WebApplication.CreateBuilder(args)`: `Log.Logger = new LoggerConfiguration().WriteTo.Console(new JsonFormatter()).CreateBootstrapLogger();`; wrap the entire host-build-and-run block in `try { ... } catch (Exception ex) { Log.Fatal(ex, "Host terminated unexpectedly"); } finally { Log.CloseAndFlush(); }`; add `using Serilog;` and `using Serilog.Formatting.Json;` — the bootstrap logger captures startup exceptions (e.g., missing connection string) before the DI container is ready
- [ ] T031 [US3] In `src/EdiTrack.Api/Program.cs`, immediately after `var builder = WebApplication.CreateBuilder(args);`, call `builder.Host.UseSerilog((ctx, services, cfg) => cfg .ReadFrom.Configuration(ctx.Configuration) .ReadFrom.Services(services) .WriteTo.Console(new JsonFormatter()));` — this replaces the default `Microsoft.Extensions.Logging` host with Serilog and binds minimum log levels to the `"Serilog"` section in `appsettings.json`; `ILogger<T>` injection at all call sites continues to work unchanged
- [ ] T032 [US3] Add `app.UseSerilogRequestLogging();` to `src/EdiTrack.Api/Program.cs` HTTP pipeline (before `app.UseHttpsRedirection()`) to emit one structured log entry per HTTP request containing method, path, status code, and elapsed time; this satisfies the constitution's observability requirement for request-level tracing
- [ ] T033 [US3] Update `src/EdiTrack.Api/appsettings.json` to add a `"Serilog"` top-level section: `"Serilog": { "MinimumLevel": { "Default": "Information", "Override": { "Microsoft.AspNetCore": "Warning", "Microsoft.EntityFrameworkCore.Database.Command": "Warning" } } }` — suppresses noisy ASP.NET Core and EF Core command logs by default; for local dev, `Microsoft.EntityFrameworkCore.Database.Command` can be set to `"Information"` in `appsettings.Development.json` only if SQL query tracing is desired
- [ ] T034 [US3] Confirm the structured log calls in `src/EdiTrack.Api/Services/IngestService.cs` (added during T019) emit the correct fields: success log at `Information` must include `{CorrelationId}`, `{SenderId}`, `{ReceiverId}`, `{TransactionType}`, `{TransactionId}`, `{Outcome}`; error log in `DbUpdateException` catch must include `{CorrelationId}`, `{SenderId}`, `{ReceiverId}` and the exception object; AUDIT: confirm `request.Payload` does not appear anywhere in any `_logger.Log*` call in `IngestService.cs`; run `dotnet run` and submit a valid request — confirm stdout shows a structured JSON log line with these fields and no `payload` property
- [ ] T035 [US3] Confirm the `ValidationFailure` log call in `src/EdiTrack.Api/Controllers/IngestController.cs` (added during T021) emits `Warning` with `{Outcome}` and `{Message}` for service-level validation rejections; note that data-annotation `400` responses (missing fields) are intercepted by `InvalidModelStateResponseFactory` before `IngestAsync` is called — those rejections do not reach the controller log; run `dotnet run` and submit a non-EDI payload — confirm stdout shows a Warning-level structured log entry with no payload content

**Checkpoint**: `dotnet run` + both accepted and rejected submissions produce structured JSON log lines on stdout. No payload content in any log output. All three user stories are now fully implemented.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Health endpoint, OpenAPI verification, `.http` file update, and GitHub Actions
CI/CD workflows. These tasks do not change feature logic but complete the constitution
requirements and make the API discoverable and continuously verified.

- [ ] T036 Add health check infrastructure to `src/EdiTrack.Api/Program.cs`: add `builder.Services.AddHealthChecks();` (no DB health check at this stage — basic liveness probe only; database health checks are deferred to a future observability feature) and `app.MapHealthChecks("/health");`; run `curl http://localhost:5250/health` and confirm `200 OK` with response body `Healthy`
- [ ] T037 [P] Confirm `app.MapOpenApi()` and `app.MapScalarApiReference()` are present in `src/EdiTrack.Api/Program.cs` under the `if (app.Environment.IsDevelopment())` guard (these were in the original scaffold); run `dotnet run` and open `http://localhost:5250/openapi/v1.json`; verify the JSON spec contains the `POST /api/ingest` path with all three declared response schemas (200 `IngestAcknowledgment`, 400 `ErrorResponse`, 503 `ErrorResponse`); open `http://localhost:5250/scalar` and confirm the Scalar UI loads and the ingest endpoint is listed with its request body schema
- [ ] T038 [P] Update `src/EdiTrack.Api/EdiTrack.Api.http` to replace the `GET /weatherforecast` placeholder with three example requests: (1) a minimal valid EDI submission without `correlationId` (auto-generation path), (2) a valid submission with an explicit `correlationId`, (3) an invalid submission with `senderId` omitted (demonstrates `400` response); use `@editrack_api_HostAddress = http://localhost:5250` as the base URL variable; each request separated by `###`
- [ ] T039 Create `.github/workflows/ci.yml` with triggers `push` (branches: `[main]`) and `pull_request` (branches: `[main]`); single job `ci` running on `ubuntu-latest`; steps: `actions/checkout@v4` → `actions/setup-dotnet@v4` (dotnet-version: `10.x`) → `dotnet restore` → `dotnet build --no-restore --warnaserror` → `dotnet test --no-build --logger "github;summary.includePassedTests=false"` (Testcontainers uses the Docker socket pre-installed on `ubuntu-latest` — no extra Docker setup step needed) → `dotnet format --verify-no-changes`; set `env: ASPNETCORE_ENVIRONMENT: Test` at the job level
- [ ] T040 [P] Create `.github/workflows/cd.yml` with trigger `push` (branches: `[main]`); single job `placeholder` on `ubuntu-latest` with checkout step and one run step: `echo "CD pipeline stub. Production deployment via ArgoCD/Kubernetes is deferred to a future phase."` — satisfies constitution Principle VII (CI/CD stubs must exist) without configuring real deployment infrastructure prematurely
- [ ] T041 Run `dotnet build --warnaserror` one final time across the full solution; resolve any remaining warnings before the test phases begin; confirm zero warnings, zero errors; run `dotnet format --verify-no-changes` to confirm code style is consistent

**Checkpoint**: Health endpoint live. OpenAPI spec accurate with all response variants. `.http` file is runnable. CI workflow enforces build/test/format gate. Zero build warnings.

---

## Phase 7: Unit Tests — IngestService (xUnit + Moq + EF Core InMemory)

**Purpose**: Verify all `IngestService` logic branches in isolation. Uses EF Core
`UseInMemoryDatabase` for tests where a real DB write is needed (avoids complex `DbSet` mock
setup). Uses `Mock<EdiTrackDbContext>` shim for the `DbUpdateException` branch since the
InMemory provider does not throw real DB exceptions.

**Test file**: `tests/EdiTrack.Api.Tests/Unit/Services/IngestServiceTests.cs`

- [ ] T042 Create `tests/EdiTrack.Api.Tests/Unit/Services/IngestServiceTests.cs`; namespace `EdiTrack.Api.Tests.Unit.Services`; add the test class `IngestServiceTests`; add a private helper `static EdiTrackDbContext BuildInMemoryContext() => new EdiTrackDbContext(new DbContextOptionsBuilder<EdiTrackDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);` (unique DB name per call ensures test isolation); add a second helper `static IngestService BuildService(EdiTrackDbContext ctx) => new IngestService(ctx, NullLogger<IngestService>.Instance);`; add required `using` statements for `EdiTrack.Api.Services`, `EdiTrack.Api.Dtos`, `EdiTrack.Api.Infrastructure.Data`, `Microsoft.EntityFrameworkCore`, `Microsoft.Extensions.Logging.Abstractions`, `Moq`, `Xunit`
- [ ] T043 [US1] Write test `IngestAsync_ValidRequest_ReturnsSuccess`: arrange a valid `IngestRequest` with all required fields and `Payload = "ISA*00*..."` (minimal ISA*-prefixed value); act `var result = await service.IngestAsync(request)`; assert `result` is `IngestResult.Success`; cast and assert `s.Acknowledgment.TransactionId != Guid.Empty`, `s.Acknowledgment.Status == "Received"`, `s.Acknowledgment.CorrelationId` is not null or whitespace, `s.Acknowledgment.ReceivedAt` is within 5 seconds of `DateTimeOffset.UtcNow`
- [ ] T044 [US1] Write test `IngestAsync_CorrelationIdOmitted_GeneratesOne`: arrange `IngestRequest` with `CorrelationId = null` and a valid ISA*-prefixed payload; act `IngestAsync`; assert result is `IngestResult.Success`; assert `s.Acknowledgment.CorrelationId` is not null and not empty (the service must have generated a value)
- [ ] T045 [US2] Write test `IngestAsync_EmptyPayload_ReturnsValidationFailure`: arrange `IngestRequest` with `Payload = ""` (construct directly to bypass the `[Required]` attribute — the service must also handle this defensively); act `IngestAsync`; assert result is `IngestResult.ValidationFailure`; assert `v.Message` is not empty; assert `context.Transactions.Count() == 0` (no record written)
- [ ] T046 [US2] Write test `IngestAsync_NonEdiPayload_ReturnsValidationFailure`: arrange `IngestRequest` with `Payload = "not a valid EDI document"`; act `IngestAsync`; assert result is `IngestResult.ValidationFailure`; assert `context.Transactions.Count() == 0`
- [ ] T047 [US2] Write test `IngestAsync_PayloadWithLeadingWhitespace_AcceptsWhenEdiShaped`: arrange `IngestRequest` with `Payload = "  ISA*00*          *00*          *..."` (two leading spaces before `ISA*`); act `IngestAsync`; assert result is `IngestResult.Success` — leading whitespace must NOT cause rejection (spec edge case: `TrimStart()` before `ISA*` check)
- [ ] T048 [US2] Write test `IngestAsync_DbUpdateException_ReturnsPersistenceFailure`: build a `Mock<EdiTrackDbContext>` configured with `Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new DbUpdateException("simulated", new Exception()))`; also mock `Transactions` property to return a mocked `DbSet<EdiTransaction>` so `Add()` does not throw; construct `IngestService` with the mocked context; act `IngestAsync` with a valid ISA*-prefixed request; assert result is `IngestResult.PersistenceFailure`; assert `p.Message` contains "transient"
- [ ] T049 [US2] Write test `IngestAsync_ValidationFailure_NoDbSave`: use the same `Mock<EdiTrackDbContext>` setup as T048; act `IngestAsync` with `Payload = "not EDI"`; assert result is `IngestResult.ValidationFailure`; assert `mock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never)` — `SaveChangesAsync` must NEVER be called when validation fails before persistence
- [ ] T050 [US3] Write test `IngestAsync_Success_LogsExpectedFields`: create `Mock<ILogger<IngestService>>`; build service with the mock logger; act `IngestAsync` with a valid request; assert `mockLogger.Verify(x => x.Log(LogLevel.Information, It.IsAny<EventId>(), It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Accepted")), null, It.IsAny<Func<It.IsAnyType, Exception?, string>>()))` (or equivalent structured log verification); additionally assert that NO `Log` call on the mock contains the literal payload string value (payload must not appear in any log)
- [ ] T051 Run `dotnet test tests/EdiTrack.Api.Tests --filter "FullyQualifiedName~Unit"`; confirm all 8 unit tests pass; if any fail, fix the service or test before proceeding to integration tests; run `dotnet build --warnaserror` to confirm no warnings were introduced

**Checkpoint**: All 8 unit tests pass. Every `IngestService` branch (success, validation failure, persistence failure, whitespace edge case, correlation ID generation, logger calls, no DB save on validation) is covered and verified.

---

## Phase 8: Integration Tests — POST /api/ingest (xUnit + Testcontainers + WebApplicationFactory)

**Purpose**: End-to-end tests against a real PostgreSQL instance managed by Testcontainers.
`WebApplicationFactory<Program>` boots the full app with the Testcontainers connection string
injected. Tests require Docker to be running but do NOT require `docker compose up` — the
Testcontainers container lifecycle is fully self-contained within `dotnet test`.

- [ ] T052 Create `tests/EdiTrack.Api.Tests/Helpers/PostgresFixture.cs`; namespace `EdiTrack.Api.Tests.Helpers`; implement `IAsyncLifetime`; in `InitializeAsync`: build and start `_pgContainer = new PostgreSqlBuilder().WithDatabase("editrack_test").WithUsername("test").WithPassword("test").Build(); await _pgContainer.StartAsync();`; then build `_factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseSetting("DATABASE_URL", _pgContainer.GetConnectionString()).ConfigureServices(services => { var sp = services.BuildServiceProvider(); using var scope = sp.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<EdiTrackDbContext>(); db.Database.EnsureCreated(); }));`; expose `public HttpClient CreateClient() => _factory.CreateClient()` and `public EdiTrackDbContext CreateDbContext() => _factory.Services.CreateScope().ServiceProvider.GetRequiredService<EdiTrackDbContext>()`; in `DisposeAsync`: `await _pgContainer.StopAsync(); _factory.Dispose();`
- [ ] T053 Create `tests/EdiTrack.Api.Tests/Integration/IngestEndpointTests.cs`; namespace `EdiTrack.Api.Tests.Integration`; declare `public class IngestEndpointTests : IClassFixture<PostgresFixture>`; constructor receives `PostgresFixture fixture`; add a static valid request body constant: `private const string ValidBody = "{\"senderId\":\"ACME\",\"receiverId\":\"GLOBEX\",\"transactionType\":\"850\",\"payload\":\"ISA*00*          *00*          *ZZ*ACME           *ZZ*GLOBEX         *250101*0900*^*00501*000000001*0*P*>~\"}";`
- [ ] T054 [US1] Write integration test `PostIngest_ValidRequest_Returns200WithAcknowledgment` in `tests/EdiTrack.Api.Tests/Integration/IngestEndpointTests.cs`: POST `ValidBody` to `/api/ingest`; assert `response.StatusCode == HttpStatusCode.OK`; deserialize body as `IngestAcknowledgment`; assert `ack.TransactionId != Guid.Empty`, `ack.CorrelationId` not null/empty, `ack.SenderId == "ACME"`, `ack.ReceiverId == "GLOBEX"`, `ack.TransactionType == "850"`, `ack.Status == "Received"`, `ack.ReceivedAt` is within 60 seconds of UTC now
- [ ] T055 [US1] Write integration test `PostIngest_ValidRequest_PersistsTransaction` in `tests/EdiTrack.Api.Tests/Integration/IngestEndpointTests.cs`: POST `ValidBody`; assert `200 OK`; using `fixture.CreateDbContext()` assert `await db.Transactions.CountAsync() == 1`; assert the stored record's `SenderId == "ACME"` and `Payload` equals the submitted payload string verbatim
- [ ] T056 [US1] Write integration test `PostIngest_CorrelationIdOmitted_GeneratedInResponse` in `tests/EdiTrack.Api.Tests/Integration/IngestEndpointTests.cs`: POST a body without `correlationId` field; assert `200 OK`; deserialize and assert `ack.CorrelationId` is not null and not empty (server-generated UUID)
- [ ] T057 [US2] Write integration test `PostIngest_MissingRequiredField_Returns400` in `tests/EdiTrack.Api.Tests/Integration/IngestEndpointTests.cs`: POST `{"receiverId":"GLOBEX","transactionType":"850","payload":"ISA*..."}` (no `senderId`); assert `response.StatusCode == HttpStatusCode.BadRequest`; deserialize body as `ErrorResponse`; assert `err.Message` is not empty; assert `err.Errors` is not null; assert `err.Errors.ContainsKey("senderId")`
- [ ] T058 [US2] Write integration test `PostIngest_EmptyPayload_Returns400` in `tests/EdiTrack.Api.Tests/Integration/IngestEndpointTests.cs`: POST `{"senderId":"ACME","receiverId":"GLOBEX","transactionType":"850","payload":""}` (empty string triggers `[MinLength(1)]` failure); assert `400 Bad Request`; assert body deserializes to `ErrorResponse` shape
- [ ] T059 [US2] Write integration test `PostIngest_NonEdiPayload_Returns400` in `tests/EdiTrack.Api.Tests/Integration/IngestEndpointTests.cs`: POST `{"senderId":"ACME","receiverId":"GLOBEX","transactionType":"850","payload":"not a valid EDI document"}`; assert `400 Bad Request`; deserialize as `ErrorResponse`; assert `err.Message == "Payload does not resemble an EDI X12 document."` and `err.Errors == null` (service-level rejection returns message only, no field-level errors)
- [ ] T060 [US2] Write integration test `PostIngest_RejectedRequest_LeavesNoDbRecord` in `tests/EdiTrack.Api.Tests/Integration/IngestEndpointTests.cs`: POST a non-EDI payload; assert `400`; using `fixture.CreateDbContext()` assert `await db.Transactions.CountAsync() == 0` — no partial record must exist after any rejection
- [ ] T061 Run `dotnet test` (all tests — unit + integration); confirm all tests pass (8 unit + 7 integration = 15 total); commit all test files; run `dotnet build --warnaserror` to confirm zero warnings after all additions; verify `dotnet format --verify-no-changes` passes (no uncommitted formatting drift)

**Checkpoint**: All 7 integration tests pass against a real Testcontainers-managed PostgreSQL. All 15 tests pass in total. `dotnet test` is a single-command, zero-prerequisites operation. Full feature is end-to-end verified and CI-ready.

---

## Dependencies & Execution Order

### Phase Dependencies

| Phase | Depends On | Blocks |
|---|---|---|
| Phase 1 (Setup) | Nothing | Phase 2 |
| Phase 2 (Foundational) | Phase 1 | Phases 3, 4, 5 |
| Phase 3 (US1) | Phase 2 | Phases 4, 5 |
| Phase 4 (US2) | Phase 3 (service + controller must exist) | Phase 7, 8 |
| Phase 5 (US3) | Phase 3 (service must exist to add logging) | Phase 7, 8 |
| Phase 6 (Polish) | Phases 3–5 | Phase 8 |
| Phase 7 (Unit Tests) | Phase 5 (service fully implemented incl. logging) | Phase 8 |
| Phase 8 (Integration Tests) | Phase 6 (full stack: Program.cs, DI wiring complete) | — |

### User Story Dependencies

- **US1 (P1)**: Starts after Phase 2. No dependency on US2 or US3.
- **US2 (P2)**: Depends on US1 service and controller scaffolding existing (adds error-handling paths to existing code and validates rejection behavior)
- **US3 (P3)**: Depends on US1 service existing (adds logging calls to existing `IngestService` methods)

### Parallel Opportunities

```bash
# Phase 1 — run in parallel:
T004  Create compose.yml
T005  Create .env.example

# Phase 2 — run in parallel after T008 (NuGet restore):
T009  Create TransactionStatus.cs
T015  Create IngestRequest.cs
T016  Create IngestAcknowledgment.cs
T017  Create ErrorResponse.cs

# Phase 6 — run in parallel after Phases 3–5:
T037  Verify OpenAPI + Scalar
T038  Update .http file
T040  Create cd.yml stub
```

---

## Implementation Strategy

### MVP Scope — User Story 1 Only (~2–3 hours)

1. Complete Phase 1 (Setup) — T001–T007
2. Complete Phase 2 (Foundational) — T008–T017
3. Complete Phase 3 (US1) — T018–T023
4. **STOP and VALIDATE**: `POST /api/ingest` accepts a valid submission, returns `200`, record is in Postgres
5. Deploy or demo — US1 alone is a shippable increment per the spec's MVP definition

### Incremental Delivery

| Iteration | Phases | What's Deliverable |
|---|---|---|
| 1 | 1 + 2 + 3 | Valid EDI submissions accepted (US1 — MVP) |
| 2 | + 4 | Invalid submissions safely rejected with structured `400`/`503` (US2) |
| 3 | + 5 | Every attempt traceable via structured logs (US3) |
| Final | + 6 + 7 + 8 | Health endpoint, CI gate, tests green, OpenAPI accurate |

### Single-Developer Recommended Sequence

```
T001 → T002 → T003 → T004 → T005 → T006 → T007
 → T008 → T009 → T010 → T011 → T012 → T013 → T014
 → T015 → T016 → T017
 → T018 → T019 → T020 → T021 → T022 → T023   ← MVP complete
 → T024 → T025 → T026 → T027 → T028
 → T029 → T030 → T031 → T032 → T033 → T034 → T035
 → T036 → T037 → T038 → T039 → T040 → T041
 → T042 → T043 → T044 → T045 → T046 → T047 → T048 → T049 → T050 → T051
 → T052 → T053 → T054 → T055 → T056 → T057 → T058 → T059 → T060 → T061
```

---

## Notes

- `[P]` tasks = different files, no data dependencies — safe to parallelize within the same phase
- `[Story]` label maps each task to a specific user story for traceability and independent delivery
- Constitution Principle III: tests are NON-NEGOTIABLE for this project. `dotnet test` must pass before merging to `main`
- `dotnet build --warnaserror` must produce zero warnings at every checkpoint (constitution constraint)
- Secrets: `appsettings.Development.json` connection string (local-dev-only) is committed; production and CI use `DATABASE_URL` env var — never commit real credentials
- Log safety: `request.Payload` must NEVER appear in any `_logger.Log*` call — verified by T050 and required by FR-013; audit both `IngestService.cs` and `IngestController.cs` before merge
- Phase 3 (US1) marks the MVP: after T023 passes, the feature is independently shippable
- Testcontainers requires Docker to be running for integration tests (`docker compose up` is NOT required — only the Docker daemon itself)
