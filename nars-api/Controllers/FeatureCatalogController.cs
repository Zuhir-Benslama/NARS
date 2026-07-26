using Microsoft.AspNetCore.Mvc;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;

namespace NarsApi.Controllers;

[ApiController]
[Route("/api")]
[Tags("Feature Catalog")]
public class FeatureCatalogController(
    IFeatureStatsService featureStatsService,
    IWebHostEnvironment webHost) : NarsControllerBase(webHost)
{
    private const string IconArea = "\u2B1F";
    private const string IconRoad = "\U0001F6E3️";
    private const string IconDistrict = "\U0001F3D8️";
    private const string IconHouseEntrance = "\U0001F6AA";
    private const string IconPublicBuilding = "\U0001F3DB️";
    private const string IconPublicSpace = "\U0001F333";
    private const string IconCityCenter = "\U0001F3D9️";
    private const string IconNamingPanel = "\U0001FAB5";

    /// <summary>Returns the full catalog of feature types with their available layers.</summary>
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
                Layers:
                [
                    new LayerOption(FeatureTypes.PublicBuildingLayers.Default,                        "Public Building"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.Bank,                          "Bank"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.PostOffice,                    "Post Office"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.ConventionCentre,              "Convention Centre"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.PublicMarket,                  "Public Market"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.TradeCentre,                  "Trade Centre"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.Library,                       "Library"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.Museum,                        "Museum"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.Theater,                       "Theater"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.BordersGuard,                  "Borders Guard"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.Customs,                       "Customs"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.FireStation,                   "Fire Station"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.Gendarmes,                    "Gendarmes"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.MilitaryBarrack,               "Military Barrack"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.PoliceStation,                 "Police Station"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.AdministrativeBranch,          "Administrative Branch"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.PublicHospital,               "Public Hospital"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.NeighborhoodHealth,            "Neighborhood Health"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.SpecializedHospital,           "Specialized Hospital"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.TreatmentRoom,                "Treatment Room"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.UniversityHospital,            "University Hospital"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.ResearchInstitute,             "Research Institute"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.University,                   "University"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.College,                       "College"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.School,                        "School"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.Cemetery,                      "Cemetery"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.Mosque,                        "Mosque"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.Hostel,                        "Hostel"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.Hotel,                         "Hotel"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.Motel,                         "Motel"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.Airport,                       "Airport"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.BusStation,                   "Bus Station"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.TrainStation,                  "Train Station"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.SpecializedVocationalInstitute, "Specialized Vocational Institute"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.VocationalEducationInstitute,   "Vocational Education Institute"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.VocationalApprenticeshipCenter, "Vocational Apprenticeship Center"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.VocationalTrainingInstitute,    "Vocational Training Institute"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.IndoorArena,                   "Indoor Arena"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.LeisureCenter,                 "Leisure Center"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.SportsComplex,                 "Sports Complex"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.Stadium,                       "Stadium"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.SwimmingPool,                 "Swimming Pool"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.YouthClubs,                    "Youth Clubs"),
                    new LayerOption(FeatureTypes.PublicBuildingLayers.YouthHostel,                  "Youth Hostel"),
                ]),
            new(Key: FeatureTypes.PublicSpace, Label: "Public Space", Icon: IconPublicSpace,
                Layers:
                [
                    new LayerOption(FeatureTypes.PublicSpaceLayers.Garden, "Garden"),
                    new LayerOption(FeatureTypes.PublicSpaceLayers.Square, "Square"),
                ]),
            new(Key: FeatureTypes.CityCenter, Label: "City Center", Icon: IconCityCenter,
                Layers: [new LayerOption(FeatureTypes.CityCenterLayers.Default, "City Center")]),
            new(Key: FeatureTypes.NamingPanel, Label: "Naming Panel", Icon: IconNamingPanel,
                Layers: [new LayerOption(FeatureTypes.NamingPanelLayers.Default, "Naming Panel")]),
        };
        return Ok(types);
    }

    /// <summary>Loads features filtered by layer type with pagination.</summary>
    [HttpGet("features/by-layer/{layerType}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> LoadByLayer(string layerType, [FromQuery] int skip = 0, [FromQuery] int take = 100, CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 500);

        var (features, totalCount) = await featureStatsService.LoadByLayerAsync(
            RequiredCurrentUserId, layerType, skip, take, cancellationToken);

        return Ok(new LoadFeaturesResponse<FeatureResult>(
            Features: features,
            Count: totalCount,
            Skip: skip,
            Take: take
        ));
    }
}
