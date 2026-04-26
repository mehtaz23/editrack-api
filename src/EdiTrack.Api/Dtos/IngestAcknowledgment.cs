namespace EdiTrack.Api.Dtos;

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
