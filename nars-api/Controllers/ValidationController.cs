using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;

namespace NarsApi.Controllers;

[ApiController]
[Route("/api")]
[Tags("Validation")]
public class ValidationController(
    IOptions<ValidationOptions> validationOptions,
    IValidationService validationService,
    ILogger<ValidationController> logger) : NarsControllerBase
{
    private double DistrictBoundaryToleranceMeters => validationOptions.Value.DistrictBoundaryToleranceMeters;

    private double MaxRoadTurnAngleDegrees => validationOptions.Value.RoadTurnAngleDegrees;

    private double RoadConnectivityDistanceMeters => validationOptions.Value.RoadConnectivityMeters;

    private int MaxCoordinateCount => validationOptions.Value.MaxCoordinateCount;

    /// <summary>Checks whether the user has already created a main urban area.</summary>
    [HttpGet("validate/area/main-urban-exists")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> MainUrbanExists(CancellationToken cancellationToken = default)
    {
        var exists = CurrentUserId.HasValue &&
            await validationService.UserHasCentralUrbanAreaAsync(CurrentUserId.Value, cancellationToken);
        return Ok(new { exists });
    }

    /// <summary>Validates road geometry (turn angles, connectivity to existing roads).</summary>
    [HttpPost("validate/road")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ValidateRoad([FromBody] ValidateRoadRequest body, CancellationToken cancellationToken = default)
    {
        if (body is null)
        {
            return Problem(detail: "Request body is required.", statusCode: 400);
        }

        if (body.Coordinates.Count < 2)
        {
            return Problem(detail: "A road must have at least 2 points.", statusCode: 400);
        }

        if (!CheckCoordinateBounds(body.Coordinates, out var boundsError))
        {
            return Problem(detail: boundsError, statusCode: 400);
        }

        for (var i = 0; i < body.Coordinates.Count - 2; i++)
        {
            var A = body.Coordinates[i];
            var B = body.Coordinates[i + 1];
            var C = body.Coordinates[i + 2];

            double v1x = B.Lng - A.Lng, v1y = B.Lat - A.Lat;
            double v2x = C.Lng - B.Lng, v2y = C.Lat - B.Lat;

            var len1 = Math.Sqrt(v1x * v1x + v1y * v1y);
            var len2 = Math.Sqrt(v2x * v2x + v2y * v2y);

            if (len1 < 1e-10 || len2 < 1e-10)
            {
                continue;
            }

            var dot = (v1x * v2x + v1y * v2y) / (len1 * len2);
            if (dot is < -1.0 or > 1.0)
            {
                logger.LogDebug("Clamping dot product {Dot} at point {Index} (floating-point drift)", dot, i + 2);
            }
            var angle = Math.Acos(Math.Clamp(dot, -1.0, 1.0)) * (180.0 / Math.PI);

            if (angle > MaxRoadTurnAngleDegrees)
            {
                return Ok(new ValidateRoadResponse(false,
                    $"Road turn at point {i + 2} is {angle:F1}°, which exceeds the {MaxRoadTurnAngleDegrees}° maximum."));
            }
        }

        if (CurrentUserId is null || await validationService.CountUserRoadsAsync(CurrentUserId.Value, cancellationToken) == 0)
        {
            return Ok(new ValidateRoadResponse(true, null));
        }

        var wkt = BuildLineStringWkt(body.Coordinates);
        var connected = await validationService.CheckRoadConnectivityAsync(
            RequiredCurrentUserId, wkt, RoadConnectivityDistanceMeters, cancellationToken);

        if (!connected)
        {
            return Ok(new ValidateRoadResponse(false,
                "This road must connect to at least one existing road (within 20 m of an endpoint)."));
        }

        return Ok(new ValidateRoadResponse(true, null));
    }

    /// <summary>Validates district geometry (overlap, adjacency, coverage).</summary>
    [HttpPost("validate/district")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ValidateDistrict([FromBody] ValidateDistrictRequest body, CancellationToken cancellationToken = default)
    {
        if (body is null)
        {
            return Problem(detail: "Request body is required.", statusCode: 400);
        }

        if (body.Coordinates.Count < 3)
        {
            return Problem(detail: "A district must have at least 3 points.", statusCode: 400);
        }

        if (!CheckCoordinateBounds(body.Coordinates, out var inputError))
        {
            return Problem(detail: inputError, statusCode: 400);
        }

        if (CurrentUserId is null || await validationService.CountUserDistrictsAsync(CurrentUserId.Value, cancellationToken) == 0)
        {
            return Ok(new ValidateDistrictResponse(true, null));
        }

        var wkt = BuildPolygonWkt(body.Coordinates);

        if (await validationService.CheckDistrictOverlapAsync(RequiredCurrentUserId, wkt, cancellationToken))
        {
            return Ok(new ValidateDistrictResponse(false,
                "This district overlaps an existing district. Districts must share edges but not overlap."));
        }

        var skipAdjacency = body.DistrictTypeKey is FeatureTypes.DistrictLayers.TradActivitiesZone or
                            FeatureTypes.DistrictLayers.IndustryZone;

        if (!skipAdjacency)
        {
            var siblings = await validationService.CountSiblingsInSameAreaAsync(RequiredCurrentUserId, wkt, cancellationToken);
            if (siblings > 0 && !await validationService.CheckDistrictAdjacencyAsync(RequiredCurrentUserId, wkt, cancellationToken))
            {
                return Ok(new ValidateDistrictResponse(false,
                    "This district does not connect to any existing district in this urban area. Districts must share a boundary (no gaps)."));
            }
        }

        return Ok(new ValidateDistrictResponse(true, null));
    }

    /// <summary>Checks whether the user's districts fully cover all urban areas.</summary>
    [HttpGet("validate/districts/coverage")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> DistrictsCoverage(CancellationToken cancellationToken = default)
    {
        if (CurrentUserId is null)
        {
            return Ok(new DistrictCoverageResponse(true, "No urban areas to cover."));
        }

        var urbanCount = await validationService.CountUserUrbanAreasAsync(CurrentUserId.Value, cancellationToken);
        if (urbanCount == 0)
        {
            return Ok(new DistrictCoverageResponse(true, "No urban areas to cover."));
        }

        var districtCount = await validationService.CountUserDistrictsAsync(CurrentUserId.Value, cancellationToken);

        if (districtCount == 0)
        {
            return Ok(new DistrictCoverageResponse(false,
                "No districts have been drawn yet. Districts must fully cover all urban areas."));
        }

        var covered = await validationService.CheckDistrictCoverageAsync(
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

    private static void AppendWktCoords(StringBuilder sb, List<CoordDto> coords)
    {
        for (var i = 0; i < coords.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(FormatDouble(coords[i].Lng));
            sb.Append(' ');
            sb.Append(FormatDouble(coords[i].Lat));
        }
    }

    private static string BuildLineStringWkt(List<CoordDto> coords)
    {
        var sb = new StringBuilder("LINESTRING(");
        AppendWktCoords(sb, coords);
        sb.Append(')');
        return sb.ToString();
    }

    private static string BuildPolygonWkt(List<CoordDto> coords)
    {
        var sb = new StringBuilder("POLYGON((");
        AppendWktCoords(sb, coords);
        var first = $"{FormatDouble(coords[0].Lng)} {FormatDouble(coords[0].Lat)}";
        var last = $"{FormatDouble(coords[^1].Lng)} {FormatDouble(coords[^1].Lat)}";
        if (first != last)
        {
            sb.Append(',');
            sb.Append(first);
        }
        sb.Append("))");
        return sb.ToString();
    }
}
