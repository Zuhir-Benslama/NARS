namespace NarsApi.Models;

/// <summary>
/// Defines the complete feature-type hierarchy used across NARS.
///
/// Top-level types (Feature.Type):
///   area | road | district | house_entrance | public_building | public_space | city_center
///
/// Sub-types / layers (Feature.Layer) per top-level type:
///   area            → central_urban | secondary_urban | scattered (auto-computed)
///   road            → boulevard | avenue | street | drive | lane | cul_de_sac | way
///   district        → housing_estate | urban_pole | district
///   house_entrance  → main_entrance | secondary_entrance
///   public_building → public_building
///   public_space    → garden | square
///   city_center     → city_center
/// </summary>
public static class FeatureTypes
{
    // ── Top-level type keys ────────────────────────────────────
    public const string Area           = "area";
    public const string Road           = "road";
    public const string District       = "district";
    public const string HouseEntrance  = "house_entrance";
    public const string PublicBuilding = "public_building";
    public const string PublicSpace    = "public_space";
    public const string CityCenter     = "city_center";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Area, Road, District, HouseEntrance, PublicBuilding, PublicSpace, CityCenter
    };

    // ── Layer keys per type ────────────────────────────────────

    public static class AreaLayers
    {
        public const string CentralUrban   = "central_urban";
        public const string SecondaryUrban = "secondary_urban";
        public const string Scattered      = "scattered";  // auto-computed, never user-drawn

        public static readonly IReadOnlySet<string> All = new HashSet<string>
        {
            CentralUrban, SecondaryUrban, Scattered
        };

        public static readonly IReadOnlySet<string> Urban = new HashSet<string>
        {
            CentralUrban, SecondaryUrban
        };
    }

    public static class RoadLayers
    {
        public const string Boulevard = "boulevard";
        public const string Avenue    = "avenue";
        public const string Street    = "street";
        public const string Drive     = "drive";
        public const string Lane      = "lane";
        public const string CulDeSac  = "cul_de_sac";
        public const string Way       = "way";

        public static readonly IReadOnlySet<string> All = new HashSet<string>
        {
            Boulevard, Avenue, Street, Drive, Lane, CulDeSac, Way
        };

        public static readonly IReadOnlySet<string> Primary   = new HashSet<string> { Boulevard, Avenue };
        public static readonly IReadOnlySet<string> Secondary = new HashSet<string> { Street };
        public static readonly IReadOnlySet<string> Tertiary  = new HashSet<string> { Drive, Lane, CulDeSac, Way };
    }

    public static class DistrictLayers
    {
        public const string HousingEstate       = "housing_estate";
        public const string UrbanPole           = "urban_pole";
        public const string District            = "district";
        public const string TradActivitiesZone  = "trad_activities_zone";
        public const string IndustryZone        = "industry_zone";

        public static readonly IReadOnlySet<string> All = new HashSet<string>
        {
            HousingEstate, UrbanPole, District, TradActivitiesZone, IndustryZone
        };
    }

    public static class HouseEntranceLayers
    {
        public const string Main      = "main_entrance";
        public const string Secondary = "secondary_entrance";

        public static readonly IReadOnlySet<string> All = new HashSet<string>
        {
            Main, Secondary
        };
    }

    public static class PublicBuildingLayers
    {
        public const string Default = "public_building";
        public static readonly IReadOnlySet<string> All = new HashSet<string> { Default };
    }

    public static class PublicSpaceLayers
    {
        public const string Garden = "garden";
        public const string Square = "square";

        public static readonly IReadOnlySet<string> All = new HashSet<string>
        {
            Garden, Square
        };
    }

    public static class CityCenterLayers
    {
        public const string Default = "city_center";
        public static readonly IReadOnlySet<string> All = new HashSet<string> { Default };
    }

    // ── Validation helper ──────────────────────────────────────

    public static bool IsValidLayer(string type, string layer) => type switch
    {
        Area           => AreaLayers.All.Contains(layer),
        Road           => RoadLayers.All.Contains(layer),
        District       => DistrictLayers.All.Contains(layer),
        HouseEntrance  => HouseEntranceLayers.All.Contains(layer),
        PublicBuilding => PublicBuildingLayers.All.Contains(layer),
        PublicSpace    => PublicSpaceLayers.All.Contains(layer),
        CityCenter     => CityCenterLayers.All.Contains(layer),
        _              => false,
    };
}
