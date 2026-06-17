using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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
    IMemoryCache cache,
    IConfiguration config,
    IBoundaryService boundaryService) : ControllerBase
{
    private const string WilayaCacheKey = "wilayas_all";
    private const string DairaCacheKeyPrefix = "dairas_wilaya_";
    private const string CommuneCacheKeyPrefix = "communes_daira_";
    private TimeSpan ReferenceDataCacheDuration => TimeSpan.FromHours(
        int.TryParse(config["Cache:ReferenceDataDurationHours"], out var h) ? h : 1);

    private async Task<List<T>> CacheOrFetchAsync<T>(string key, Func<Task<List<T>>> fetch)
    {
        return (await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = ReferenceDataCacheDuration;
            return await fetch();
        }))!;
    }

    private async Task<IActionResult> PaginateAsync<TEntity, TDto>(
        string search, int skip, int take,
        string cacheKey,
        Func<IQueryable<TEntity>> baseQuery,
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? searchFilter,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> orderBy,
        Func<TEntity, TDto> mapper,
        CancellationToken cancellationToken)
    {
        take = Math.Clamp(take, 1, 500);

        var maxSearchLength = int.TryParse(config["Locations:MaxSearchLength"], out var msl) ? msl : 200;
        if (search.Length > maxSearchLength)
            return BadRequest(new { detail = $"Search query is too long (max {maxSearchLength} characters)." });

        if (string.IsNullOrEmpty(search) && skip == 0 && take >= 500)
        {
            var cached = await CacheOrFetchAsync(cacheKey, async () =>
            {
                var all = await orderBy(baseQuery()).ToListAsync(cancellationToken);
                return all.Select(mapper).ToList();
            });
            return Ok(new PagedResponse<TDto>(cached, cached.Count, 0, cached.Count));
        }

        var q = baseQuery();
        if (!string.IsNullOrEmpty(search) && searchFilter is not null)
            q = searchFilter(q);

        var total = await q.CountAsync(cancellationToken);
        var result = await orderBy(q).Skip(skip).Take(take).ToListAsync(cancellationToken);
        var items = result.Select(mapper).ToList();

        return Ok(new PagedResponse<TDto>(items, total, skip, take));
    }

    // ── GET /api/wilayas ──────────────────────────────────────

    [HttpGet("wilayas")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetWilayas(
        [FromQuery] string search = "",
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        return await PaginateAsync(
            search, skip, take,
            WilayaCacheKey,
            () => db.Wilayas.AsQueryable(),
            q => q.Where(w => EF.Functions.ILike(w.WilayaFr!, $"%{search}%")
                           || EF.Functions.ILike(w.WilayaAr!, $"%{search}%")),
            q => q.OrderBy(w => w.WilayaFr),
            w => new WilayaItem(w.WilayaId, w.WilayaFr ?? "", w.WilayaAr ?? "", w.WilayaLatitude, w.WilayaLongitude),
            cancellationToken);
    }

    // ── GET /api/dairas ───────────────────────────────────────

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
            return BadRequest(new { detail = "wilaya_id is required." });
        return await PaginateAsync(
            search, skip, take,
            $"{DairaCacheKeyPrefix}{wilaya_id}",
            () => db.Dairas.Where(d => d.WilayaId == wilaya_id),
            q => q.Where(d => EF.Functions.ILike(d.DairaFr, $"%{search}%")
                           || EF.Functions.ILike(d.DairaAr, $"%{search}%")),
            q => q.OrderBy(d => d.DairaFr),
            d => new DairaItem(d.DairaId, d.DairaFr, d.DairaAr, d.DairaLatitude, d.DairaLongitude, d.DairaName ?? ""),
            cancellationToken);
    }

    // ── GET /api/communes ─────────────────────────────────────

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
            return BadRequest(new { detail = "daira_id is required." });
        return await PaginateAsync(
            search, skip, take,
            $"{CommuneCacheKeyPrefix}{daira_id}",
            () => db.Communes.Where(c => c.DairaId == daira_id),
            q => q.Where(c => EF.Functions.ILike(c.CommuneFr, $"%{search}%")
                           || EF.Functions.ILike(c.CommuneAr, $"%{search}%")),
            q => q.OrderBy(c => c.CommuneFr),
            c => new CommuneItem(c.CommuneId, c.CommuneFr, c.CommuneAr, c.CommuneCode?.ToString(), c.CommuneLatitude, c.CommuneLongitude, c.CommuneName ?? ""),
            cancellationToken);
    }

    // ── GET /api/commune/{id}/boundary ────────────────────────

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
            return NotFound(new { detail = "Boundary not found for this commune" });

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

    [HttpGet("commune/{communeId:int}/boundary-debug")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DebugCommuneBoundary(int communeId, IHostEnvironment env, CancellationToken cancellationToken = default)
    {
        if (!env.IsDevelopment())
            return NotFound();

        var boundary = await db.CommuneBoundaries.FirstOrDefaultAsync(b => b.CommuneId == communeId, cancellationToken);
        if (boundary is null)
            return Ok(new { error = "Boundary not found" });

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
