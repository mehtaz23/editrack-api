using System.Net;
using System.Net.Http.Json;
using EdiTrack.Api.Dtos;
using EdiTrack.Api.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EdiTrack.Api.Tests.Integration;

public class IngestEndpointTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    private const string ValidBody = """
        {
          "senderId": "ACME",
          "receiverId": "GLOBEX",
          "transactionType": "850",
          "payload": "ISA*00*          *00*          *ZZ*ACME           *ZZ*GLOBEX         *250101*0900*^*00501*000000001*0*P*>~"
        }
        """;

    public IngestEndpointTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PostIngest_ValidRequest_Returns200WithAcknowledgment()
    {
        var client = _fixture.CreateClient();
        var response = await client.PostAsync("/api/ingest",
            new StringContent(ValidBody, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var ack = await response.Content.ReadFromJsonAsync<IngestAcknowledgment>();
        Assert.NotNull(ack);
        Assert.NotEqual(Guid.Empty, ack.TransactionId);
        Assert.False(string.IsNullOrEmpty(ack.CorrelationId));
        Assert.Equal("ACME", ack.SenderId);
        Assert.Equal("GLOBEX", ack.ReceiverId);
        Assert.Equal("850", ack.TransactionType);
        Assert.Equal("Received", ack.Status);
        Assert.True(DateTimeOffset.UtcNow - ack.ReceivedAt < TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task PostIngest_ValidRequest_PersistsTransaction()
    {
        var client = _fixture.CreateClient();
        var response = await client.PostAsync("/api/ingest",
            new StringContent(ValidBody, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var db = _fixture.CreateDbContext();
        var count = await db.Transactions.CountAsync();
        Assert.True(count >= 1);

        var record = await db.Transactions.OrderByDescending(t => t.ReceivedAt).FirstAsync();
        Assert.Equal("ACME", record.SenderId);
        Assert.Contains("ISA*", record.Payload);
    }

    [Fact]
    public async Task PostIngest_CorrelationIdOmitted_GeneratedInResponse()
    {
        var client = _fixture.CreateClient();
        var body = """
            {
              "senderId": "ACME",
              "receiverId": "GLOBEX",
              "transactionType": "850",
              "payload": "ISA*00*          *00*          *ZZ*ACME           *ZZ*GLOBEX         *250101*0900*^*00501*000000001*0*P*>~"
            }
            """;
        var response = await client.PostAsync("/api/ingest",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var ack = await response.Content.ReadFromJsonAsync<IngestAcknowledgment>();
        Assert.NotNull(ack);
        Assert.False(string.IsNullOrEmpty(ack.CorrelationId));
    }

    [Fact]
    public async Task PostIngest_MissingRequiredField_Returns400()
    {
        var client = _fixture.CreateClient();
        var body = """{"receiverId":"GLOBEX","transactionType":"850","payload":"ISA*00*..."}""";
        var response = await client.PostAsync("/api/ingest",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var err = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(err);
        Assert.False(string.IsNullOrEmpty(err.Message));
        Assert.NotNull(err.Errors);
        Assert.True(err.Errors.ContainsKey("senderId") || err.Errors.ContainsKey("SenderId"));
    }

    [Fact]
    public async Task PostIngest_EmptyPayload_Returns400()
    {
        var client = _fixture.CreateClient();
        var body = """{"senderId":"ACME","receiverId":"GLOBEX","transactionType":"850","payload":""}""";
        var response = await client.PostAsync("/api/ingest",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var err = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(err);
    }

    [Fact]
    public async Task PostIngest_NonEdiPayload_Returns400()
    {
        var client = _fixture.CreateClient();
        var body = """{"senderId":"ACME","receiverId":"GLOBEX","transactionType":"850","payload":"not a valid EDI document"}""";
        var response = await client.PostAsync("/api/ingest",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var err = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(err);
        Assert.Equal("Payload does not resemble an EDI X12 document.", err.Message);
        Assert.Null(err.Errors);
    }

    [Fact]
    public async Task PostIngest_RejectedRequest_LeavesNoDbRecord()
    {
        // Use a fresh context to check count before
        using var db = _fixture.CreateDbContext();
        var countBefore = await db.Transactions.CountAsync();

        var client = _fixture.CreateClient();
        var body = """{"senderId":"ACME","receiverId":"GLOBEX","transactionType":"850","payload":"not a valid EDI document"}""";
        var response = await client.PostAsync("/api/ingest",
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var db2 = _fixture.CreateDbContext();
        var countAfter = await db2.Transactions.CountAsync();
        Assert.Equal(countBefore, countAfter);
    }
}
