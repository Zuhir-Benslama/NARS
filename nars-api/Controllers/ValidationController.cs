using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;

namespace NarsApi.Controllers;

[ApiController]
[Route("/api")]
[Tags("Validation")]
public class ValidationController(
    AppDbContext db,
    IConfiguration config,
    IValidationService validationService) : NarsControllerBase
{
    private double DistrictBoundaryToleranceMeters =>
        double.TryParse(config["Validation:DistrictBoundaryToleranceMeters"], out var v) ? v : 10.0;

    private double MaxRoadTurnAngleDegrees =>
        double.TryParse(config["Validation:RoadTurnAngleDegrees"], out var v) ? v : 90.0;

    private double RoadConnectivityDistanceMeters =>
        double.TryParse(config["Validation:RoadConnectivityMeters"], out var v) ? v : 20.0;

    private int MaxCoordinateCount =>
        int.TryParse(config["Validation:MaxCoordinateCount"], out var v) ? v : 10_000;

    [HttpGet("validate/area/main-urban-exists")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> MainUrbanExists(CancellationToken cancellationToken = default)
    {
        var exists = await db.Areas.AnyAsync(f =>
            f.UserId == CurrentUserId &&
            f.Layer == FeatureTypes.AreaLayers.CentralUrban, cancellationToken);

        return Ok(new { exists });
    }

    [HttpPost("validate/road")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ValidateRoad([FromBody] ValidateRoadRequest body, CancellationToken cancellationToken = default)
    {
        if (body is null) return BadRequest(new ValidateRoadResponse(false, "Request body is required."));
        if (body.Coordinates.Count < 2)
            return BadRequest(new ValidateRoadResponse(false, "A road must have at least 2 points."));

        if (!CheckCoordinateBounds(body.Coordinates, out var boundsError))
            return BadRequest(new ValidateRoadResponse(false, boundsError!));

        for (int i = 0; i < body.Coordinates.Count - 2; i++)
        {
            var A = body.Coordinates[i];
            var B = body.Coordinates[i + 1];
            var C = body.Coordinates[i + 2];

            double v1x = B.Lng - A.Lng, v1y = B.Lat - A.Lat;
            double v2x = C.Lng - B.Lng, v2y = C.Lat - B.Lat;

            double len1 = Math.Sqrt(v1x * v1x + v1y * v1y);
            double len2 = Math.Sqrt(v2x * v2x + v2y * v2y);

            if (len1 < 1e-10 || len2 < 1e-10) continue;

            double dot = (v1x * v2x + v1y * v2y) / (len1 * len2);
            double angle = Math.Acos(Math.Clamp(dot, -1.0, 1.0)) * (180.0 / Math.PI);

            if (angle > MaxRoadTurnAngleDegrees)
                return Ok(new ValidateRoadResponse(false,
                    $"Road turn at point {i + 2} is {angle:F1}°, which exceeds the {MaxRoadTurnAngleDegrees}° maximum."));
        }

        var existingCount = await db.Roads.CountAsync(f => f.UserId == CurrentUserId, cancellationToken);

        if (existingCount == 0)
            return Ok(new ValidateRoadResponse(true, null));

        var wkt = BuildLineStringWkt(body.Coordinates);
        bool connected = await validationService.CheckRoadConnectivityAsync(
            RequiredCurrentUserId, wkt, RoadConnectivityDistanceMeters, cancellationToken);

        if (!connected)
            return Ok(new ValidateRoadResponse(false,
                "This road must connect to at least one existing road (within 20 m of an endpoint)."));

        return Ok(new ValidateRoadResponse(true, null));
    }

    [HttpPost("validate/district")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ValidateDistrict([FromBody] ValidateDistrictRequest body, CancellationToken cancellationToken = default)
    {
        if (body is null) return BadRequest(new ValidateDistrictResponse(false, "Request body is required."));
        if (body.Coordinates.Count < 3)
            return BadRequest(new ValidateDistrictResponse(false, "A district must have at least 3 points."));
        if (!CheckCoordinateBounds(body.Coordinates, out var inputError))
            return BadRequest(new ValidateDistrictResponse(false, inputError!));

        var existingCount = await db.Districts.CountAsync(f => f.UserId == CurrentUserId, cancellationToken);

        if (existingCount == 0)
            return Ok(new ValidateDistrictResponse(true, null));

        var wkt = BuildPolygonWkt(body.Coordinates);

        if (await validationService.CheckDistrictOverlapAsync(RequiredCurrentUserId, wkt, cancellationToken))
            return Ok(new ValidateDistrictResponse(false,
                "This district overlaps an existing district. Districts must share edges but not overlap."));

        var skipAdjacency = body.DistrictTypeKey == FeatureTypes.DistrictLayers.TradActivitiesZone ||
                            body.DistrictTypeKey == FeatureTypes.DistrictLayers.IndustryZone;

        if (!skipAdjacency)
        {
            var siblings = await validationService.CountSiblingsInSameAreaAsync(RequiredCurrentUserId, wkt, cancellationToken);
            if (siblings > 0 && !await validationService.CheckDistrictAdjacencyAsync(RequiredCurrentUserId, wkt, cancellationToken))
                return Ok(new ValidateDistrictResponse(false,
                    "This district does not connect to any existing district in this urban area. Districts must share a boundary (no gaps)."));
        }

        return Ok(new ValidateDistrictResponse(true, null));
    }

    [HttpGet("validate/districts/coverage")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> DistrictsCoverage(CancellationToken cancellationToken = default)
    {
        var urbanCount = await db.Areas.CountAsync(f =>
            f.UserId == CurrentUserId &&
            (f.Layer == FeatureTypes.AreaLayers.CentralUrban ||
             f.Layer == FeatureTypes.AreaLayers.SecondaryUrban), cancellationToken);

        if (urbanCount == 0)
            return Ok(new DistrictCoverageResponse(true, "No urban areas to cover."));

        var districtCount = await db.Districts.CountAsync(f => f.UserId == CurrentUserId, cancellationToken);

        if (districtCount == 0)
            return Ok(new DistrictCoverageResponse(false,
                "No districts have been drawn yet. Districts must fully cover all urban areas."));

        bool covered = await validationService.CheckDistrictCoverageAsync(
            RequiredCurrentUserId, DistrictBoundaryToleranceMeters, cancellationToken);

        return Ok(new DistrictCoverageResponse(
            covered,
            covered
                ? "All urban areas are fully covered by districts."
                : "Districts do not yet fully cover all urban areas. Please fill any remaining gaps before proceeding."));
    }

    private bool CheckCoordinateBounds(List<CoordDto> coords, out string? error)
    {
        if (coords.Count > MaxCoordinateCount) { error = $"Too many coordinates (max {MaxCoordinateCount:N0})."; return false; }
        if (coords.Any(c => double.IsNaN(c.Lat) || double.IsInfinity(c.Lat) ||
                            double.IsNaN(c.Lng) || double.IsInfinity(c.Lng)))
        {
            error = "Invalid coordinate values (NaN or Infinity).";
            return false;
        }
        error = null;
        return true;
    }

    private static string FormatDouble(double v) => v.ToString(CultureInfo.InvariantCulture);

    private static string BuildLineStringWkt(List<CoordDto> coords) =>
        $"LINESTRING({string.Join(",", coords.Select(c => $"{FormatDouble(c.Lng)} {FormatDouble(c.Lat)}"))})";

    private static string BuildPolygonWkt(List<CoordDto> coords)
    {
        var pts = coords.Select(c => $"{FormatDouble(c.Lng)} {FormatDouble(c.Lat)}").ToList();
        if (pts[0] != pts[^1]) pts.Add(pts[0]);
        return $"POLYGON(({string.Join(",", pts)}))";
    }
}
