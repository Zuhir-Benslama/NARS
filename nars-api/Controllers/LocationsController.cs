using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NarsApi.Infrastructure;
using NarsApi.Services;

namespace NarsApi.Controllers;

/// <summary>
/// Administrative hierarchy endpoints (Wilayas, Dairas, Communes) with
/// pagination support. Maximum page size is 500 to prevent oversized responses.
/// Reference data (lists without search) is cached using IMemoryCache.
///
/// INTENTIONALLY UNAUTHENTICATED: These endpoints are consumed by the login
/// page for admin signup (wilaya/daira/commune dropdowns) and by the map page
/// for boundary display. The data is public reference data.
/// </summary>
[ApiController]
[Route("/api")]
[Tags("Locations")]
#pragma warning disable S6960 // LocationsController intentionally serves paginated reference data + boundary endpoints
public class LocationsController(
    IOptions<LocationsOptions> locationsOptions,
    IBoundaryService boundaryService,
    ILocationQueryService locationQuery,
    ILocationSearchService locationSearch,
    IWebHostEnvironment environment) : ControllerBase
{

    private static string EscapeLikeWildcards(string input)
        => input.Replace("%", "\\%").Replace("_", "\\_", StringComparison.Ordinal);

    /// <summary>
    /// Validates the search length and escapes LIKE wildcards. Returns an error
    /// <see cref="IActionResult"/> when the search is too long, or null on success
    /// with <paramref name="sanitized"/> holding the escaped search term.
    /// </summary>
    private IActionResult? ValidateSearch(string? search, out string sanitized)
    {
        search ??= "";
        sanitized = EscapeLikeWildcards(search);
        var maxSearchLength = locationsOptions.Value.MaxSearchLength;
        if (search.Length > maxSearchLength)
        {
            return Problem(detail: $"Search query is too long (max {maxSearchLength} characters).", statusCode: 400);
        }

        return null;
    }

    // ── GET /api/wilayas ──────────────────────────────────────

    /// <summary>Lists wilayas with optional search and pagination.</summary>
    [HttpGet("wilayas")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetWilayas(
        [FromQuery] string search = "",
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        skip = Math.Max(skip, 0);
        take = Math.Clamp(take, 1, 500);
        if (ValidateSearch(search, out var sanitized) is { } error)
        {
            return error;
        }

        var result = await locationSearch.SearchWilayasAsync(sanitized, skip, take, cancellationToken);
        return Ok(result);
    }

    // ── GET /api/dairas ───────────────────────────────────────

    /// <summary>Lists dairas within a wilaya with optional search and pagination.</summary>
    [HttpGet("dairas")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetDairas(
        [FromQuery] int wilaya_id,
        [FromQuery] string search = "",
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        if (wilaya_id <= 0)
        {
            return Problem(detail: "wilaya_id is required.", statusCode: 400);
        }

        skip = Math.Max(skip, 0);
        take = Math.Clamp(take, 1, 500);
        if (ValidateSearch(search, out var sanitized) is { } error)
        {
            return error;
        }

        var result = await locationSearch.SearchDairasAsync(wilaya_id, sanitized, skip, take, cancellationToken);
        return Ok(result);
    }

    // ── GET /api/communes ─────────────────────────────────────

    /// <summary>Lists communes within a daira with optional search and pagination.</summary>
    [HttpGet("communes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetCommunes(
        [FromQuery] int? daira_id,
        [FromQuery] string search = "",
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        if (daira_id is null or <= 0)
        {
            return Problem(detail: "daira_id is required.", statusCode: 400);
        }

        skip = Math.Max(skip, 0);
        take = Math.Clamp(take, 1, 500);
        if (ValidateSearch(search, out var sanitized) is { } error)
        {
            return error;
        }

        var result = await locationSearch.SearchCommunesAsync(daira_id.Value, sanitized, skip, take, cancellationToken);
        return Ok(result);
    }

    // ── GET /api/commune/{id}/boundary ────────────────────────

    /// <summary>Returns the GeoJSON boundary geometry for a commune.</summary>
    [HttpGet("commune/{communeId:int}/boundary")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCommuneBoundary(int communeId, CancellationToken cancellationToken = default)
    {
        var commune = await locationQuery.GetCommuneByIdAsync(communeId, cancellationToken);
        if (commune is null)
        {
            return Problem(detail: "Commune not found.", statusCode: 404);
        }

        // Raw ADO.NET required — ST_AsGeoJSON returns a text result that
        // EF Core's Npgsql mapper mis-handles under UseSnakeCaseNamingConvention().
        var geoJson = await boundaryService.GetBoundaryGeoJsonAsync(communeId, cancellationToken);

        if (geoJson is null)
        {
            if (commune.CommuneLatitude is not null && commune.CommuneLongitude is not null)
            {
                geoJson = JsonSerializer.Serialize(new
                {
                    type = "Point",
                    coordinates = new[] { commune.CommuneLongitude.Value, commune.CommuneLatitude.Value }
                });
            }
            else
            {
                return Problem(detail: "Boundary not found for this commune", statusCode: 404);
            }
        }

        return Ok(new
        {
            communeId,
            communeName = commune.CommuneFr,
            geometry = geoJson,
        });
    }

    // ── GET /api/commune/{id}/boundary-debug ──────────────────
    // Development-only endpoint for inspecting commune boundary geometry.

    /// <summary>Development-only endpoint for inspecting commune boundary geometry details.</summary>
    [HttpGet("commune/{communeId:int}/boundary-debug")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DebugCommuneBoundary(int communeId, CancellationToken cancellationToken = default)
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        var boundary = await locationQuery.GetCommuneBoundaryAsync(communeId, cancellationToken);
        if (boundary is null)
        {
            return Problem(detail: "Boundary not found", statusCode: 404);
        }

        return Ok(new
        {
            communeId,
            geometryType = boundary.Geometry.GeometryType,
            numPoints = boundary.Geometry.NumPoints,
            isValid = boundary.Geometry.IsValid,
            envelope = boundary.Geometry.EnvelopeInternal.ToString(),
        });
    }
}
