# Research: EDI Transaction Ingest

*Phase 0 output for `/speckit.plan` — all NEEDS CLARIFICATION items resolved.*

---

## 1. UUID v7 in .NET 10

**Decision**: Use `Guid.CreateVersion7()` built into .NET 9+.

**Rationale**: `Guid.CreateVersion7()` produces time-ordered UUIDs (UUID v7) which
reduce B-tree index fragmentation on PostgreSQL's `uuid`-typed PK column because new
values are monotonically increasing in time order. This is a BCL built-in — no NuGet
package required. The method is available from .NET 9 onwards and the project targets
.NET 10.

**Alternatives considered**:
- `Guid.NewGuid()` (UUID v4): random, causes index fragmentation — rejected.
- External packages (e.g., `UUIDNext`, `Medo.Uuid7`): unnecessary given BCL coverage.
- Sequential GUIDs via EF Core `ValueGeneratedOnAdd` with a PostgreSQL function: adds
  infrastructure complexity without benefit over `Guid.CreateVersion7()`.

---

## 2. EF Core Version Selection (.NET 10)

**Decision**: EF Core 9.x with Npgsql 9.x provider.

**Rationale**: EF Core 10 is in preview as of the feature branch date. The stable release
is EF Core 9.x, which is fully supported and compatible with .NET 10 (EF Core targets
`netstandard2.0` / `net6.0+` and runs on any .NET version ≥ its minimum target).
`Npgsql.EntityFrameworkCore.PostgreSQL` 9.x is the production-stable provider for
PostgreSQL against EF Core 9.

**Alternatives considered**:
- EF Core 10 preview: provides new features but is not production-stable; upgrade deferred.
- Dapper: fast for read queries (in-scope for future features) but requires manual schema
  management — not the right tool for write-path entities with migrations.

---

## 3. Testcontainers vs Compose Postgres for Integration Tests

**Decision**: Testcontainers.PostgreSql for integration tests.

**Rationale**:

| Concern | Testcontainers | Compose |
|---|---|---|
| Self-contained | Yes — `dotnet test` has zero external pre-conditions | No — developer must `docker compose up` first |
| CI compatibility | Works on any Docker-enabled runner (GitHub Actions `ubuntu-latest`) | Requires compose as a CI pre-step |
| Test isolation | Fresh ephemeral container per test class | Shared Postgres state across test runs |
| Dev workflow | Transparent to the developer | Adds a mental "did I start it?" step |

The `compose.yml` remains for interactive local development (running the app, inspecting
data with a client, validating migrations). Integration tests are fully autonomous.

**Alternatives considered**:
- EF Core InMemory provider: useful for unit tests of service logic but does not exercise
  real SQL, constraint enforcement, or the Npgsql driver — not adequate for integration
  tests.
- Respawn / database snapshot reset: adds complexity over a per-class Testcontainers
  fixture; overkill for this feature's test volume.

---

## 4. EDI X12 Minimal Validation Rule

**Decision**: `payload.AsSpan().TrimStart()` starts with `ISA*` (case-sensitive).

**Rationale**: Every valid EDI X12 interchange begins with the ISA (Interchange Control
Header) segment, with fields delimited by `*`. Checking for the `ISA*` prefix is the
lightest possible check that distinguishes "something that looks like EDI X12" from "an
empty string or clearly non-EDI content". The spec explicitly defers deep segment parsing
to a future feature.

Case-sensitivity: The ISA header is uppercase by the X12 standard. Case-insensitive
matching would be overly permissive and inconsistent with the standard.

Whitespace: The spec edge-case notes that leading/trailing whitespace should not cause
rejection of otherwise valid content. `TrimStart()` before the prefix check handles this.

**Alternatives considered**:
- Regex `^\\s*ISA\\*`: equivalent but adds overhead for a simple prefix check.
- Full X12 parser (e.g., `EdiFabric`, `EdiWeave`): correct for future parsing but out of
  scope and adds a significant commercial/licensing dependency.
- No structural check (accept any non-empty payload): rejected — spec FR-003 requires
  rejecting content "that does not resemble an EDI X12 document".

---

## 5. `IngestResult` Discriminated Union Pattern

**Decision**: Nested abstract record hierarchy inside `Services/`:

```csharp
public abstract record IngestResult
{
    public sealed record Success(IngestAcknowledgment Acknowledgment) : IngestResult;
    public sealed record ValidationFailure(string Message) : IngestResult;
    public sealed record PersistenceFailure(string Message) : IngestResult;
}
```

**Rationale**: Returns service-layer semantics (not HTTP codes) from `IngestService`.
The controller pattern-matches on the result type and maps to HTTP status codes. This
keeps HTTP concerns in the controller layer and makes `IngestService` independently
testable without HTTP context. C# record types make equality comparison in unit tests
trivial (`Success` records with the same acknowledgment are equal by value).

**Alternatives considered**:
- `OneOf<T1, T2, T3>` NuGet package: equivalent but adds a dependency. The nested record
  approach is BCL-only.
- Throwing exceptions from the service: conflates control flow with error signalling;
  makes unit testing exception-path branches awkward; rejected per Clean Code principle.
- Returning `(T result, string? error)` tuple: less expressive and less type-safe than
  the record hierarchy.

---

## 6. Serilog Configuration Approach

**Decision**: Bootstrap logger before `WebApplication.CreateBuilder`, then reconfigure
via `UseSerilog` with `Configuration` binding. Console sink only for this feature.

**Rationale**: The bootstrap logger catches startup exceptions before the DI container
is built (e.g., missing connection string). Full reconfiguration via
`ReadFrom.Configuration` allows environment-specific log levels via `appsettings.json`
without code changes. JSON formatter on the console sink produces structured output
compatible with log aggregators.

**Alternatives considered**:
- `Microsoft.Extensions.Logging` without Serilog: does not produce structured JSON by
  default; the constitution mandates Serilog.
- Additional sinks (Seq, Application Insights): useful in staging/production but out of
  scope for this local-dev-first feature. Deferred.

---

## 7. `EdiTransaction.CorrelationId` Nullability in DB

**Decision**: `CorrelationId` stored as `NOT NULL` `varchar(100)` in the database.

**Rationale**: The service always resolves a correlation ID before persistence — if the
caller omits it, the service generates one. There is therefore no case where a stored
`EdiTransaction` lacks a `CorrelationId`. Storing it as nullable would require null-checks
throughout downstream query code for no benefit. The DB schema reflects the service
invariant.

**Alternatives considered**:
- Nullable column: allows "no correlation ID" state to be represented at the DB level,
  but this contradicts the service invariant — rejected.

---

## 8. Error Response Shape

**Decision**: Custom `ErrorResponse` DTO replaces ASP.NET Core's default
`ValidationProblemDetails` for both 400 and 503 responses.

**Rationale**: The constitution (Principle II) requires a consistent error envelope across
all endpoints. `ValidationProblemDetails` is verbose, RFC 7807-based, and not the
designed format for this API. A simple `ErrorResponse { Message, Errors? }` shape covers
both validation errors (with field-level detail in `Errors`) and transient failures (with
a plain `Message`). Registered via `ApiBehaviorOptions.InvalidModelStateResponseFactory`
to intercept the automatic 400 response.

**Alternatives considered**:
- Keep `ValidationProblemDetails`: violates the consistent-envelope requirement.
- FluentValidation: adds a dependency; data annotations are sufficient for this feature's
  simple required-field validation rules.
