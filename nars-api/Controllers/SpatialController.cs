using System.Data;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;
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
    AppDbContext db,
    IScatteredAreaService scatteredService,
    IEntranceQueryService entranceQuery) : NarsControllerBase
{
    // ── POST /api/road-side ───────────────────────────────────────────────────

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

        var road = await db.Roads.FirstOrDefaultAsync(f =>
            f.Id == body.RoadId && f.UserId == CurrentUserId, cancellationToken);

        if (road is null)
        {
            return Problem(detail: "Road not found.", statusCode: 404);
        }

        var roadData = JsonHelper.DeserializeSafe(road.Data);
        if (!roadData.TryGetProperty("coordinates", out var coordsEl))
        {
            return Problem(detail: "Road data is missing coordinates.", statusCode: 400);
        }

        var roadCoords = coordsEl.EnumerateArray()
            .Select(c => (Lat: c.GetProperty("lat").GetDouble(),
                          Lng: c.GetProperty("lng").GetDouble()))
            .ToList();

        if (roadCoords.Count < 2)
        {
            return Problem(detail: "Road has insufficient coordinates.", statusCode: 400);
        }

        if (double.IsNaN(body.Lat) || double.IsInfinity(body.Lat) ||
            double.IsNaN(body.Lng) || double.IsInfinity(body.Lng))
        {
            return Problem(detail: "Invalid coordinate values.", statusCode: 400);
        }

        // Find the nearest segment midpoint to the marker.
        // Apply cosine correction so the Δlng component is in the same
        // unit scale as Δlat (important at Algeria's latitudes ~28–37°N).
        double markerLat = body.Lat, markerLng = body.Lng;
        var cosLat = Math.Cos(markerLat * Math.PI / 180.0);
        var minDist = double.MaxValue;
        var nearestIdx = 0;

        for (var i = 0; i < roadCoords.Count - 1; i++)
        {
            var mid = ((roadCoords[i].Lat + roadCoords[i + 1].Lat) / 2,
                       (roadCoords[i].Lng + roadCoords[i + 1].Lng) / 2);
            var dLat = markerLat - mid.Item1;
            var dLng = (markerLng - mid.Item2) * cosLat;
            var d = Math.Sqrt(dLat * dLat + dLng * dLng);
            if (d < minDist) { minDist = d; nearestIdx = i; }
        }

        var (Lat, Lng) = roadCoords[nearestIdx];
        var p2 = roadCoords[nearestIdx + 1];

        // Cross product: positive -> left, negative -> right
        var cross = (p2.Lng - Lng) * (markerLat - Lat)
                  - (p2.Lat - Lat) * (markerLng - Lng);

        var side = cross >= 0 ? "left" : "right";

        // Query used entrance numbers via dedicated service (raw ADO.NET
        // is required for JSONB field extraction that EF Core doesn't handle).
        var usedNumbers = await entranceQuery.GetUsedEntranceNumbersAsync(
            RequiredCurrentUserId, body.RoadId, cancellationToken);

        // Next available odd (left) or even (right) number
        var suggested = side == "left" ? 1 : 2;
        while (usedNumbers.Contains(suggested))
        {
            suggested += 2;
        }

        return Ok(new RoadSideResponse(side, suggested));
    }

    // ── POST /api/areas/refresh-scattered ────────────────────────────────────

    [HttpPost("areas/refresh-scattered")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> RefreshScattered(CancellationToken cancellationToken = default)
    {
        var communeId = CurrentCommuneId;
        if (communeId is null)
        {
            return Problem(detail: "This endpoint requires a commune-level account.", statusCode: 400);
        }

        await scatteredService.RefreshAsync(RequiredCurrentUserId, communeId.Value, cancellationToken);
        return Ok(new ScatteredRefreshResponse(true, null, "Scattered area recomputed."));
    }
}
