# Quickstart: Local Development — EDI Transaction Ingest

*Phase 1 output for `/speckit.plan`.*

This guide gets a new contributor from zero to a running EdiTrack API with a working
`POST /api/ingest` endpoint in under 10 minutes.

---

## Prerequisites

| Tool | Minimum Version | Install |
|---|---|---|
| .NET SDK | 10.0 | https://dot.net/download |
| Docker Desktop (or Docker Engine) | 24+ | https://docs.docker.com/get-docker/ |
| `dotnet-ef` CLI tool | 9.x | `dotnet tool install --global dotnet-ef` |

---

## One-Time Setup

### 1. Clone and restore

```bash
git clone https://github.com/mehtaz23/editrack-api.git
cd editrack-api
dotnet restore
```

### 2. Create your local env file (optional)

```bash
cp .env.example .env
# Edit .env if you want to override DATABASE_URL or ALLOWED_CORS_ORIGINS.
# For standard local dev the defaults in appsettings.Development.json are sufficient.
```

### 3. Start the PostgreSQL dependency

```bash
docker compose up -d
```

This starts a single `postgres:16-alpine` container named `editrack-postgres` on port
`5432`. The application itself is **not** containerised.

Verify Postgres is ready:

```bash
docker compose ps
# editrack-postgres  running (healthy)
```

### 4. Apply EF Core migrations

```bash
dotnet ef database update \
  --project src/EdiTrack.Api \
  --startup-project src/EdiTrack.Api
```

This creates the `EdiTransactions` table (and any other schema objects) in the
`editrack` database.

---

## Running the API

```bash
# Standard run
dotnet run --project src/EdiTrack.Api

# Or with hot reload (recommended during active development)
dotnet watch run --project src/EdiTrack.Api
```

The API listens on:
- HTTP: http://localhost:5250
- HTTPS: https://localhost:7200 (requires dev cert: `dotnet dev-certs https --trust`)

---

## Verifying the Endpoint

### Using the Scalar UI

Open http://localhost:5250/scalar in a browser. The `POST /api/ingest` endpoint will be
listed. Use the built-in request builder to send a test submission.

### Using the .http file

Open `src/EdiTrack.Api/EdiTrack.Api.http` in VS Code (REST Client extension) or JetBrains
Rider. The file contains a ready-made `POST /api/ingest` example.

### Using curl

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

Expected response:

```json
{
  "transactionId": "019241f9-a1b2-7c3d-8e4f-5a6b7c8d9e0f",
  "correlationId": "some-generated-id",
  "senderId": "ACME",
  "receiverId": "GLOBEX",
  "transactionType": "850",
  "receivedAt": "2025-07-23T14:32:01.123456Z",
  "status": "Received"
}
```

### Checking the Health Endpoint

```bash
curl http://localhost:5250/health
# "Healthy"
```

---

## Running Tests

```bash
# All tests (unit + integration)
dotnet test

# Verbose output
dotnet test --logger "console;verbosity=detailed"

# A specific test project
dotnet test tests/EdiTrack.Api.Tests
```

> **Note**: Integration tests use Testcontainers to spin up their own ephemeral Postgres.
> You do **not** need to have `docker compose up` running to run `dotnet test`. Docker
> must be running (Docker Desktop / Docker Engine), but no compose setup is required.

---

## Adding a New Migration

When you change the EF Core data model, generate and apply a new migration:

```bash
# Generate
dotnet ef migrations add <MigrationName> \
  --project src/EdiTrack.Api \
  --startup-project src/EdiTrack.Api

# Apply to compose Postgres
dotnet ef database update \
  --project src/EdiTrack.Api \
  --startup-project src/EdiTrack.Api

# Review the SQL without applying
dotnet ef migrations script \
  --project src/EdiTrack.Api \
  --startup-project src/EdiTrack.Api
```

Commit the generated files in `src/EdiTrack.Api/Migrations/`.

---

## Inspecting the Database

Connect to the compose Postgres with any PostgreSQL client:

```
Host:     localhost
Port:     5432
Database: editrack
Username: editrack
Password: editrack_dev
```

Using `psql`:

```bash
psql -h localhost -p 5432 -U editrack -d editrack
# Password: editrack_dev

\dt                         -- list tables
SELECT * FROM "EdiTransactions" LIMIT 10;
```

---

## Stopping the Environment

```bash
# Stop Postgres (preserves data volume)
docker compose stop

# Stop and remove containers (preserves data volume)
docker compose down

# Remove everything including the data volume (fresh start)
docker compose down -v
```

---

## Troubleshooting

| Problem | Fix |
|---|---|
| `docker compose up -d` fails | Ensure Docker Desktop is running |
| `dotnet ef database update` fails with "connection refused" | Run `docker compose up -d` first and wait for the health check to pass |
| Port 5432 already in use | Another Postgres may be running locally; stop it or change the host port in `compose.yml` |
| `dotnet dev-certs https --trust` prompt | Approve the macOS keychain prompt; required for HTTPS profile only |
| Integration tests fail with "Docker not available" | Ensure Docker is running; Testcontainers requires the Docker socket |
