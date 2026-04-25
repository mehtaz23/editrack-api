<!--
  EdiTrack API — Spec Notes:
  - All request/response shapes MUST be defined as DTO classes (Principle II).
  - Testing is NON-NEGOTIABLE — include test acceptance criteria (Principle III).
  - New NuGet packages MUST be documented here before installation (Principle VI).
  - Feature branches MUST follow the `###-feature-name` naming convention.
-->

# Feature Specification: EDI Transaction Ingest

**Feature Branch**: `001-edi-transaction-ingest`  
**Created**: 2026-04-24  
**Status**: Clarified  
**Input**: User description: "Create the first production ingest capability for EdiTrack so callers can submit raw EDI X12 payloads with minimal metadata, receive a durable acknowledgment with a stable transaction ID and correlation ID, and ensure every attempt is traceable through structured logging."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Accept valid EDI submissions (Priority: P1)

As an integration operator or upstream system, I want to submit a raw EDI X12 payload with sender, receiver, and transaction type metadata so that the platform can record the transaction durably and confirm receipt immediately.

**Why this priority**: This is the load-bearing platform entry point. Without a reliable ingest path, no downstream tracking, replay, or audit capability can exist.

**Independent Test**: Submit a minimally well-formed EDI X12 payload with the required metadata and verify the system records the transaction before returning a receipt that includes a stable transaction ID, correlation ID, received timestamp, and status.

**Acceptance Scenarios**:

1. **Given** a caller provides a minimally well-formed EDI X12 payload and all required metadata, **When** the submission is sent to the ingest endpoint, **Then** the system stores the transaction durably and returns a success acknowledgment with the transaction ID, correlation ID, sender ID, receiver ID, transaction type, received timestamp, and status `Received`.
2. **Given** a valid submission does not include a correlation ID, **When** the system accepts the submission, **Then** it generates one and returns the same correlation ID in the response and logs.

---

### User Story 2 - Reject invalid submissions safely (Priority: P2)

As an integration operator or developer, I want invalid submissions to be rejected with a clear structured error so that I can correct the request without wondering whether bad data was stored.

**Why this priority**: Reliable rejection behavior protects data quality and prevents operators from treating malformed submissions as accepted work.

**Independent Test**: Submit requests with missing metadata, an empty payload, or a payload that does not resemble EDI X12 and verify the system returns a structured validation error and creates no transaction record.

**Acceptance Scenarios**:

1. **Given** a caller omits a required metadata field or sends it as empty, **When** the submission reaches the ingest endpoint, **Then** the system returns a structured validation response describing the invalid fields and does not store a transaction.
2. **Given** a caller submits an empty body or content that is clearly not EDI-shaped, **When** validation runs, **Then** the system rejects the submission with a structured `400` response and no durable record is created.

---

### User Story 3 - Trace every ingest attempt (Priority: P3)

As an operator, I want every ingest attempt to be traceable through structured logs so that I can reconstruct outcomes quickly without querying stored transaction data.

**Why this priority**: Observability is part of the feature’s business value because ingest reliability depends on operators being able to audit both accepted and rejected attempts.

**Independent Test**: Trigger one accepted submission and one rejected submission, then confirm each attempt produces a structured log entry with the expected tracing fields and no raw payload content.

**Acceptance Scenarios**:

1. **Given** an ingest attempt succeeds, **When** the attempt is completed, **Then** a structured log entry records the correlation ID, sender ID, receiver ID, transaction type, outcome, and timestamp without raw payload content.
2. **Given** an ingest attempt fails validation, **When** the rejection is returned, **Then** a structured log entry still records the correlation ID, known request metadata, rejection outcome, and timestamp without raw payload content.

---

### Edge Cases

