using EdiTrack.Api.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace EdiTrack.Api.Tests.Helpers;

public class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer _pgContainer = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        _pgContainer = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("editrack_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        await _pgContainer.StartAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("DATABASE_URL", _pgContainer.GetConnectionString());
                b.ConfigureServices(services =>
                {
                    // Remove existing EdiTrackDbContext registration and replace with test one
                    var descriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<EdiTrackDbContext>));
                    if (descriptor != null)
                        services.Remove(descriptor);

                    var dbDescriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(EdiTrackDbContext));
                    if (dbDescriptor != null)
                        services.Remove(dbDescriptor);

                    services.AddDbContext<EdiTrackDbContext>(opts =>
                        opts.UseNpgsql(_pgContainer.GetConnectionString()));

                    var sp = services.BuildServiceProvider();
                    using var scope = sp.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<EdiTrackDbContext>();
                    db.Database.EnsureCreated();
                });
            });
    }

    public HttpClient CreateClient() => _factory.CreateClient();

    public EdiTrackDbContext CreateDbContext()
    {
        var scope = _factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<EdiTrackDbContext>();
    }

    public async Task DisposeAsync()
    {
        await _pgContainer.StopAsync();
        _factory.Dispose();
    }
}
