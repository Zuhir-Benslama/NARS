using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Models;
using NarsApi.Services;

namespace NarsApi.Controllers;

[ApiController]
[Tags("Features")]
public class FeaturesController(AppDbContext db, JwtService jwt) : ControllerBase
{
    // ── GET /api/feature-types ────────────────────────────────
    // Returns the full type / layer hierarchy so the frontend
    // can build its UI dynamically without hard-coding values.

    [HttpGet("/api/feature-types")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetFeatureTypes()
    {
        var types = new List<FeatureTypeDefinition>
        {
            new(
                Key:    FeatureTypes.Area,
                Label:  "Area",
                Icon:   "⬟",
                Layers: new[]
                {
                    new LayerOption(FeatureTypes.AreaLayers.CentralUrban,   "Central Urban Area"),
                    new LayerOption(FeatureTypes.AreaLayers.SecondaryUrban, "Secondary Urban Area"),
                    new LayerOption(FeatureTypes.AreaLayers.Scattered,      "Scattered Area"),
                }
            ),
            new(
                Key:    FeatureTypes.Road,
                Label:  "Road",
                Icon:   "🛣️",
                Layers: new[]
                {
                    new LayerOption(FeatureTypes.RoadLayers.Boulevard, "Boulevard", "primary"),
                    new LayerOption(FeatureTypes.RoadLayers.Avenue,    "Avenue",    "primary"),
                    new LayerOption(FeatureTypes.RoadLayers.Street,    "Street",    "secondary"),
                    new LayerOption(FeatureTypes.RoadLayers.Drive,     "Drive",     "tertiary"),
                    new LayerOption(FeatureTypes.RoadLayers.Lane,      "Lane",      "tertiary"),
                    new LayerOption(FeatureTypes.RoadLayers.CulDeSac,  "Cul-de-sac","tertiary"),
                    new LayerOption(FeatureTypes.RoadLayers.Way,       "Way",       "tertiary"),
                }
            ),
            new(
                Key:    FeatureTypes.District,
                Label:  "District",
                Icon:   "🏘️",
                Layers: new[]
                {
                    new LayerOption(FeatureTypes.DistrictLayers.HousingEstate, "Housing Estate"),
                    new LayerOption(FeatureTypes.DistrictLayers.UrbanPole,     "Urban Pole"),
                    new LayerOption(FeatureTypes.DistrictLayers.District,      "District"),
                }
            ),
            new(
                Key:    FeatureTypes.HouseEntrance,
                Label:  "House Entrance",
                Icon:   "🚪",
                Layers: new[]
                {
                    new LayerOption(FeatureTypes.HouseEntranceLayers.Main,      "Main Entrance"),
                    new LayerOption(FeatureTypes.HouseEntranceLayers.Secondary, "Secondary Entrance"),
                }
            ),
            new(
                Key:    FeatureTypes.PublicBuilding,
                Label:  "Public Building",
                Icon:   "🏛️",
                Layers: new[]
                {
                    new LayerOption(FeatureTypes.PublicBuildingLayers.Default, "Public Building"),
                }
            ),
            new(
                Key:    FeatureTypes.PublicSpace,
                Label:  "Public Space",
                Icon:   "🌳",
                Layers: new[]
                {
                    new LayerOption(FeatureTypes.PublicSpaceLayers.Garden, "Garden"),
                    new LayerOption(FeatureTypes.PublicSpaceLayers.Square, "Square"),
                }
            ),
        };

        return Ok(types);
    }

    // ── POST /api/save ────────────────────────────────────────

    [HttpPost("/api/save")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SaveFeature([FromBody] FeatureSaveRequest body)
    {
        if (RequireAuth() is not { } user)
            return Unauthorized(new { detail = "Not authenticated" });

        // Validate type + layer combination
        if (!FeatureTypes.All.Contains(body.Type))
            return BadRequest(new { detail = $"Unknown feature type '{body.Type}'." });

        if (!FeatureTypes.IsValidLayer(body.Type, body.Layer))
            return BadRequest(new { detail = $"Layer '{body.Layer}' is not valid for type '{body.Type}'." });

        var feature = new Feature
        {
            UserId = user.UserId,
            Type   = body.Type,
            Layer  = body.Layer,
            Label  = body.Label,
            Data   = body.Data.ToString(),
        };

        db.Features.Add(feature);
        await db.SaveChangesAsync();

        // After saving an area, recompute scattered areas asynchronously
        if (body.Type == FeatureTypes.Area &&
            (body.Layer == FeatureTypes.AreaLayers.CentralUrban ||
             body.Layer == FeatureTypes.AreaLayers.SecondaryUrban))
        {
            _ = TriggerScatteredRefreshAsync(user.UserId);
        }

        return StatusCode(201, new { success = true, id = feature.Id, message = "Feature saved successfully" });
    }

    // ── GET /api/load ─────────────────────────────────────────

    [HttpGet("/api/load")]
    public async Task<IActionResult> LoadFeatures()
    {
        if (RequireAuth() is not { } user)
            return Unauthorized(new { detail = "Not authenticated" });

        var features = await db.Features
            .Where(f => f.UserId == user.UserId)
            .OrderBy(f => f.CreatedAt)
            .ToListAsync();

        return Ok(features.Select(ToDto));
    }

    // ── POST /api/clear ───────────────────────────────────────

    [HttpPost("/api/clear")]
    public async Task<IActionResult> ClearFeatures()
    {
        if (RequireAuth() is not { } user)
            return Unauthorized(new { detail = "Not authenticated" });

        var count = await db.Features
            .Where(f => f.UserId == user.UserId)
            .ExecuteDeleteAsync();

        return Ok(new { success = true, message = $"Deleted {count} features" });
    }

    // ── DELETE /api/delete/{id} ───────────────────────────────

    [HttpDelete("/api/delete/{featureId:int}")]
    public async Task<IActionResult> DeleteFeature(int featureId)
    {
        if (RequireAuth() is not { } user)
            return Unauthorized(new { detail = "Not authenticated" });

        var feature = await db.Features.FirstOrDefaultAsync(f =>
            f.Id == featureId && f.UserId == user.UserId);

        if (feature is null)
            return NotFound(new { detail = "Feature not found" });

        bool wasUrbanArea = feature.Type == FeatureTypes.Area &&
            (feature.Layer == FeatureTypes.AreaLayers.CentralUrban ||
             feature.Layer == FeatureTypes.AreaLayers.SecondaryUrban);

        db.Features.Remove(feature);
        await db.SaveChangesAsync();

        if (wasUrbanArea)
            _ = TriggerScatteredRefreshAsync(user.UserId);

        return Ok(new { success = true, message = "Feature deleted successfully" });
    }

    // ── GET /api/stats ────────────────────────────────────────

    [HttpGet("/api/stats")]
    public async Task<IActionResult> GetStats()
    {
        if (RequireAuth() is not { } user)
            return Unauthorized(new { detail = "Not authenticated" });

        var groups = await db.Features
            .Where(f => f.UserId == user.UserId)
            .GroupBy(f => f.Type)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .ToListAsync();

        var total = groups.Sum(g => g.Count);
        var stats = groups.ToDictionary(g => g.Type, g => (object)g.Count);
        stats["total"] = total;

        return Ok(stats);
    }

    // ── GET /api/load/layer/{layerType} ───────────────────────

    [HttpGet("/api/load/layer/{layerType}")]
    public async Task<IActionResult> LoadByLayer(string layerType)
    {
        if (RequireAuth() is not { } user)
            return Unauthorized(new { detail = "Not authenticated" });

        var features = await db.Features
            .Where(f => f.UserId == user.UserId && f.Layer == layerType)
            .OrderBy(f => f.CreatedAt)
            .ToListAsync();

        return Ok(features.Select(ToDto));
    }

    // ── GET /api/load/type/{featureType} ──────────────────────

    [HttpGet("/api/load/type/{featureType}")]
    public async Task<IActionResult> LoadByType(string featureType)
    {
        if (RequireAuth() is not { } user)
            return Unauthorized(new { detail = "Not authenticated" });

        var features = await db.Features
            .Where(f => f.UserId == user.UserId && f.Type == featureType)
            .OrderBy(f => f.CreatedAt)
            .ToListAsync();

        return Ok(features.Select(ToDto));
    }

    // ── PUT /api/update/{id} ─────────────────────────────────

    [HttpPut("/api/update/{featureId:int}")]
    public async Task<IActionResult> UpdateFeature(int featureId, [FromBody] FeatureUpdateRequest body)
    {
        if (RequireAuth() is not { } user)
            return Unauthorized(new { detail = "Not authenticated" });

        var feature = await db.Features.FirstOrDefaultAsync(f =>
            f.Id == featureId && f.UserId == user.UserId);

        if (feature is null)
            return NotFound(new { detail = "Feature not found" });

        if (body.Label is not null)
            feature.Label = body.Label;

        if (body.Data is not null)
            feature.Data = body.Data.ToString()!;

        feature.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        return Ok(new { success = true, id = feature.Id, updated_at = feature.UpdatedAt });
    }

    // ── Helpers ───────────────────────────────────────────────

    private (int UserId, string Username)? RequireAuth()
    {
        var token = Request.Cookies["access_token"];
        if (token is null) return null;

        var principal = jwt.ValidateToken(token);
        if (principal is null) return null;

        var userId   = principal.FindFirst("user_id")?.Value;
        var username = principal.FindFirst("username")?.Value;

        if (userId is null) return null;
        return (int.Parse(userId), username ?? string.Empty);
    }

    private static object ToDto(Feature f) => new
    {
        id         = f.Id,
        type       = f.Type,
        layer      = f.Layer,
        label      = f.Label,
        data       = JsonSerializer.Deserialize<JsonElement>(f.Data),
        created_at = f.CreatedAt.ToString("o"),
        updated_at = f.UpdatedAt?.ToString("o"),
    };
    // ── Trigger scattered area recomputation ──────────────────
    // Fires-and-forgets a POST to our own /api/areas/refresh-scattered endpoint.
    // We do this after any urban area save/delete so scattered areas stay current.
    private async Task TriggerScatteredRefreshAsync(int userId)
    {
        try
        {
            // Re-use the cookie from the current request context by calling
            // the validation controller logic directly via the HTTP client factory
            // is complex. For simplicity we replicate the core refresh logic here.
            var token = Request.Cookies["access_token"];
            if (token is null) return;

            var principal = jwt.ValidateToken(token);
            if (principal is null) return;

            if (!int.TryParse(principal.FindFirst("commune_id")?.Value, out int communeId)) return;

            const string PolygonFromDataSql = @"
                ST_SetSRID(ST_GeomFromGeoJSON(
                    json_build_object(
                        'type', 'Polygon',
                        'coordinates', json_build_array((
                            SELECT json_agg(json_build_array(
                                (c->>'lng')::float, (c->>'lat')::float
                            ) ORDER BY ord)
                            FROM jsonb_array_elements(f.data::jsonb->'coordinates')
                            WITH ORDINALITY AS t(c, ord)
                        ))
                    )::text
                ), 4326)";

            var conn = db.Database.GetDbConnection();
            await conn.OpenAsync();
            string? scatteredGeoJson = null;
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $@"
                    WITH
                    boundary AS (
                        SELECT geometry AS geom
                        FROM communes_boundaries
                        WHERE commune_id = @cid
                    ),
                    urban AS (
                        SELECT ST_Union({PolygonFromDataSql}) AS geom
                        FROM features f
                        WHERE f.user_id = @uid
                          AND f.type   = 'area'
                          AND f.layer  IN ('central_urban', 'secondary_urban')
                    )
                    SELECT ST_AsGeoJSON(
                        ST_Difference(
                            boundary.geom,
                            COALESCE(urban.geom, ST_GeomFromText('GEOMETRYCOLLECTION EMPTY', 4326))
                        )
                    )
                    FROM boundary LEFT JOIN urban ON true";

                var p1 = cmd.CreateParameter(); p1.ParameterName = "@cid"; p1.Value = communeId; cmd.Parameters.Add(p1);
                var p2 = cmd.CreateParameter(); p2.ParameterName = "@uid"; p2.Value = userId;    cmd.Parameters.Add(p2);

                scatteredGeoJson = await cmd.ExecuteScalarAsync() as string;
            }
            finally { await conn.CloseAsync(); }

            if (scatteredGeoJson is null) return;

            await db.Features
                .Where(f => f.UserId == userId &&
                            f.Type   == FeatureTypes.Area &&
                            f.Layer  == FeatureTypes.AreaLayers.Scattered)
                .ExecuteDeleteAsync();

            db.Features.Add(new Feature
            {
                UserId = userId,
                Type   = FeatureTypes.Area,
                Layer  = FeatureTypes.AreaLayers.Scattered,
                Label  = "Scattered Area",
                Data   = System.Text.Json.JsonSerializer.Serialize(new
                {
                    type     = "areas",
                    label    = "Scattered Area",
                    layer    = "scattered",
                    geometry = scatteredGeoJson,
                }),
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[RefreshScattered] Error: {ex.Message}");
        }
    }


}