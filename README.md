# EdiTrack API

EdiTrack is a production-grade EDI X12 transaction ingest platform built on ASP.NET Core. It accepts raw EDI payloads from integration operators and upstream systems, durably stores each accepted transaction, and returns a structured acknowledgment with a stable transaction ID and correlation ID — making every ingest attempt fully traceable through structured logs.

---

## Table of Contents

- [How This Project Is Built — Spec-Kit](#how-this-project-is-built--spec-kit)
- [Tech Stack](#tech-stack)
- [Project Structure](#project-structure)
- [Local Setup](#local-setup)
- [Running the API](#running-the-api)
- [API Reference](#api-reference)
- [Environment Variables](#environment-variables)
- [Running Tests](#running-tests)
- [Database Migrations](#database-migrations)
- [CI / CD](#ci--cd)
- [Feature Development Workflow](#feature-development-workflow)

---

## How This Project Is Built — Spec-Kit

EdiTrack uses **[GitHub Spec-Kit](https://github.com/marketplace/spec-kit)** — a spec-driven development library for GitHub Copilot. Every feature follows a structured pipeline before a single line of code is written:

```
specify → clarify → plan → tasks → analyze → implement
```

| Spec-Kit Command | What It Does |
|---|---|
| `/speckit.specify` | Generates a full feature spec (`spec.md`) from a natural-language description |
| `/speckit.clarify` | Surfaces ambiguities and encodes answers back into the spec |
| `/speckit.plan` | Produces an implementation plan and quickstart guide from the clarified spec |
| `/speckit.tasks` | Breaks the plan into a dependency-ordered `tasks.md` |
| `/speckit.implement` | Executes each task, committing code iteratively |
| `/speckit.analyze` | Cross-checks spec, plan, and tasks for consistency before or after implementation |
| `/speckit.checklist` | Generates a feature-specific QA checklist |
| `/speckit.taskstoissues` | Converts tasks into GitHub Issues for project tracking |

### Feature Artifacts

Each feature lives under `specs/<###-feature-name>/` and contains:

```
specs/
└── 001-edi-transaction-ingest/
    ├── spec.md          # Source of truth: requirements, acceptance scenarios, decisions
    ├── plan.md          # Implementation design and architecture decisions
    ├── tasks.md         # Ordered task list executed by /speckit.implement
    ├── quickstart.md    # Local dev guide generated during planning
    ├── data-model.md    # Entity and schema documentation
    ├── research.md      # Background research notes
    └── checklists/      # QA checklists
```

> The spec always comes first. No implementation work begins until `spec.md` is clarified and approved.

---

## Tech Stack

| Layer | Technology |
|---|---|
| Runtime | C# 13 / .NET 10 |
| Web Framework | ASP.NET Core (Minimal + Controller APIs) |
| ORM | Entity Framework Core 9 (`Npgsql.EntityFrameworkCore.PostgreSQL`) |
| Database | PostgreSQL 16 |
| Logging | Serilog (structured JSON to stdout) |
| API Docs | `Microsoft.AspNetCore.OpenApi` + Scalar UI |
| Testing | xUnit + Testcontainers (ephemeral Postgres for integration tests) |
| CI | GitHub Actions |

---

## Project Structure

```
editrack-api/
├── src/
│   └── EdiTrack.Api/
│       ├── Controllers/        # HTTP controllers (thin — delegate to services)
│       ├── Domain/
│       │   ├── Entities/       # EF Core entity models
│       │   └── Enums/          # Domain enumerations (e.g., TransactionStatus)
│       ├── Dtos/               # Request / response DTO contracts
│       ├── Infrastructure/     # DbContext, data access configuration
│       ├── Migrations/         # EF Core code-first migrations
│       ├── Services/           # Business logic (IIngestService, IngestService)
│       ├── Program.cs          # App bootstrap and DI configuration
│       └── appsettings*.json   # Configuration
├── tests/
│   └── EdiTrack.Api.Tests/
│       ├── Unit/               # Unit tests (services, validation logic)
│       ├── Integration/        # Integration tests (real Postgres via Testcontainers)
│       └── Helpers/            # Shared test utilities and factories
├── specs/                      # Spec-Kit feature artifacts (spec, plan, tasks)
├── compose.yml                 # Local development dependencies (Postgres only)
├── .env.example                # Environment variable template
└── EdiTrack.sln
```

---

## Local Setup

### Prerequisites

| Tool | Minimum Version | Install |
|---|---|---|
| .NET SDK | 10.0 | https://dot.net/download |
| Docker Desktop (or Docker Engine) | 24+ | https://docs.docker.com/get-docker/ |
| `dotnet-ef` CLI | 9.x | `dotnet tool install --global dotnet-ef` |

### 1. Clone and restore

```bash
git clone https://github.com/mehtaz23/editrack-api.git
cd editrack-api
dotnet restore
```

### 2. Configure environment (optional)

```bash
cp .env.example .env
# Defaults in appsettings.Development.json work for standard local dev.
# Edit .env only if you need to override DATABASE_URL or ALLOWED_CORS_ORIGINS.
```

### 3. Start PostgreSQL

The application runs natively via `dotnet run`; only the database runs in Docker.

```bash
docker compose up -d
# Verify it is healthy:
docker compose ps
# editrack-postgres   running (healthy)
```

### 4. Apply database migrations

```bash
dotnet ef database update \
  --project src/EdiTrack.Api \
  --startup-project src/EdiTrack.Api
```

---

## Running the API

```bash
# Standard run
dotnet run --project src/EdiTrack.Api

# With hot reload (recommended during active development)
dotnet watch run --project src/EdiTrack.Api
```

The API listens on:
- **HTTP**: http://localhost:5250
- **HTTPS**: https://localhost:7200 *(requires `dotnet dev-certs https --trust`)*

**Interactive API docs (Scalar UI):** http://localhost:5250/scalar

---

## API Reference

### `POST /api/ingest`

Submits a raw EDI X12 payload for durable storage. Returns a structured acknowledgment with a stable transaction ID.

**Request body** (`Content-Type: application/json`):

```json
{
  "senderId":       "ACME",
  "receiverId":     "GLOBEX",
  "transactionType": "850",
  "correlationId":  "optional-caller-provided-id",
  "payload":        "ISA*00*          *00*          *ZZ*ACME..."
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `senderId` | string | ✅ | Sender identifier |
| `receiverId` | string | ✅ | Receiver identifier |
| `transactionType` | string | ✅ | EDI transaction set type (e.g., `850`, `856`) |
| `correlationId` | string | ❌ | Caller-supplied trace ID; auto-generated if omitted |
| `payload` | string | ✅ | Raw EDI X12 document content |

**Success — `200 OK`:**

```json
{
  "transactionId":   "019241f9-a1b2-7c3d-8e4f-5a6b7c8d9e0f",
  "correlationId":   "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "senderId":        "ACME",
  "receiverId":      "GLOBEX",
  "transactionType": "850",
  "receivedAt":      "2026-04-24T14:32:01.123456Z",
  "status":          "Received"
}
```

**Validation error — `400 Bad Request`:**

```json
{
  "message": "One or more validation errors occurred.",
  "correlationId": "3fa85f64-...",
  "errors": {
    "senderId": ["The senderId field is required."]
  }
}
```

**Persistence failure — `503 Service Unavailable`** *(transient; safe to retry)*:

```json
{
  "message": "A transient error occurred. The submission was not stored. Please retry."
}
```

---

### `GET /health`

```bash
curl http://localhost:5250/health
# "Healthy"
```

---

### Quick curl example

```bash
curl -s -X POST http://localhost:5250/api/ingest \
  -H "Content-Type: application/json" \
  -d '{
    "senderId": "ACME",
    "receiverId": "GLOBEX",
    "transactionType": "850",
    "payload": "ISA*00*          *00*          *ZZ*ACME           *ZZ*GLOBEX         *250101*0900*^*00501*000000001*0*P*>~"
  }' | jq .
```

---

## Environment Variables

| Variable | Default | Description |
|---|---|---|
| `DATABASE_URL` | *(see appsettings.Development.json)* | Full Npgsql connection string. Overrides `ConnectionStrings:DefaultConnection`. |
| `ALLOWED_CORS_ORIGINS` | `http://localhost:3000` | Comma-separated list of allowed CORS origins. |

Default development connection string (set in `appsettings.Development.json`):
```
Host=localhost;Port=5432;Database=editrack;Username=editrack;Password=editrack_dev
```

---

## Running Tests

```bash
# All tests (unit + integration)
dotnet test

# Verbose output
dotnet test --logger "console;verbosity=detailed"

# Specific project only
dotnet test tests/EdiTrack.Api.Tests
```

> **Integration tests** use [Testcontainers](https://dotnet.testcontainers.org/) to spin up an ephemeral Postgres instance automatically. You do **not** need `docker compose up` running — but Docker must be running.

---

## Database Migrations

```bash
# Add a new migration after changing the EF Core data model
dotnet ef migrations add <MigrationName> \
  --project src/EdiTrack.Api \
  --startup-project src/EdiTrack.Api

# Apply to local Postgres
dotnet ef database update \
  --project src/EdiTrack.Api \
  --startup-project src/EdiTrack.Api

# Preview the SQL without applying
dotnet ef migrations script \
  --project src/EdiTrack.Api \
  --startup-project src/EdiTrack.Api
```

Always commit the generated files under `src/EdiTrack.Api/Migrations/`.

---

## CI / CD

### CI (`ci.yml`) — runs on every push and PR to `main`

| Step | Command |
|---|---|
| Restore | `dotnet restore` |
| Build | `dotnet build --warnaserror` |
| Test | `dotnet test` |
| Format check | `dotnet format --verify-no-changes` |

### CD (`cd.yml`)

Production deployment via ArgoCD/Kubernetes is deferred to a future phase. The CD workflow currently serves as a placeholder on merges to `main`.

---

## Feature Development Workflow

New features in EdiTrack follow the Spec-Kit pipeline. All feature branches use the naming convention `###-feature-name` (e.g., `002-transaction-query`).

```
1. /speckit.specify   → describe the feature in natural language
2. /speckit.clarify   → resolve ambiguities, lock down decisions
3. /speckit.plan      → generate implementation plan + quickstart
4. /speckit.tasks     → produce dependency-ordered task list
5. /speckit.analyze   → verify spec ↔ plan ↔ tasks consistency
6. /speckit.implement → execute tasks, commit iteratively
7. Open PR → CI passes → merge to main
```

Each step auto-commits via the configured Git hooks in `.specify/extensions.yml`, keeping the spec artifacts in sync with the implementation branch throughout the cycle.

### Conventions

- All request/response shapes **must** be defined as explicit DTO classes under `Dtos/`.
- Testing is **non-negotiable** — every feature must include both unit and integration test coverage.
- New NuGet packages must be **documented in the feature spec** before installation.
- Log output must **never** contain raw EDI payload content or sensitive business data.
