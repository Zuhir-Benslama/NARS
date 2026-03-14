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
        public const string Default = "public_building"; // legacy / fallback

        // Banking & Postal
        public const string Bank       = "bank";
        public const string PostOffice = "post_office";

        // Commerce
        public const string ConventionCentre = "convention_centre";
        public const string PublicMarket     = "public_market";
        public const string TradeCentre      = "trade_centre";

        // Culture
        public const string Library = "library";
        public const string Museum  = "museum";
        public const string Theater = "theater";

        // Defence and Security
        public const string BordersGuard    = "borders_guard";
        public const string Customs         = "customs";
        public const string FireStation     = "fire_station";
        public const string Gendarmes       = "gendarmes";
        public const string MilitaryBarrack = "military_barrack";
        public const string PoliceStation   = "police_station";

        // Government & Law
        public const string AdministrativeBranch = "administrative_branch";

        // Healthcare
        public const string PublicHospital      = "public_hospital";
        public const string NeighborhoodHealth  = "neighborhood_health";
        public const string SpecializedHospital = "specialized_hospital";
        public const string TreatmentRoom       = "treatment_room";
        public const string UniversityHospital  = "university_hospital";

        // Higher Education
        public const string ResearchInstitute = "research_institute";
        public const string University        = "university";

        // National Education
        public const string College = "college";
        public const string School  = "school";

        // Religious
        public const string Cemetery = "cemetery";
        public const string Mosque   = "mosque";

        // Tourism
        public const string Hostel = "hostel";
        public const string Hotel  = "hotel";
        public const string Motel  = "motel";

        // Transport
        public const string Airport      = "airport";
        public const string BusStation   = "bus_station";
        public const string TrainStation = "train_station";

        // Vocational Training and Education
        public const string SpecializedVocationalInstitute  = "specialized_vocational_institute";
        public const string VocationalEducationInstitute    = "vocational_education_institute";
        public const string VocationalApprenticeshipCenter  = "vocational_apprenticeship_center";
        public const string VocationalTrainingInstitute     = "vocational_training_institute";

        // Youth & Sports
        public const string IndoorArena   = "indoor_arena";
        public const string LeisureCenter = "leisure_center";
        public const string SportsComplex = "sports_complex";
        public const string Stadium       = "stadium";
        public const string SwimmingPool  = "swimming_pool";
        public const string YouthClubs    = "youth_clubs";
        public const string YouthHostel   = "youth_hostel";

        public static readonly IReadOnlySet<string> All = new HashSet<string>
        {
            Default,
            Bank, PostOffice,
            ConventionCentre, PublicMarket, TradeCentre,
            Library, Museum, Theater,
            BordersGuard, Customs, FireStation, Gendarmes, MilitaryBarrack, PoliceStation,
            AdministrativeBranch,
            PublicHospital, NeighborhoodHealth, SpecializedHospital, TreatmentRoom, UniversityHospital,
            ResearchInstitute, University,
            College, School,
            Cemetery, Mosque,
            Hostel, Hotel, Motel,
            Airport, BusStation, TrainStation,
            SpecializedVocationalInstitute, VocationalEducationInstitute,
            VocationalApprenticeshipCenter, VocationalTrainingInstitute,
            IndoorArena, LeisureCenter, SportsComplex, Stadium, SwimmingPool, YouthClubs, YouthHostel,
        };
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
