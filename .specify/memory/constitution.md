<!--
  SYNC IMPACT REPORT
  ==================
  Version change: (new) → 1.0.0
  Modified principles: N/A — initial ratification
  Added sections:
    - I.   Clean Code (C# and .NET)
    - II.  API Contract Ownership
    - III. Testing is Required (NON-NEGOTIABLE)
    - IV.  Structured Logging and Observability
    - V.   Security Baseline
    - VI.  Minimal and Justified Dependencies
    - VII. CI Pipeline (GitHub Actions)
    - Solution Structure
    - Tech Stack
    - Development Workflow
    - Governance
  Removed sections: N/A — initial ratification
  Templates updated:
    - ✅ .specify/templates/plan-template.md — Constitution Check gates updated
    - ✅ .specify/templates/spec-template.md — Tech context defaults updated
    - ✅ .specify/templates/tasks-template.md — Path conventions updated
  Deferred TODOs: None
-->

# EdiTrack API Constitution

## Core Principles

### I. Clean Code (C# and .NET)

Code MUST be readable, well-structured, and immediately understandable by any contributor.

- Classes, methods, and interfaces MUST be small and single-purpose. No method shall exceed
  30 lines without documented justification in a code comment.
- Names MUST reveal intent. Abbreviations are forbidden except for universally accepted
  acronyms (e.g. `Id`, `Dto`, `Http`).
- Abstractions MUST be justified by concrete reuse. Speculative interfaces or base classes
  MUST NOT be introduced.
- `async`/`await` MUST be used consistently for all I/O operations. Blocking calls
  (`.Result`, `.Wait()`) are forbidden.
- Dead code MUST NOT be committed. Commented-out code blocks MUST NOT exist in production
  paths.
- Controller actions and non-trivial service methods MUST have XML doc comments describing
  intent and edge cases — not restating the signature. DTOs with self-documenting property
  names are exempt.

### II. API Contract Ownership

`editrack-api` owns and defines the API contract for all consumers, including `spec-lab`.
The following rules are non-negotiable.

- All request and response shapes MUST be defined as explicit DTO classes in a dedicated
  `Contracts` or `Dtos` folder. Controllers MUST NOT return entity or domain models directly.
- OpenAPI documentation MUST be generated using `Microsoft.AspNetCore.OpenApi`. The Scalar
  UI (`Scalar.AspNetCore`) MUST be mounted and accessible at `/scalar` in development. The
  raw OpenAPI JSON MUST be accessible at `/openapi/v1.json`.
- Every controller action MUST declare its response types explicitly using
  `[ProducesResponseType]` attributes. These feed the OpenAPI spec and MUST be kept accurate.
- CORS MUST be explicitly configured to allow requests from the `spec-lab` frontend origin,
  controlled via the `ALLOWED_CORS_ORIGINS` environment variable. Wildcard CORS (`*`) is
  forbidden in non-development environments.
- API versioning is out of scope for MVP, but the project structure MUST NOT preclude it.
  Controllers MUST be namespaced to support future versioning without architectural changes.
- Error responses MUST follow a consistent error envelope shape across all endpoints.
  Unstructured or framework-default error responses MUST NOT be exposed to consumers.

### III. Testing is Required (NON-NEGOTIABLE)

This principle supersedes any conflicting guidance. Testing is a first-class citizen of this
project and MUST NOT be deferred, descoped, or removed.

- Every non-trivial service and utility class MUST have unit tests.
- xUnit is the required test framework.
- Moq is the required mocking library.
- Integration tests MUST cover all critical API flows (happy path and primary failure modes)
  using `WebApplicationFactory`.
- Test projects MUST live in `/tests` at the repo root, mirroring `/src` with a `.Tests`
  suffix (e.g. `EdiTrack.Api.Tests` mirrors `EdiTrack.Api`).
- Tests MUST run and pass as part of every CI pipeline execution. A failing test blocks merge.
- No feature is considered complete until its tests are green.
- Code coverage reporting is encouraged but is not a merge gate. Correctness of tests matters
  more than coverage percentage.

### IV. Structured Logging and Observability

The API MUST be observable by default. Logging MUST be structured, consistent, and safe.

- Serilog is the required logging library. The `ILogger` interface from
  `Microsoft.Extensions.Logging` MUST be used at all injection sites so the implementation
  can be swapped without code changes.
- Every request MUST emit a structured log entry containing: HTTP method, path, status code,
  and duration.
- Sensitive data (passwords, tokens, PII) MUST NEVER appear in log output. Sanitisation is
  the responsibility of the service layer, not the caller.
- A `GET /health` endpoint MUST exist and return a structured health status response.
- Correlation IDs MUST be supported for request tracing across service boundaries.

### V. Security Baseline

Security hygiene MUST be applied by default, not retrofitted.

- Secrets MUST NEVER be hardcoded. All credentials, connection strings, and API keys MUST
  be read from environment variables or a secrets manager. No secrets in `appsettings.json`
  or any committed configuration file.
- A `.env.example` file MUST exist at the repo root so any contributor can onboard locally
  without out-of-band communication.
- Input validation MUST be applied at the controller boundary using data annotations or
  FluentValidation. Controllers MUST reject invalid input before it reaches service logic.
- HTTPS redirection MUST be enabled. HTTP-only operation is forbidden in non-development
  environments.
- Authentication and authorisation are out of scope for MVP, but the middleware pipeline MUST
  be structured to accommodate them without architectural rework.

### VI. Minimal and Justified Dependencies

Dependencies MUST be deliberate. The existing stack covers the majority of needs.

- Before adding any NuGet package, the contributor MUST verify the requirement cannot be
  satisfied by the .NET BCL, ASP.NET Core built-ins, or the approved stack.
- New packages MUST be documented at the point of proposal in the relevant feature spec
  before they are installed.
- Transitive dependency risk MUST be considered for any new package addition.

### VII. CI Pipeline (GitHub Actions)

Continuous integration runs via GitHub Actions workflows at `.github/workflows/` at the
repo root. This is a GitHub requirement and workflows MUST NOT be placed anywhere else.

- A `ci.yml` workflow MUST exist at `.github/workflows/ci.yml`.
- The CI workflow MUST trigger on every pull request targeting `main` and on every push
  to `main`.
- The CI workflow MUST execute in order:
  1. `dotnet restore`
  2. `dotnet build --no-restore --warnaserror`
  3. `dotnet test --no-build`
  4. `dotnet format --verify-no-changes`
- A failing step MUST block merge. No exceptions.
- A `cd.yml` workflow stub MUST exist at `.github/workflows/cd.yml` with a comment
  documenting that production deployment via ArgoCD and Kubernetes is deferred to a future
  phase. The stub MUST NOT contain active deployment steps.

## Solution Structure

The following structure is mandated and MUST NOT deviate without a constitution amendment.
All production C# projects MUST live under `/src`. All test projects MUST live under
`/tests`. The `.github/workflows/` directory MUST remain at the repo root.

```text
editrack-api/                        ← repo root
  .github/
    workflows/
      ci.yml
      cd.yml
  src/
    EdiTrack.Api/                    ← Web API (current)
    EdiTrack.Core/                   ← domain logic (future)
    EdiTrack.Infrastructure/         ← data access (future)
  tests/
    EdiTrack.Api.Tests/              ← mirrors EdiTrack.Api
    EdiTrack.Core.Tests/             ← mirrors EdiTrack.Core (future)
    EdiTrack.Infrastructure.Tests/   ← mirrors EdiTrack.Infrastructure (future)
  EdiTrack.sln
```

## Tech Stack

The following stack is pinned. Changes MUST NOT be made without a constitution amendment.

| Layer           | Technology                   | Role                         |
|-----------------|------------------------------|------------------------------|
| Framework       | ASP.NET Core (.NET 10)       | Web API host                 |
| Language        | C# 13                        | Primary language             |
| ORM             | Entity Framework Core        | Primary data access          |
| Micro-ORM       | Dapper                       | Performance-critical queries |
| Database        | PostgreSQL                   | Primary datastore            |
| Logging         | Serilog                      | Structured logging           |
| API Docs (spec) | Microsoft.AspNetCore.OpenApi | OpenAPI spec generation      |
| API Docs (UI)   | Scalar.AspNetCore            | OpenAPI UI at `/scalar`      |
| Testing         | xUnit + Moq                  | Unit and integration tests   |
| Containerisation| Docker + docker-compose      | Local development            |
| CI              | GitHub Actions               | Build, test, lint            |

Environment variables (MUST NOT be hardcoded anywhere):

| Variable              | Purpose                                     |
|-----------------------|---------------------------------------------|
| `DATABASE_URL`        | PostgreSQL connection string                |
| `ALLOWED_CORS_ORIGINS`| Comma-separated list of allowed UI origins  |

## Development Workflow

- Features MUST be developed on branches named `###-feature-name`
  (sequential number prefix).
- Every feature MUST have a spec in `specs/###-feature-name/` before implementation begins.
- `dotnet build --warnaserror` MUST produce zero errors and zero warnings before any commit
  is merged.
- `dotnet test` MUST pass with zero failures before any commit is merged.
- `dotnet format --verify-no-changes` MUST produce no diffs before any commit is merged.
- All C# project and namespace naming MUST follow PascalCase dotted convention
  (e.g. `EdiTrack.Api`, `EdiTrack.Core`).
- `Console.Write` or `Debug.Write` calls MUST NOT exist in production paths. Use `ILogger`
  exclusively.

## Governance

This constitution supersedes all other project guidance for `editrack-api`. Conflicts between
a feature spec, plan, or task list and this constitution resolve in favour of the
constitution.

**Amendment procedure**:

1. Propose the change with written rationale in a feature spec or standalone document.
2. Update `.specify/memory/constitution.md`.
3. Increment the version number per semantic versioning:
   - MAJOR for removals or redefinitions of existing principles.
   - MINOR for additions of new principles or sections.
   - PATCH for clarifications, wording, or typo fixes.
4. Propagate changes to all affected templates and include a Sync Impact Report header.
5. Commit with message: `docs: amend constitution to vX.Y.Z (summary)`

**Version**: 1.0.0 | **Ratified**: 2025-07-23 | **Last Amended**: 2025-07-23
