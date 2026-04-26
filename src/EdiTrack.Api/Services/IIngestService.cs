using EdiTrack.Api.Dtos;

namespace EdiTrack.Api.Services;

public interface IIngestService
{
    Task<IngestResult> IngestAsync(IngestRequest request, CancellationToken ct = default);
}

public abstract record IngestResult
{
    public sealed record Success(IngestAcknowledgment Acknowledgment) : IngestResult;
    public sealed record ValidationFailure(string Message) : IngestResult;
    public sealed record PersistenceFailure(string Message) : IngestResult;
}
