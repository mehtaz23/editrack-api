using EdiTrack.Api.Dtos;
using EdiTrack.Api.Infrastructure.Data;
using EdiTrack.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Formatting.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, cfg) => cfg
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .WriteTo.Console(new JsonFormatter()));

// Add services to the container.
builder.Services.AddOpenApi();

var connectionString = builder.Configuration["DATABASE_URL"]
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("No database connection string configured.");

builder.Services.AddDbContext<EdiTrackDbContext>(opts => opts.UseNpgsql(connectionString));

builder.Services.AddControllers();

var corsOrigins = builder.Configuration["ALLOWED_CORS_ORIGINS"] ?? "http://localhost:3000";
builder.Services.AddCors(opts => opts.AddDefaultPolicy(p =>
    p.WithOrigins(corsOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
     .AllowAnyHeader()
     .AllowAnyMethod()));

builder.Services.AddScoped<IIngestService, IngestService>();

builder.Services.AddHealthChecks();

builder.Services.Configure<ApiBehaviorOptions>(opts =>
{
    opts.InvalidModelStateResponseFactory = ctx =>
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var logger = ctx.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("EdiTrack.Api.Validation");
        logger.LogWarning("Ingest rejected {CorrelationId} {Path} {Outcome}",
            correlationId, ctx.HttpContext.Request.Path, "ValidationFailure");
        var errors = ctx.ModelState
            .Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(e => e.Key, e => e.Value!.Errors.Select(x => x.ErrorMessage).ToArray());
        return new BadRequestObjectResult(new ErrorResponse
        {
            Message = "One or more validation errors occurred.",
            CorrelationId = correlationId,
            Errors = errors
        });
    };
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseSerilogRequestLogging();
app.UseHttpsRedirection();
app.UseCors();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program { }
