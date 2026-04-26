# Data Model: EDI Transaction Ingest

*Phase 1 output for `/speckit.plan`.*

---

## Entities

### EdiTransaction

The durable record of an accepted EDI ingest submission.

| Property | C# Type | DB Column | DB Type | Constraints | Notes |
|---|---|---|---|---|---|
| `Id` | `Guid` | `id` | `uuid` | PK, NOT NULL | Generated via `Guid.CreateVersion7()` in service layer; UUID v7 time-ordered |
| `SenderId` | `string` | `sender_id` | `varchar(50)` | NOT NULL | ISA06 / trading partner sender ID |
| `ReceiverId` | `string` | `receiver_id` | `varchar(50)` | NOT NULL | ISA08 / trading partner receiver ID |
| `TransactionType` | `string` | `transaction_type` | `varchar(50)` | NOT NULL | Caller-provided type hint (e.g., "850", "856"); not validated against X12 catalogue |
| `CorrelationId` | `string` | `correlation_id` | `varchar(100)` | NOT NULL | Caller-provided or server-generated; always present |
| `Payload` | `string` | `payload` | `text` | NOT NULL | Verbatim raw EDI X12 string; no length cap |
| `Status` | `TransactionStatus` | `status` | `varchar(50)` | NOT NULL | Initial value: `Received`; stored as human-readable string via `.HasConversion<string>()` |
| `ReceivedAt` | `DateTimeOffset` | `received_at` | `timestamptz` | NOT NULL | UTC timestamp set at service layer; Npgsql maps `DateTimeOffset` → `timestamptz` natively |

**EF Core Table Name**: `EdiTransactions` (default EF Core plural convention)

**Index**: No additional index defined in this phase. UUID v7 PK ordering reduces insert
fragmentation naturally. Query indexes (e.g., on `sender_id`, `correlation_id`) are
deferred to a future query/search feature.

---

## Enum: TransactionStatus

```csharp
namespace EdiTrack.Api.Domain.Enums;

public enum TransactionStatus
{
    Received   // Initial status for all accepted ingest submissions
}
```

**DB Mapping**: EF Core `.HasConversion<string>()` stores `"Received"` as `varchar(50)`.
Integer ordinal storage is explicitly rejected — human-readable string values make the
database self-documenting and survivable without the C# enum definition.

**Extension path**: Additional statuses (e.g., `Processing`, `Delivered`, `Failed`) will
be added to this enum and a new EF Core migration column update applied. The `varchar(50)`
width gives plenty of room.

---

## DTOs (API Boundary)

### IngestRequest (Request Body — POST /api/ingest)

```
{
  "senderId":        string  (required, non-empty)
  "receiverId":      string  (required, non-empty)
  "transactionType": string  (required, non-empty)
  "correlationId":   string  (optional — server generates if absent)
  "payload":         string  (required, non-empty, must resemble EDI X12)
}
```

### IngestAcknowledgment (200 OK Response Body)

```
{
  "transactionId":   UUID string  (stable, UUID v7)
  "correlationId":   string       (caller-provided or server-generated)
  "senderId":        string
  "receiverId":      string
  "transactionType": string
  "receivedAt":      ISO 8601 datetime (UTC)
  "status":          string       ("Received")
}
```

### ErrorResponse (400 / 503 Response Body)

```
{
  "message": string                             (human-readable description)
  "errors":  { "fieldName": ["reason", ...] }  (optional — present for 400 validation failures)
}
```

---

## State Transitions

```
                    ┌──────────────────────────────────────────┐
                    │             EdiTransaction                │
                    │                                          │
  [Accepted] ──────►│  Status = Received                       │
                    │                                          │
                    └──────────────────────────────────────────┘
                                     │
                              (future features)
                                     │
                         ┌───────────┴───────────┐
                         ▼                       ▼
                      Processing             (other statuses)
```

The status lifecycle beyond `Received` is out of scope for this feature.

---

## EF Core Fluent API Configuration Summary

```csharp
modelBuilder.Entity<EdiTransaction>(entity =>
{
    entity.HasKey(t => t.Id);

    entity.Property(t => t.Status)
          .HasConversion<string>()
          .HasMaxLength(50)
          .IsRequired();

    entity.Property(t => t.SenderId)
          .IsRequired()
          .HasMaxLength(50);

    entity.Property(t => t.ReceiverId)
          .IsRequired()
          .HasMaxLength(50);

    entity.Property(t => t.TransactionType)
          .IsRequired()
          .HasMaxLength(50);

    entity.Property(t => t.CorrelationId)
          .IsRequired()
          .HasMaxLength(100);

    entity.Property(t => t.Payload)
          .IsRequired();   // text — no max length

    entity.Property(t => t.ReceivedAt)
          .IsRequired();
});
```

---

## Migration Plan

| Migration | Description | When Applied |
|---|---|---|
| `InitialCreate` | Creates `EdiTransactions` table with all columns per the model above | Phase 3 of implementation — run `dotnet ef database update` against compose Postgres |

Future migrations will be additive only (new columns nullable by default, or with DB-level
defaults, to avoid locking existing rows during `ALTER TABLE`).
