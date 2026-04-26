using EdiTrack.Api.Domain.Entities;
using EdiTrack.Api.Domain.Enums;
using EdiTrack.Api.Dtos;
using EdiTrack.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EdiTrack.Api.Services;

public sealed class IngestService : IIngestService
{
    private readonly EdiTrackDbContext _context;
    private readonly ILogger<IngestService> _logger;

    public IngestService(EdiTrackDbContext context, ILogger<IngestService> logger)
    {
        _context = context;
        _logger = logger;
    }

    private static bool IsEdiX12Shaped(string payload) =>
        payload.AsSpan().TrimStart().StartsWith("ISA*", StringComparison.Ordinal);

    public async Task<IngestResult> IngestAsync(IngestRequest request, CancellationToken ct = default)
    {
        if (!IsEdiX12Shaped(request.Payload))
            return new IngestResult.ValidationFailure("Payload does not resemble an EDI X12 document.");

        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : request.CorrelationId;

        var entity = new EdiTransaction
        {
            Id = Guid.CreateVersion7(),
            SenderId = request.SenderId,
            ReceiverId = request.ReceiverId,
            TransactionType = request.TransactionType,
            CorrelationId = correlationId,
            Payload = request.Payload,
            Status = TransactionStatus.Received,
            ReceivedAt = DateTimeOffset.UtcNow
        };

        _context.Transactions.Add(entity);

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Persistence failure {CorrelationId} {SenderId} {ReceiverId}",
                correlationId, request.SenderId, request.ReceiverId);
            return new IngestResult.PersistenceFailure("A transient database error occurred. Please retry.");
        }

        _logger.LogInformation(
            "Ingest accepted {CorrelationId} {SenderId} {ReceiverId} {TransactionType} {TransactionId} {Outcome}",
            correlationId, request.SenderId, request.ReceiverId, request.TransactionType, entity.Id, "Accepted");

        return new IngestResult.Success(new IngestAcknowledgment
        {
            TransactionId = entity.Id,
            CorrelationId = correlationId,
            SenderId = entity.SenderId,
            ReceiverId = entity.ReceiverId,
            TransactionType = entity.TransactionType,
            ReceivedAt = entity.ReceivedAt,
            Status = entity.Status.ToString()
        });
    }
}