- A caller omits the correlation ID; the system must generate one and use it consistently in the response and logs.
- A caller submits a payload with leading or trailing whitespace around otherwise valid EDI content; validation should assess the actual content rather than reject solely because of surrounding whitespace.
- A caller repeats the same payload after a previous successful ingest; each accepted submission is recorded as its own transaction unless a future deduplication feature says otherwise.
- A caller provides partial metadata and a malformed payload in the same request; the error response should identify validation issues without exposing internal processing details.
- Logging must remain safe even when the payload contains sensitive business data; logs may reference metadata and outcomes only, never the raw document body.
- A PostgreSQL write failure after successful validation must not surface an unhandled exception to the caller; the system must catch persistence failures and return a structured `503 Service Unavailable` response, leaving no transaction record in a partially-written state.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST expose a single ingest endpoint at `POST /api/ingest` that accepts `Content-Type: application/json`. The request body MUST be a JSON object containing the following fields: `senderId` (string, required), `receiverId` (string, required), `transactionType` (string, required), `correlationId` (string, optional), and `payload` (string, required, raw EDI X12 content).
- **FR-002**: The system MUST validate required metadata at the API boundary and reject requests where sender ID, receiver ID, or transaction type is missing or empty.
- **FR-003**: The system MUST reject submissions whose payload is empty or does not resemble an EDI X12 document.
- **FR-004**: The system MUST complete validation before attempting to persist a submission.
- **FR-005**: The system MUST create no transaction record for a rejected submission.
- **FR-006**: For each accepted submission, the system MUST assign a stable transaction identifier, capture the received timestamp, and set the initial transaction status to `Received` (expressed as `TransactionStatus.Received` from the `TransactionStatus` C# enum).
- **FR-007**: The system MUST persist each accepted transaction durably before returning a success acknowledgment.
- **FR-008**: The system MUST store the accepted raw payload verbatim together with sender ID, receiver ID, transaction type, received timestamp, stable transaction ID, and initial status.
- **FR-009**: The system MUST return a structured success acknowledgment for accepted submissions that includes the stable transaction ID, correlation ID, sender ID, receiver ID, transaction type, received timestamp, and current status.
- **FR-010**: The system MUST return structured validation errors for invalid submissions and MUST NOT expose unhandled exceptions or internal implementation details to callers.
- **FR-011**: The system MUST support a correlation ID on every ingest attempt, generating one when the caller does not provide it, and MUST include that value in both the response and traceable operational records.
- **FR-012**: The system MUST emit a structured log entry for every ingest attempt, whether accepted or rejected, including correlation ID, sender ID, receiver ID, transaction type when known, outcome, and timestamp.
- **FR-013**: The system MUST exclude raw payload contents and other sensitive business data from ingest log output.
- **FR-014**: The ingest contract MUST be documented in the API reference in a way that makes request shape, success responses, and validation responses visible to API consumers and aligned with repository contract conventions.
- **FR-015**: The feature MUST include automated tests covering missing required fields, successful ingest through the service flow, valid-ingest API behavior, and invalid-ingest API behavior.
- **FR-016**: If a PostgreSQL write operation fails after validation passes, the system MUST return `503 Service Unavailable` with a structured error body (consistent with the existing validation error shape). The API layer MUST NOT perform automatic retries. Callers MUST treat `503` as a safe-to-retry transient failure and own the retry strategy. The ingest controller action MUST declare `[ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]` alongside existing success and validation response type annotations.

### Key Entities *(include if feature involves data)*

- **Ingest Submission**: The caller-provided request DTO (JSON body to `POST /api/ingest`) containing the fields: `senderId`, `receiverId`, `transactionType`, `correlationId` (optional), and `payload` (raw EDI X12 string).
- **Transaction Record**: The durable stored record created for an accepted submission, including the stable transaction ID (UUID v7, `uuid` column, generated via `Guid.CreateVersion7()`), verbatim raw payload, sender ID, receiver ID, transaction type, received timestamp, correlation ID, and current status (typed as `TransactionStatus` enum, persisted as `varchar(50)` via EF Core `.HasConversion<string>()`).
- **Ingest Acknowledgment**: The structured response returned after validation, either confirming acceptance with transaction details or describing validation failure in a consistent error shape.
- **Ingest Attempt Log**: The operational trace of a submission attempt containing safe metadata, correlation data, outcome, and timestamp for auditing and troubleshooting.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of valid ingest submissions receive a structured acknowledgment in a single request that includes a stable transaction ID, received timestamp, correlation ID, and current status.
- **SC-002**: 100% of submissions missing required metadata or containing an empty or clearly non-EDI-shaped payload are rejected with a structured validation response and leave no stored transaction record.
- **SC-003**: 100% of accepted submissions can be retrieved later with the exact raw payload and submission metadata that were originally provided.
- **SC-004**: 100% of ingest attempts, successful or failed, can be traced by operators using correlation ID and structured log fields without inspecting database records.
- **SC-005**: Under normal operating conditions, valid submissions receive their acknowledgment within 5 seconds of request receipt.

## Assumptions

- The ingest endpoint is the first production entry point for EdiTrack and is intended for API clients, automated upstream systems, and manual testing tools.
- The caller supplies a transaction type hint as part of the request; deeper interpretation or verification of that value is deferred to later features.
- Any non-empty transaction type value may be accepted at ingest as long as the rest of the submission passes minimal validation.
- The initial stored status for an accepted transaction is `Received`, matching the primary happy-path scenario described for this feature.
- Authentication and authorization are intentionally out of scope for this feature and will be addressed by a later security-focused spec.
- Duplicate payload submissions are treated as separate ingest attempts unless a future deduplication requirement is introduced.
- Existing repository conventions for explicit API contract models, documented response variants, and automated test coverage apply to this feature.

## Open Questions

All clarification questions for this feature have been resolved. No open items remain.

| # | Category | Question | Decision | Status |
|---|----------|----------|----------|--------|
| 1 | Interaction & API Contract | How does the caller submit the EDI payload — multipart, raw body, or JSON envelope? | JSON envelope: `POST /api/ingest` with `Content-Type: application/json`; single DTO with `senderId`, `receiverId`, `transactionType`, `correlationId` (optional), `payload` | ✅ Resolved |
| 2 | Domain & Data Model | Which persistence technology and ORM? | PostgreSQL + EF Core via `Npgsql.EntityFrameworkCore.PostgreSQL`; `Payload` stored as `text`; JSONB promotion deferred to a future query feature | ✅ Resolved |
| 3 | Domain & Data Model | Transaction ID generation strategy? | `Guid.CreateVersion7()` (.NET 9+ built-in, no extra package); UUID v7 time-ordered values stored as PostgreSQL native `uuid` column | ✅ Resolved |
| 4 | Domain & Data Model | Transaction status representation — C# enum or raw string? | C# enum `TransactionStatus` with EF Core `.HasConversion<string>()`; stored as `varchar(50)` (e.g., `"Received"`); no extra NuGet package | ✅ Resolved |
| 5 | Edge Cases & Failure Handling | DB write failure contract — HTTP status, retry ownership, error body? | `503 Service Unavailable` + structured JSON error body; no automatic retry in API layer; caller retries; `[ProducesResponseType(503)]` declared on action | ✅ Resolved |

## Technical Decisions

### Persistence

- **Database**: PostgreSQL.
- **ORM**: Entity Framework Core via `Npgsql.EntityFrameworkCore.PostgreSQL` *(new NuGet package — documented per Principle VI)*.
- **Payload column**: The `Payload` column on the `EdiTransaction` entity is stored as PostgreSQL `text` (verbatim EDI string). A future migration may promote it to `jsonb` if structured payload querying becomes a requirement; that decision is deferred until a query-against-payload feature is explicitly specified.
- **Transaction ID strategy**: Transaction IDs are generated using `Guid.CreateVersion7()`, available as a .NET 9+ built-in (no additional NuGet package required). UUID v7 produces time-ordered values, which reduces B-tree index fragmentation on the `EdiTransaction` table. The column is stored in PostgreSQL as `uuid` (native type, not `varchar`).
- **Status representation**: Transaction status is modelled as a C# enum `TransactionStatus` with an initial value of `Received`. EF Core maps this column using `.HasConversion<string>()` so the database column stores a human-readable `varchar(50)` string (e.g., `"Received"`) rather than an integer ordinal. No additional NuGet package is required beyond EF Core.
- **Migrations**: EF Core code-first migrations will be used to manage schema evolution.

### Error Handling & Failure Contract

- **DB write failure — status code**: A failed PostgreSQL write (e.g., connection timeout, transient network error, constraint violation) after validation has passed MUST result in a `503 Service Unavailable` response. This is chosen over `500` because it explicitly signals a transient infrastructure failure that is safe to retry, not a permanent server error caused by the request itself.
- **DB write failure — response body**: The `503` response MUST use a structured error body consistent with the existing validation error shape (i.e., a machine-readable JSON object with a descriptive message field), so callers can handle all failure cases uniformly.
- **Retry ownership**: The API layer MUST NOT perform automatic retries on write failure. Retry logic is the caller's responsibility. The API contract treats `503` as an unambiguous "safe to retry" signal for transient persistence failures.
- **API contract annotation**: The ingest controller action MUST declare `[ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]` alongside existing `200`, `400`, and any other declared response type annotations, keeping the OpenAPI/Swagger contract accurate.

## Clarifications

### Session 2026-04-25

- Q: How does the caller submit the EDI payload? → A: JSON envelope body — `POST /api/ingest` with `Content-Type: application/json`; single DTO with fields: `senderId`, `receiverId`, `transactionType`, `correlationId` (optional), `payload`.
- Q: Persistence technology? → A: PostgreSQL + EF Core via `Npgsql.EntityFrameworkCore.PostgreSQL`; `Payload` stored as `text` with JSONB promotion deferred to a future query-against-payload feature.
- Q: Transaction ID generation strategy? → A: `Guid.CreateVersion7()` (.NET 9+ built-in, no extra package); UUID v7 time-ordered values stored as PostgreSQL native `uuid` column type.
- Q: Transaction status representation — enum or raw string? → A: C# enum `TransactionStatus` with `.HasConversion<string>()` in EF Core; column stored as `varchar(50)` (e.g., `"Received"`); initial value `TransactionStatus.Received`; no extra NuGet package required.
- Q: DB write failure HTTP contract — status code, retry ownership, and error body? → A: `503 Service Unavailable` with a structured JSON error body (consistent with validation error shape); no automatic retry in the API layer; caller treats `503` as a safe-to-retry transient failure; `[ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]` declared on the ingest action.
