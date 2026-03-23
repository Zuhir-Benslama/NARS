using System.ComponentModel.DataAnnotations.Schema;

namespace NarsApi.Models;

[Table("areas")]
public class Area : FeatureBase { }

[Table("districts")]
public class District : FeatureBase { }

[Table("city_centers")]
public class CityCenter : FeatureBase { }

[Table("roads")]
public class Road : FeatureBase { }

[Table("house_entrances")]
public class HouseEntrance : FeatureBase
{
    /// <summary>
    /// Extracted from data->roadDbId for indexed road-side queries.
    /// Allows filtering entrances by road without a JSONB extraction per row.
    /// </summary>
    [Column("road_id")]
    public long? RoadId { get; set; }
}

[Table("public_buildings")]
public class PublicBuilding : FeatureBase { }

[Table("public_spaces")]
public class PublicSpace : FeatureBase { }

[Table("naming_panels")]
public class NamingPanel : FeatureBase { }
