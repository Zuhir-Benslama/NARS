using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;

namespace NarsApi.Controllers;

[ApiController]
[Tags("Locations")]
public class LocationsController(AppDbContext db) : ControllerBase
{
    // ── GET /api/wilayas ──────────────────────────────────────

    [HttpGet("/api/wilayas")]
    public async Task<IActionResult> GetWilayas([FromQuery] string search = "")
    {
        var q = db.Wilayas.AsQueryable();
        if (!string.IsNullOrEmpty(search))
            q = q.Where(w => EF.Functions.ILike(w.WilayaFr!, $"%{search}%")
                          || EF.Functions.ILike(w.WilayaAr!, $"%{search}%"));

        var result = await q.OrderBy(w => w.WilayaFr).ToListAsync();

        return Ok(result.Select(w => new
        {
            id        = w.WilayaId,
            name_fr   = w.WilayaFr,
            name_ar   = w.WilayaAr,
            latitude  = w.WilayaLatitude,
            longitude = w.WilayaLongitude,
        }));
    }

    // ── GET /api/dairas ───────────────────────────────────────

    [HttpGet("/api/dairas")]
    public async Task<IActionResult> GetDairas([FromQuery] int wilaya_id, [FromQuery] string search = "")
    {
        var q = db.Dairas.Where(d => d.WilayaId == wilaya_id);
        if (!string.IsNullOrEmpty(search))
            q = q.Where(d => EF.Functions.ILike(d.DairaFr!, $"%{search}%")
                          || EF.Functions.ILike(d.DairaAr!, $"%{search}%"));

        var result = await q.OrderBy(d => d.DairaFr).ToListAsync();

        return Ok(result.Select(d => new
        {
            id        = d.DairaId,
            name_fr   = d.DairaFr,
            name_ar   = d.DairaAr,
            latitude  = d.DairaLatitude,
            longitude = d.DairaLongitude,
            full_name = d.DairaName,
        }));
    }

    // ── GET /api/communes ─────────────────────────────────────

    [HttpGet("/api/communes")]
    public async Task<IActionResult> GetCommunes([FromQuery] int daira_id, [FromQuery] string search = "")
    {
        var q = db.Communes.Where(c => c.DairaId == daira_id);
        if (!string.IsNullOrEmpty(search))
            q = q.Where(c => EF.Functions.ILike(c.CommuneFr, $"%{search}%")
                          || EF.Functions.ILike(c.CommuneAr, $"%{search}%"));

        var result = await q.OrderBy(c => c.CommuneFr).ToListAsync();

        return Ok(result.Select(c => new
        {
            id        = c.CommuneId,
            name_fr   = c.CommuneFr,
            name_ar   = c.CommuneAr,
            code      = c.CommuneCode,
            latitude  = c.CommuneLatitude,
            longitude = c.CommuneLongitude,
            full_name = c.CommuneName,
        }));
    }

    // ── GET /api/commune/{id}/boundary ────────────────────────

    [HttpGet("/api/commune/{communeId:int}/boundary")]
    public async Task<IActionResult> GetCommuneBoundary(int communeId)
    {
        var commune = await db.Communes.FirstOrDefaultAsync(c => c.CommuneId == communeId);

        // Use ADO.NET directly — SqlQueryRaw routes through EF Core's mapping pipeline
        // which mis-maps the ST_AsGeoJSON text result under UseSnakeCaseNamingConvention()
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();

        string? geoJson = null;
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT ST_AsGeoJSON(geometry) FROM communes_boundaries WHERE commune_id = @id";
            var param = cmd.CreateParameter();
            param.ParameterName = "@id";
            param.Value = communeId;
            cmd.Parameters.Add(param);

            var scalar = await cmd.ExecuteScalarAsync();
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
            commune_id   = communeId,
            commune_name = commune?.CommuneFr,
            geometry     = geoJson,
        });
    }

    // ── GET /api/commune/{id}/boundary-debug ──────────────────

    [HttpGet("/api/commune/{communeId:int}/boundary-debug")]
    public async Task<IActionResult> DebugCommuneBoundary(int communeId)
    {
        var boundary = await db.CommuneBoundaries.FirstOrDefaultAsync(b => b.CommuneId == communeId);
        if (boundary is null)
            return Ok(new { error = "Boundary not found" });

        return Ok(new
        {
            commune_id    = communeId,
            geometry_type = boundary.Geometry.GeometryType,
            num_points    = boundary.Geometry.NumPoints,
            is_valid      = boundary.Geometry.IsValid,
            envelope      = boundary.Geometry.EnvelopeInternal.ToString(),
        });
    }
}
