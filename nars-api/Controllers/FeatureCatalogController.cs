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
[Tags("Feature Catalog")]
public class FeatureCatalogController(
    IFeatureStatsService featureStatsService) : NarsControllerBase
{
    private const string IconArea = "\u2B1F";
    private const string IconRoad = "\U0001F6E3️";
    private const string IconDistrict = "\U0001F3D8️";
    private const string IconHouseEntrance = "\U0001F6AA";
    private const string IconPublicBuilding = "\U0001F3DB️";
    private const string IconPublicSpace = "\U0001F333";

    [HttpGet("feature-types")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetFeatureTypes()
    {
        var types = new List<FeatureTypeDefinition>
        {
            new(Key: FeatureTypes.Area, Label: "Area", Icon: IconArea,
                Layers:
                [
                    new LayerOption(FeatureTypes.AreaLayers.CentralUrban,   "Central Urban Area"),
                    new LayerOption(FeatureTypes.AreaLayers.SecondaryUrban, "Secondary Urban Area"),
                    new LayerOption(FeatureTypes.AreaLayers.Scattered,      "Scattered Area"),
                ]),
            new(Key: FeatureTypes.Road, Label: "Road", Icon: IconRoad,
                Layers:
                [
                    new LayerOption(FeatureTypes.RoadLayers.Boulevard, "Boulevard", "primary"),
                    new LayerOption(FeatureTypes.RoadLayers.Avenue,    "Avenue",    "primary"),
                    new LayerOption(FeatureTypes.RoadLayers.Street,    "Street",    "secondary"),
                    new LayerOption(FeatureTypes.RoadLayers.Drive,     "Drive",     "tertiary"),
                    new LayerOption(FeatureTypes.RoadLayers.Lane,      "Lane",      "tertiary"),
                    new LayerOption(FeatureTypes.RoadLayers.CulDeSac,  "Cul-de-sac","tertiary"),
                    new LayerOption(FeatureTypes.RoadLayers.Way,       "Way",       "tertiary"),
                ]),
            new(Key: FeatureTypes.District, Label: "District", Icon: IconDistrict,
                Layers:
                [
                    new LayerOption(FeatureTypes.DistrictLayers.HousingEstate,      "Housing Estate"),
                    new LayerOption(FeatureTypes.DistrictLayers.UrbanPole,          "Urban Pole"),
                    new LayerOption(FeatureTypes.DistrictLayers.DistrictLayer,      "District"),
                    new LayerOption(FeatureTypes.DistrictLayers.TradActivitiesZone, "Trad. Activities Zone"),
                    new LayerOption(FeatureTypes.DistrictLayers.IndustryZone,       "Industry Zone"),
                ]),
            new(Key: FeatureTypes.HouseEntrance, Label: "House Entrance", Icon: IconHouseEntrance,
                Layers:
                [
                    new LayerOption(FeatureTypes.HouseEntranceLayers.Main,      "Main Entrance"),
                    new LayerOption(FeatureTypes.HouseEntranceLayers.Secondary, "Secondary Entrance"),
                ]),
            new(Key: FeatureTypes.PublicBuilding, Label: "Public Building", Icon: IconPublicBuilding,
                Layers: [new LayerOption(FeatureTypes.PublicBuildingLayers.Default, "Public Building")]),
            new(Key: FeatureTypes.PublicSpace, Label: "Public Space", Icon: IconPublicSpace,
                Layers:
                [
                    new LayerOption(FeatureTypes.PublicSpaceLayers.Garden, "Garden"),
                    new LayerOption(FeatureTypes.PublicSpaceLayers.Square, "Square"),
                ]),
        };
        return Ok(types);
    }

    [HttpGet("load/layer/{layerType}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> LoadByLayer(string layerType, [FromQuery] int skip = 0, [FromQuery] int take = 100, CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 500);

        var (features, totalCount) = await featureStatsService.LoadByLayerAsync(
            RequiredCurrentUserId, layerType, skip, take, cancellationToken);

        return Ok(new LoadFeaturesResponse(
            Features: features,
            Count: totalCount,
            Skip: skip,
            Take: take
        ));
    }
}
