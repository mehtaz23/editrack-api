using System.ComponentModel.DataAnnotations;

namespace EdiTrack.Api.Dtos;

public sealed class IngestRequest
{
    [Required, MinLength(1)]
    public string SenderId { get; init; } = string.Empty;

    [Required, MinLength(1)]
    public string ReceiverId { get; init; } = string.Empty;

    [Required, MinLength(1)]
    public string TransactionType { get; init; } = string.Empty;

    public string? CorrelationId { get; init; }

    [Required, MinLength(1)]
    public string Payload { get; init; } = string.Empty;
}
