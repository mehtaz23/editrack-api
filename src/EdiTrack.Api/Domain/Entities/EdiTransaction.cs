using EdiTrack.Api.Domain.Enums;

namespace EdiTrack.Api.Domain.Entities;

public sealed class EdiTransaction
{
    public Guid Id { get; set; }
    public string SenderId { get; set; } = string.Empty;
    public string ReceiverId { get; set; } = string.Empty;
    public string TransactionType { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public TransactionStatus Status { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}
