using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NarsApi.Data;
using NarsApi.DTOs;
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
public class LocationsController(
    AppDbContext db,
    IOptions<LocationsOptions> locationsOptions,
    IBoundaryService boundaryService) : ControllerBase
{

    private async Task<IActionResult> PaginateAsync<TEntity, TDto>(
        string search, int skip, int take,
        Func<IQueryable<TEntity>> baseQuery,
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? searchFilter,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> orderBy,
        Func<TEntity, TDto> mapper,
        CancellationToken cancellationToken)
    {
        take = Math.Clamp(take, 1, 500);

        var maxSearchLength = locationsOptions.Value.MaxSearchLength;
        if (search.Length > maxSearchLength)
        {
            return Problem(detail: $"Search query is too long (max {maxSearchLength} characters).", statusCode: 400);
        }

        var q = baseQuery();
        if (!string.IsNullOrEmpty(search) && searchFilter is not null)
        {
            q = searchFilter(q);
        }

        var total = await q.CountAsync(cancellationToken);
        var result = await orderBy(q).Skip(skip).Take(take).ToListAsync(cancellationToken);
        var items = result.Select(mapper).ToList();

        return Ok(new PagedResponse<TDto>(items, total, skip, take));
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
        CancellationToken cancellationToken = default) => await PaginateAsync(
            search, skip, take,
            () => db.Wilayas.AsQueryable(),
            q => q.Where(w => EF.Functions.ILike(w.WilayaFr!, $"%{search}%")
                           || EF.Functions.ILike(w.WilayaAr!, $"%{search}%")),
            q => q.OrderBy(w => w.WilayaFr),
            w => new WilayaItem(w.WilayaId, w.WilayaFr ?? "", w.WilayaAr ?? "", w.WilayaLatitude, w.WilayaLongitude),
            cancellationToken);

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

        return await PaginateAsync(
            search, skip, take,
            () => db.Dairas.Where(d => d.WilayaId == wilaya_id),
            q => q.Where(d => EF.Functions.ILike(d.DairaFr, $"%{search}%")
                           || EF.Functions.ILike(d.DairaAr, $"%{search}%")),
            q => q.OrderBy(d => d.DairaFr),
            d => new DairaItem(d.DairaId, d.DairaFr, d.DairaAr, d.DairaLatitude, d.DairaLongitude, d.DairaName ?? ""),
            cancellationToken);
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

        return await PaginateAsync(
            search, skip, take,
            () => db.Communes.Where(c => c.DairaId == daira_id),
            q => q.Where(c => EF.Functions.ILike(c.CommuneFr, $"%{search}%")
                           || EF.Functions.ILike(c.CommuneAr, $"%{search}%")),
            q => q.OrderBy(c => c.CommuneFr),
            c => new CommuneItem(c.CommuneId, c.CommuneFr, c.CommuneAr, c.CommuneCode?.ToString(), c.CommuneLatitude, c.CommuneLongitude, c.CommuneName ?? ""),
            cancellationToken);
    }

    // ── GET /api/commune/{id}/boundary ────────────────────────

    /// <summary>Returns the GeoJSON boundary geometry for a commune.</summary>
    [HttpGet("commune/{communeId:int}/boundary")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCommuneBoundary(int communeId, CancellationToken cancellationToken = default)
    {
        var commune = await db.Communes.FirstOrDefaultAsync(c => c.CommuneId == communeId, cancellationToken);

        // Raw ADO.NET required — ST_AsGeoJSON returns a text result that
        // EF Core's Npgsql mapper mis-handles under UseSnakeCaseNamingConvention().
        var geoJson = await boundaryService.GetBoundaryGeoJsonAsync(communeId, cancellationToken);

        if (geoJson is null)
        {
            if (commune?.CommuneLatitude is not null && commune.CommuneLongitude is not null)
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
            communeName = commune?.CommuneFr,
            geometry = geoJson,
        });
    }

    // ── GET /api/commune/{id}/boundary-debug ──────────────────
    // Development-only endpoint for inspecting commune boundary geometry.
    // Returns internal details (geometry type, point count, validity, envelope).

    /// <summary>Development-only endpoint for inspecting commune boundary geometry details.</summary>
    [HttpGet("commune/{communeId:int}/boundary-debug")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DebugCommuneBoundary(int communeId, IHostEnvironment env, CancellationToken cancellationToken = default)
    {
        if (!env.IsDevelopment())
        {
            return NotFound();
        }

        var boundary = await db.CommuneBoundaries.FirstOrDefaultAsync(b => b.CommuneId == communeId, cancellationToken);
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
