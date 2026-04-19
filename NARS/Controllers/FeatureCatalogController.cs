using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
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

    // ── GET /api/load/layer/{layerType} ───────────────────────────────────────
    // Returns all features for the current user that belong to a specific layer.
    // Useful for targeted refreshes (e.g. reload only roads after direction compute).
    // Supports pagination via ?skip=0&take=100 query parameters.
    // Uses UNION ALL across tables so pagination applies to the combined result.

    [HttpGet("/api/load/layer/{layerType}")]
    public async Task<IActionResult> LoadByLayer(string layerType, [FromQuery] int skip = 0, [FromQuery] int take = 100)
    {
        // Cap page size to prevent oversized responses.
        take = Math.Clamp(take, 1, 500);

        var (features, totalCount) = await FeatureQueryHelper.LoadByLayerAsync(
            db.Database.GetDbConnection(), CurrentUserId, layerType, skip, take);

        return Ok(new
        {
            features,
            count = totalCount,
            skip,
            take,
        });
    }
}
