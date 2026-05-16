using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NarsApi.Data;
using NarsApi.DTOs;

namespace NarsApi.Controllers;

/// <summary>
/// Administrative hierarchy endpoints (Wilayas, Dairas, Communes) with
/// pagination support. Maximum page size is 500 to prevent oversized responses.
/// Reference data (lists without search) is cached using IMemoryCache.
/// </summary>
[ApiController]
[Route("/api")]
[Tags("Locations")]
public class LocationsController(AppDbContext db, IMemoryCache cache, IConfiguration config) : ControllerBase
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

    // ── GET /api/wilayas ──────────────────────────────────────

    [HttpGet("wilayas")]
    public async Task<IActionResult> GetWilayas(
        [FromQuery] string search = "",
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        // Cap page size to prevent oversized responses.
        take = Math.Clamp(take, 1, 500);

        // Guard against excessively long search strings (defense-in-depth).
        const int MaxSearchLength = 200;
        if (search.Length > MaxSearchLength)
            return BadRequest(new { detail = $"Search query is too long (max {MaxSearchLength} characters)." });

        // Use cache for full list (no search, no pagination)
        if (string.IsNullOrEmpty(search) && skip == 0 && take >= 500)
        {
            var cached = await CacheOrFetchAsync(WilayaCacheKey, async () =>
            {
                var all = await db.Wilayas.OrderBy(w => w.WilayaFr).ToListAsync(cancellationToken);
                return all.Select(w => new WilayaItem(
                    w.WilayaId, w.WilayaFr ?? "", w.WilayaAr ?? "", w.WilayaLatitude, w.WilayaLongitude
                )).ToList();
            });
            return Ok(new PagedResponse<WilayaItem>(cached, cached.Count, 0, cached.Count));
        }

        var q = db.Wilayas.AsQueryable();
        if (!string.IsNullOrEmpty(search))
        {
            q = q.Where(w => EF.Functions.ILike(w.WilayaFr!, $"%{search}%")
                          || EF.Functions.ILike(w.WilayaAr!, $"%{search}%"));
        }

        var total = await q.CountAsync(cancellationToken);
        var result = await q.OrderBy(w => w.WilayaFr).Skip(skip).Take(take).ToListAsync(cancellationToken);

        var items = result.Select(w => new WilayaItem(
            w.WilayaId, w.WilayaFr ?? "", w.WilayaAr ?? "", w.WilayaLatitude, w.WilayaLongitude
        )).ToList();

        return Ok(new PagedResponse<WilayaItem>(items, total, skip, take));
    }

    // ── GET /api/dairas ───────────────────────────────────────

    [HttpGet("dairas")]
    public async Task<IActionResult> GetDairas(
        [FromQuery] int wilaya_id,
        [FromQuery] string search = "",
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        // Cap page size to prevent oversized responses.
        take = Math.Clamp(take, 1, 500);

        // Guard against excessively long search strings (defense-in-depth).
        const int MaxSearchLength = 200;
        if (search.Length > MaxSearchLength)
            return BadRequest(new { detail = $"Search query is too long (max {MaxSearchLength} characters)." });

        // Use cache for full list (no search, no pagination)
        var dairaCacheKey = $"{DairaCacheKeyPrefix}{wilaya_id}";
        if (string.IsNullOrEmpty(search) && skip == 0 && take >= 500)
        {
            var cached = await CacheOrFetchAsync(dairaCacheKey, async () =>
            {
                var all = await db.Dairas.Where(d => d.WilayaId == wilaya_id).OrderBy(d => d.DairaFr).ToListAsync(cancellationToken);
                return all.Select(d => new DairaItem(
                    d.DairaId, d.DairaFr, d.DairaAr, d.DairaLatitude, d.DairaLongitude, d.DairaName ?? ""
                )).ToList();
            });
            return Ok(new PagedResponse<DairaItem>(cached, cached.Count, 0, cached.Count));
        }

        var q = db.Dairas.Where(d => d.WilayaId == wilaya_id);
        if (!string.IsNullOrEmpty(search))
        {
            q = q.Where(d => EF.Functions.ILike(d.DairaFr, $"%{search}%")
                          || EF.Functions.ILike(d.DairaAr, $"%{search}%"));
        }

        var total = await q.CountAsync(cancellationToken);
        var result = await q.OrderBy(d => d.DairaFr).Skip(skip).Take(take).ToListAsync(cancellationToken);

        var items = result.Select(d => new DairaItem(
            d.DairaId, d.DairaFr, d.DairaAr, d.DairaLatitude, d.DairaLongitude, d.DairaName ?? ""
        )).ToList();

        return Ok(new PagedResponse<DairaItem>(items, total, skip, take));
    }

    // ── GET /api/communes ─────────────────────────────────────

    [HttpGet("communes")]
    public async Task<IActionResult> GetCommunes(
        [FromQuery] int daira_id,
        [FromQuery] string search = "",
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        // Cap page size to prevent oversized responses.
        take = Math.Clamp(take, 1, 500);

        // Guard against excessively long search strings (defense-in-depth).
        const int MaxSearchLength = 200;
        if (search.Length > MaxSearchLength)
            return BadRequest(new { detail = $"Search query is too long (max {MaxSearchLength} characters)." });

        // Use cache for full list (no search, no pagination)
        var communeCacheKey = $"{CommuneCacheKeyPrefix}{daira_id}";
        if (string.IsNullOrEmpty(search) && skip == 0 && take >= 500)
        {
            var cached = await CacheOrFetchAsync(communeCacheKey, async () =>
            {
                var all = await db.Communes.Where(c => c.DairaId == daira_id).OrderBy(c => c.CommuneFr).ToListAsync(cancellationToken);
                return all.Select(c => new CommuneItem(
                    c.CommuneId, c.CommuneFr, c.CommuneAr, c.CommuneCode?.ToString(), c.CommuneLatitude, c.CommuneLongitude, c.CommuneName ?? ""
                )).ToList();
            });
            return Ok(new PagedResponse<CommuneItem>(cached, cached.Count, 0, cached.Count));
        }

        var q = db.Communes.Where(c => c.DairaId == daira_id);
        if (!string.IsNullOrEmpty(search))
        {
            q = q.Where(c => EF.Functions.ILike(c.CommuneFr, $"%{search}%")
                          || EF.Functions.ILike(c.CommuneAr, $"%{search}%"));
        }

        var total = await q.CountAsync(cancellationToken);
        var result = await q.OrderBy(c => c.CommuneFr).Skip(skip).Take(take).ToListAsync(cancellationToken);

        var items = result.Select(c => new CommuneItem(
            c.CommuneId, c.CommuneFr, c.CommuneAr, c.CommuneCode?.ToString(), c.CommuneLatitude, c.CommuneLongitude, c.CommuneName ?? ""
        )).ToList();

        return Ok(new PagedResponse<CommuneItem>(items, total, skip, take));
    }

    // ── GET /api/commune/{id}/boundary ────────────────────────

    [HttpGet("commune/{communeId:int}/boundary")]
    public async Task<IActionResult> GetCommuneBoundary(int communeId, CancellationToken cancellationToken = default)
    {
        var commune = await db.Communes.FirstOrDefaultAsync(c => c.CommuneId == communeId, cancellationToken);

        // Use ADO.NET directly — SqlQueryRaw routes through EF Core's mapping pipeline
        // which mis-maps the ST_AsGeoJSON text result under UseSnakeCaseNamingConvention()
        var conn = db.Database.GetDbConnection();

        string? geoJson = null;
        await conn.OpenAsync(cancellationToken);
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT ST_AsGeoJSON(geometry) FROM communes_boundaries WHERE commune_id = @id";
            var param = cmd.CreateParameter();
            param.ParameterName = "@id";
            param.Value = communeId;
            cmd.Parameters.Add(param);

            var scalar = await cmd.ExecuteScalarAsync(cancellationToken);
            geoJson = scalar as string;
        }
        finally
        {
            await conn.CloseAsync();
        }

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
