using EdiTrack.Api.Dtos;
using EdiTrack.Api.Infrastructure.Data;
using EdiTrack.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace EdiTrack.Api.Tests.Unit.Services;

public class IngestServiceTests
{
    private static EdiTrackDbContext BuildInMemoryContext() =>
        new EdiTrackDbContext(new DbContextOptionsBuilder<EdiTrackDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static IngestService BuildService(EdiTrackDbContext ctx) =>
        new IngestService(ctx, NullLogger<IngestService>.Instance);

    private static IngestRequest ValidRequest(string? correlationId = null) => new IngestRequest
    {
        SenderId = "ACME",
        ReceiverId = "GLOBEX",
        TransactionType = "850",
        CorrelationId = correlationId,
        Payload = "ISA*00*          *00*          *ZZ*ACME           *ZZ*GLOBEX         *250101*0900*^*00501*000000001*0*P*>~"
    };

    [Fact]
    public async Task IngestAsync_ValidRequest_ReturnsSuccess()
    {
        using var ctx = BuildInMemoryContext();
        var service = BuildService(ctx);

        var result = await service.IngestAsync(ValidRequest());

        var success = Assert.IsType<IngestResult.Success>(result);
        Assert.NotEqual(Guid.Empty, success.Acknowledgment.TransactionId);
        Assert.Equal("Received", success.Acknowledgment.Status);
        Assert.False(string.IsNullOrWhiteSpace(success.Acknowledgment.CorrelationId));
        Assert.True(DateTimeOffset.UtcNow - success.Acknowledgment.ReceivedAt < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task IngestAsync_CorrelationIdOmitted_GeneratesOne()
    {
        using var ctx = BuildInMemoryContext();
        var service = BuildService(ctx);

        var result = await service.IngestAsync(ValidRequest(correlationId: null));

        var success = Assert.IsType<IngestResult.Success>(result);
        Assert.False(string.IsNullOrEmpty(success.Acknowledgment.CorrelationId));
    }

    [Fact]
    public async Task IngestAsync_EmptyPayload_ReturnsValidationFailure()
    {
        using var ctx = BuildInMemoryContext();
        var service = BuildService(ctx);
        var request = new IngestRequest
        {
            SenderId = "ACME",
            ReceiverId = "GLOBEX",
            TransactionType = "850",
            Payload = ""
        };

        var result = await service.IngestAsync(request);

        var failure = Assert.IsType<IngestResult.ValidationFailure>(result);
        Assert.False(string.IsNullOrEmpty(failure.Message));
        Assert.Equal(0, ctx.Transactions.Count());
    }

    [Fact]
    public async Task IngestAsync_NonEdiPayload_ReturnsValidationFailure()
    {
        using var ctx = BuildInMemoryContext();
        var service = BuildService(ctx);
        var request = new IngestRequest
        {
            SenderId = "ACME",
            ReceiverId = "GLOBEX",
            TransactionType = "850",
            Payload = "not a valid EDI document"
        };

        var result = await service.IngestAsync(request);

        Assert.IsType<IngestResult.ValidationFailure>(result);
        Assert.Equal(0, ctx.Transactions.Count());
    }

    [Fact]
    public async Task IngestAsync_PayloadWithLeadingWhitespace_AcceptsWhenEdiShaped()
    {
        using var ctx = BuildInMemoryContext();
        var service = BuildService(ctx);
        var request = new IngestRequest
        {
            SenderId = "ACME",
            ReceiverId = "GLOBEX",
            TransactionType = "850",
            Payload = "  ISA*00*          *00*          *ZZ*ACME           *ZZ*GLOBEX         *250101*0900*^*00501*000000001*0*P*>~"
        };

        var result = await service.IngestAsync(request);

        Assert.IsType<IngestResult.Success>(result);
    }

    [Fact]
    public async Task IngestAsync_DbUpdateException_ReturnsPersistenceFailure()
    {
        using var ctx = new FaultyDbContext(new DbContextOptionsBuilder<EdiTrackDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        var service = new IngestService(ctx, NullLogger<IngestService>.Instance);

        var result = await service.IngestAsync(ValidRequest());

        var failure = Assert.IsType<IngestResult.PersistenceFailure>(result);
        Assert.Contains("transient", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IngestAsync_ValidationFailure_NoDbSave()
    {
        using var ctx = new SaveCallTrackingDbContext(new DbContextOptionsBuilder<EdiTrackDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        var service = new IngestService(ctx, NullLogger<IngestService>.Instance);
        var request = new IngestRequest
        {
            SenderId = "ACME",
            ReceiverId = "GLOBEX",
            TransactionType = "850",
            Payload = "not EDI"
        };

        var result = await service.IngestAsync(request);

        Assert.IsType<IngestResult.ValidationFailure>(result);
        Assert.Equal(0, ctx.SaveCallCount);
    }

    [Fact]
    public async Task IngestAsync_Success_LogsExpectedFields()
    {
        using var ctx = BuildInMemoryContext();
        var mockLogger = new Mock<ILogger<IngestService>>();
        var service = new IngestService(ctx, mockLogger.Object);
        var request = ValidRequest();

        await service.IngestAsync(request);

        mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Accepted")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);

        // Verify payload is NOT logged
        mockLogger.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(request.Payload)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    private sealed class FaultyDbContext : EdiTrackDbContext
    {
        public FaultyDbContext(DbContextOptions<EdiTrackDbContext> options) : base(options) { }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
            => throw new DbUpdateException("simulated", new Exception());
    }

    private sealed class SaveCallTrackingDbContext : EdiTrackDbContext
    {
        public int SaveCallCount { get; private set; }

        public SaveCallTrackingDbContext(DbContextOptions<EdiTrackDbContext> options) : base(options) { }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCallCount++;
            return await base.SaveChangesAsync(cancellationToken);
        }
    }
}
