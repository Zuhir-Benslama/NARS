using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Models;

namespace NarsApi.Controllers;

/// <summary>
/// Read-only / metadata endpoints for feature type definitions and
/// layer-scoped feature queries. Separated from FeaturesController (CRUD)
/// to keep each file focused on one responsibility.
/// </summary>
[ApiController]
[Tags("Feature Catalog")]
public class FeatureCatalogController(AppDbContext db) : NarsControllerBase
{
    // ── GET /api/feature-types ────────────────────────────────────────────────
    // Returns the full type/layer hierarchy used by the frontend to populate
    // selectors. Statically defined — no DB query needed.

    [HttpGet("/api/feature-types")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetFeatureTypes()
    {
        var types = new List<FeatureTypeDefinition>
        {
            new(Key: FeatureTypes.Area, Label: "Area", Icon: "⬟",
                Layers: new[]
                {
                    new LayerOption(FeatureTypes.AreaLayers.CentralUrban,   "Central Urban Area"),
                    new LayerOption(FeatureTypes.AreaLayers.SecondaryUrban, "Secondary Urban Area"),
                    new LayerOption(FeatureTypes.AreaLayers.Scattered,      "Scattered Area"),
                }),
            new(Key: FeatureTypes.Road, Label: "Road", Icon: "🛣️",
                Layers: new[]
                {
                    new LayerOption(FeatureTypes.RoadLayers.Boulevard, "Boulevard", "primary"),
                    new LayerOption(FeatureTypes.RoadLayers.Avenue,    "Avenue",    "primary"),
                    new LayerOption(FeatureTypes.RoadLayers.Street,    "Street",    "secondary"),
                    new LayerOption(FeatureTypes.RoadLayers.Drive,     "Drive",     "tertiary"),
                    new LayerOption(FeatureTypes.RoadLayers.Lane,      "Lane",      "tertiary"),
                    new LayerOption(FeatureTypes.RoadLayers.CulDeSac,  "Cul-de-sac","tertiary"),
                    new LayerOption(FeatureTypes.RoadLayers.Way,       "Way",       "tertiary"),
                }),
            new(Key: FeatureTypes.District, Label: "District", Icon: "🏘️",
                Layers: new[]
                {
                    new LayerOption(FeatureTypes.DistrictLayers.HousingEstate,      "Housing Estate"),
                    new LayerOption(FeatureTypes.DistrictLayers.UrbanPole,          "Urban Pole"),
                    new LayerOption(FeatureTypes.DistrictLayers.District,           "District"),
                    new LayerOption(FeatureTypes.DistrictLayers.TradActivitiesZone, "Trad. Activities Zone"),
                    new LayerOption(FeatureTypes.DistrictLayers.IndustryZone,       "Industry Zone"),
                }),
            new(Key: FeatureTypes.HouseEntrance, Label: "House Entrance", Icon: "🚪",
                Layers: new[]
                {
                    new LayerOption(FeatureTypes.HouseEntranceLayers.Main,      "Main Entrance"),
                    new LayerOption(FeatureTypes.HouseEntranceLayers.Secondary, "Secondary Entrance"),
                }),
            new(Key: FeatureTypes.PublicBuilding, Label: "Public Building", Icon: "🏛️",
                Layers: new[] { new LayerOption(FeatureTypes.PublicBuildingLayers.Default, "Public Building") }),
            new(Key: FeatureTypes.PublicSpace, Label: "Public Space", Icon: "🌳",
                Layers: new[]
                {
                    new LayerOption(FeatureTypes.PublicSpaceLayers.Garden, "Garden"),
                    new LayerOption(FeatureTypes.PublicSpaceLayers.Square, "Square"),
                }),
        };
        return Ok(types);
    }

    // ── POST /api/feature-types/custom ───────────────────────────────────────
    // Stub for future custom type registration.

    [HttpPost("/api/feature-types/custom")]
    public IActionResult AddCustomFeatureType([FromBody] JsonElement body) =>
        Ok(new { success = true, message = "Custom feature type registered successfully." });

    // ── GET /api/load/layer/{layerType} ───────────────────────────────────────
    // Returns all features for the current user that belong to a specific layer.
    // Useful for targeted refreshes (e.g. reload only roads after direction compute).

    [HttpGet("/api/load/layer/{layerType}")]
    public async Task<IActionResult> LoadByLayer(string layerType)
    {
        var uid  = CurrentUserId;
        var rows = new List<object>();

        rows.AddRange((await db.Areas.Where(f => f.UserId == uid && f.Layer == layerType).ToListAsync())
            .Select(f => ToDto(f, "area")));
        rows.AddRange((await db.Districts.Where(f => f.UserId == uid && f.Layer == layerType).ToListAsync())
            .Select(f => ToDto(f, "district")));
        rows.AddRange((await db.Roads.Where(f => f.UserId == uid && f.Layer == layerType).ToListAsync())
            .Select(f => ToDto(f, "road")));
        rows.AddRange((await db.HouseEntrances.Where(f => f.UserId == uid && f.Layer == layerType).ToListAsync())
            .Select(f => ToDto(f, "house_entrance")));
        rows.AddRange((await db.PublicBuildings.Where(f => f.UserId == uid && f.Layer == layerType).ToListAsync())
            .Select(f => ToDto(f, "public_building")));
        rows.AddRange((await db.PublicSpaces.Where(f => f.UserId == uid && f.Layer == layerType).ToListAsync())
            .Select(f => ToDto(f, "public_space")));

        return Ok(rows);
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static object ToDto(FeatureBase f, string type) => new
    {
        id         = f.Id,
        type,
        layer      = f.Layer,
        label      = f.Label,
        data       = JsonSerializer.Deserialize<JsonElement>(f.Data),
        created_at = f.CreatedAt.ToString("o"),
        updated_at = f.UpdatedAt?.ToString("o"),
    };
}
