namespace EdiTrack.Api.Dtos;

public sealed class ErrorResponse
{
    public string Message { get; init; } = string.Empty;
    public string? CorrelationId { get; init; }
    public IReadOnlyDictionary<string, string[]>? Errors { get; init; }
}
