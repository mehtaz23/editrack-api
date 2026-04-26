using EdiTrack.Api.Dtos;
using EdiTrack.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace EdiTrack.Api.Controllers;

[ApiController]
[Route("api")]
public sealed class IngestController : ControllerBase
{
    private readonly IIngestService _service;
    private readonly ILogger<IngestController> _logger;

    public IngestController(IIngestService service, ILogger<IngestController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpPost("ingest")]
    [ProducesResponseType<IngestAcknowledgment>(StatusCodes.Status200OK)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> IngestAsync([FromBody] IngestRequest request, CancellationToken ct)
    {
        var result = await _service.IngestAsync(request, ct);

        if (result is IngestResult.Success s)
            return Ok(s.Acknowledgment);

        if (result is IngestResult.ValidationFailure v)
        {
            _logger.LogWarning("Ingest rejected {Outcome} {Message}", "ValidationFailure", v.Message);
            return BadRequest(new ErrorResponse { Message = v.Message });
        }

        if (result is IngestResult.PersistenceFailure p)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ErrorResponse { Message = p.Message });

        return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse { Message = "Unexpected error." });
    }
}
