# API Contract: EDI Transaction Ingest

*Phase 1 output for `/speckit.plan` — authoritative request/response shapes for
`POST /api/ingest`.*

---

## Endpoint

| Property | Value |
|---|---|
| Method | `POST` |
| Path | `/api/ingest` |
| Content-Type | `application/json` |
| Auth | None (deferred to future security feature) |

---

## Request Body

```json
{
  "senderId":        "SENDER001",
  "receiverId":      "RECEIVER002",
  "transactionType": "850",
  "correlationId":   "req-abc-123",
  "payload":         "ISA*00*          *00*          *ZZ*SENDER001      *ZZ*RECEIVER002    *230101*1200*^*00501*000000001*0*P*>~GS*PO*SENDER001*RECEIVER002*20230101*1200*1*X*005010~ST*850*0001~BEG*00*NE*PO-001**20230101~SE*3*0001~GE*1*1~IEA*1*000000001~"
}
```

### Fields

| Field | Type | Required | Description |
|---|---|---|---|
| `senderId` | string | **Yes** | Trading partner sender identifier. Must be non-empty. |
| `receiverId` | string | **Yes** | Trading partner receiver identifier. Must be non-empty. |
| `transactionType` | string | **Yes** | EDI X12 transaction set identifier hint (e.g., `"850"`, `"856"`, `"810"`). Any non-empty value accepted at this stage. |
| `correlationId` | string | No | Caller-provided trace identifier. If omitted, the server generates one and includes it in the response. |
| `payload` | string | **Yes** | Raw EDI X12 interchange string. Must be non-empty and must begin (after optional leading whitespace) with `ISA*`. |

---

## Responses

### 200 OK — Accepted

Returned when the submission passes validation and is persisted successfully.

```json
{
  "transactionId":   "019241f9-a1b2-7c3d-8e4f-5a6b7c8d9e0f",
  "correlationId":   "req-abc-123",
  "senderId":        "SENDER001",
  "receiverId":      "RECEIVER002",
  "transactionType": "850",
  "receivedAt":      "2025-07-23T14:32:01.123456Z",
  "status":          "Received"
}
```

| Field | Type | Description |
|---|---|---|
| `transactionId` | UUID string | Stable UUID v7 identifier for this transaction. Time-ordered, safe for use as a durable reference. |
| `correlationId` | string | Echoes the caller-provided value or the server-generated value if the caller did not provide one. |
| `senderId` | string | Echoes the request value. |
| `receiverId` | string | Echoes the request value. |
| `transactionType` | string | Echoes the request value. |
| `receivedAt` | ISO 8601 UTC datetime | Server-assigned receipt timestamp. |
| `status` | string | Always `"Received"` on initial ingest. |

---

### 400 Bad Request — Validation Failure

Returned when required fields are missing/empty, or the payload does not resemble an EDI
X12 document. **No transaction record is created.**

```json
{
  "message": "One or more validation errors occurred.",
  "errors": {
    "senderId": ["The senderId field is required."],
    "payload":  ["The payload field is required."]
  }
}
```

**Field-level errors** (`errors` map): present when data-annotation validation fails
(missing/empty required fields). Each key is the camelCase field name; each value is an
array of human-readable error messages.

**No field-level errors** (message only): returned when the payload fails the EDI X12
shape check:

```json
{
  "message": "Payload does not resemble an EDI X12 document."
}
```

---

### 503 Service Unavailable — Transient Persistence Failure

Returned when validation passes but the PostgreSQL write fails (connection timeout,
transient network error, constraint violation). **No transaction record is created.**

```json
{
  "message": "A transient database error occurred. Please retry."
}
```

> **Retry guidance**: `503` is the explicit signal that the failure is transient and safe
> to retry. The API does **not** perform automatic retries. The caller is responsible for
> implementing a retry strategy with appropriate back-off.

---

## Error Envelope Shape

All non-2xx responses use the same `ErrorResponse` shape:

```typescript
{
  message: string;                         // always present
  errors?: Record<string, string[]>;       // present only for 400 field-level validation
}
```

---

## OpenAPI Annotations (Controller)

```csharp
[ProducesResponseType<IngestAcknowledgment>(StatusCodes.Status200OK)]
[ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
```

The OpenAPI spec at `/openapi/v1.json` reflects all three response variants.
The Scalar UI at `/scalar` (Development only) provides an interactive request builder.

---

## Example Requests

### Minimal valid EDI submission (auto-generated correlationId)

```http
POST /api/ingest HTTP/1.1
Content-Type: application/json

{
  "senderId": "ACME",
  "receiverId": "GLOBEX",
  "transactionType": "850",
  "payload": "ISA*00*          *00*          *ZZ*ACME           *ZZ*GLOBEX         *250101*0900*^*00501*000000001*0*P*>~"
}
```

### Submission with explicit correlationId

```http
POST /api/ingest HTTP/1.1
Content-Type: application/json

{
  "senderId": "ACME",
  "receiverId": "GLOBEX",
  "transactionType": "856",
  "correlationId": "shipment-batch-2025-07-23-001",
  "payload": "ISA*00*          *00*          *ZZ*ACME           *ZZ*GLOBEX         *250723*1400*^*00501*000000002*0*P*>~"
}
```

### Invalid — missing senderId

```http
POST /api/ingest HTTP/1.1
Content-Type: application/json

{
  "receiverId": "GLOBEX",
  "transactionType": "850",
  "payload": "ISA*..."
}
```

Response: `400 Bad Request` with `errors.senderId` populated.

### Invalid — non-EDI payload

```http
POST /api/ingest HTTP/1.1
Content-Type: application/json

{
  "senderId": "ACME",
  "receiverId": "GLOBEX",
  "transactionType": "850",
  "payload": "not a valid EDI document"
}
```

Response: `400 Bad Request` with `message: "Payload does not resemble an EDI X12 document."`
