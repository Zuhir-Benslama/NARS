using Microsoft.AspNetCore.Mvc;
using NarsApi.DTOs;
using NarsApi.Services;

namespace NarsApi.Controllers;

/// <summary>
/// Runs the districts generation phase on its own, with no imagery and no road
/// run, so it can be triggered from the districts phase.
/// </summary>
/// <remarks>
/// This phase is PostGIS-only work over data the user has already mapped — it
/// partitions the commune's urban areas along the primary road network
/// (boulevards/avenues) — so it needs neither the satellite tiles nor the
/// segmentation pass that <see cref="GenerationJobController"/> drives. It was
/// previously reachable only as the tail of a generation job, which meant
/// re-cutting districts forced a full re-segmentation of the commune.
/// </remarks>
[ApiController]
[Route("api/generation/districts")]
public sealed class GenerationDistrictsController(
    IDistrictGenerationService districtGenerationService,
    ILogger<GenerationDistrictsController> logger,
    IWebHostEnvironment webHost) : NarsControllerBase(webHost)
{
    /// <summary>
    /// Partitions a commune's urban areas along its primary roads and writes the
    /// pieces as pending district drafts for review. The caller must have access
    /// to the commune.
    /// </summary>
    /// <response code="200">Drafts created.</response>
    /// <response code="400">The commune has no urban area, no primary road to cut along, or the pass produced nothing.</response>
    /// <response code="403">The caller has no access to the commune.</response>
    /// <response code="501">The host database is not PostGIS-backed.</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status501NotImplemented)]
    public async Task<ActionResult<GenerateDistrictsResponse>> Generate(
        [FromBody] GenerateDistrictsRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        if (request.CommuneId is null)
        {
            return Problem(detail: "communeId is required.", statusCode: 400);
        }

        try
        {
            var summary = await districtGenerationService.GenerateAsync(
                CurrentUserRole, CurrentCommuneId, CurrentDairaId, CurrentWilayaId,
                request.CommuneId.Value, cancellationToken);

            var response = new GenerateDistrictsResponse(
                summary.Drafts
                    .Select(d => new GeneratedDistrictDto(d.DraftId, d.AreaM2, d.Lat, d.Lng))
                    .ToList(),
                summary.AbsorbedSlivers,
                summary.PrimaryRoadCount,
                summary.UrbanAreaCount);

            logger.LogInformation(
                "Districts generation for commune {CommuneId}: {Count} draft(s), {Absorbed} sliver(s) absorbed.",
                request.CommuneId.Value, summary.Drafts.Count, summary.AbsorbedSlivers);

            return Ok(response);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            // Missing urban area / no primary road / empty partition: a problem
            // with the commune's current data that the user can act on, so the
            // message is surfaced verbatim.
            logger.LogInformation(ex, "Districts generation rejected for commune {CommuneId}: {Reason}",
                request.CommuneId.Value, ex.Message);
            return Problem(detail: ex.Message, statusCode: 400);
        }
        catch (NotSupportedException ex)
        {
            logger.LogError(ex, "Districts generation is unsupported on this host.");
            return Problem(detail: ex.Message, statusCode: 501);
        }
    }
}
