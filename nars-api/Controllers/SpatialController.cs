using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Services;

namespace NarsApi.Controllers;

/// <summary>
/// Spatial query endpoints: road-side determination and scattered-area recomputation.
/// These are operational/GIS queries distinct from the shape-validation rules in
/// <see cref="ValidationController"/>.
/// </summary>
[ApiController]
[Route("/api")]
[Tags("Spatial")]
public class SpatialController(
    IRoadQueryService roadQuery,
    IScatteredAreaService scatteredService,
    IEntranceQueryService entranceQuery,
    IWebHostEnvironment webHost,
    ILogger<SpatialController> logger) : NarsControllerBase(webHost)
{
    // ── POST /api/road-side ───────────────────────────────────────────────────

    /// <summary>Determines which side of a road a marker is on and suggests the next entrance number.</summary>
    [HttpPost("road-side")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRoadSide([FromBody] RoadSideRequest body, CancellationToken cancellationToken = default)
    {
        if (body is null)
        {
            return Problem(detail: "Request body is required.", statusCode: 400);
        }

        if (double.IsNaN(body.Lat) || double.IsInfinity(body.Lat) ||
            double.IsNaN(body.Lng) || double.IsInfinity(body.Lng))
        {
            return Problem(detail: "Invalid coordinate values.", statusCode: 400);
        }

        var road = await roadQuery.GetUserRoadByIdAsync(body.RoadId, RequiredCurrentUserId, cancellationToken);

        if (road is null)
        {
            return Problem(detail: "Road not found.", statusCode: 404);
        }

        var roadData = JsonHelper.DeserializeSafe(road.Data);
        var coordsNode = roadData?["coordinates"];

        if (coordsNode is null)
        {
            return Problem(detail: "Road has no coordinates data.", statusCode: 400);
        }

        List<(double Lat, double Lng)> roadCoords;
        try
        {
            roadCoords = GeometryHelper.ParseRoadCoordinates(coordsNode);
        }
        catch (ArgumentException ex)
        {
            logger.LogDebug(ex, "Rejected invalid road coordinates: {Reason}", ex.Message);
            return Problem(detail: "Road coordinates are invalid.", statusCode: 400);
        }

        var nearestIdx = GeometryHelper.FindNearestSegmentIndex(body.Lat, body.Lng, roadCoords);
        var (Lat, Lng) = roadCoords[nearestIdx];
        var p2 = roadCoords[nearestIdx + 1];
        var side = GeometryHelper.DetermineSide(body.Lat, body.Lng, Lat, Lng, p2.Lat, p2.Lng);

        // Query used entrance numbers via dedicated service (raw ADO.NET
        // is required for JSONB field extraction that EF Core doesn't handle).
        var usedNumbers = await entranceQuery.GetUsedEntranceNumbersAsync(
            RequiredCurrentUserId, body.RoadId, side, cancellationToken);

        var suggested = GeometryHelper.SuggestEntranceNumber(side, usedNumbers);
        if (suggested < 0)
        {
            return Problem(
                detail: "The entrance-number series for this side is exhausted; no collision-free number is available.",
                statusCode: 409);
        }

        return Ok(new RoadSideResponse(side, suggested));
    }

    // ── GET /api/areas/scattered-status ─────────────────────────────────────

    /// <summary>Returns the last error encountered during scattered area computation.</summary>
    [HttpGet("areas/scattered-status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetScatteredStatus()
    {
        var communeId = CurrentCommuneId;
        var error = communeId is null
            ? null
            : scatteredService.GetLastError(RequiredCurrentUserId, communeId.Value);
        return Ok(new ScatteredStatusResponse(
            LastErrorTime: error?.Timestamp.ToString(JsonHelper.IsoDateFormat),
            LastErrorMessage: error.HasValue ? "An error occurred during computation." : null,
            HasError: error.HasValue
        ));
    }

    // ── POST /api/areas/refresh-scattered ────────────────────────────────────

    /// <summary>
    /// Triggers a recomputation of scattered areas for the user's commune.
    /// Deliberately synchronous: the frontend consumes the computed GeoJSON in the
    /// response body. It is rate-limited because the PostGIS recompute can block
    /// the request thread for seconds.
    /// </summary>
    [HttpPost("areas/refresh-scattered")]
    [EnableRateLimiting(RateLimitPolicies.ScatteredRefresh)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RefreshScattered(CancellationToken cancellationToken = default)
    {
        var communeId = CurrentCommuneId;
        if (communeId is null)
        {
            return Problem(detail: "This endpoint requires a commune-level account.", statusCode: 400);
        }

        var geojson = await scatteredService.RefreshAsync(RequiredCurrentUserId, communeId.Value, cancellationToken);

        // A null result is ambiguous between "no scattered geometry existed" and
        // a real failure; failures always populate the error cache, so use it to
        // tell the two apart.
        if (geojson is null && scatteredService.GetLastError(RequiredCurrentUserId, communeId.Value) is not null)
        {
            return Problem(
                detail: "Scattered area recomputation failed.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        return Ok(new ScatteredRefreshResponse(true, geojson, "Scattered area recomputed."));
    }
}
