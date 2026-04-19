using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NarsApi.Models;

[Table("areas")] public class Area : FeatureBase { }
[Table("districts")] public class District : FeatureBase { }
[Table("city_centers")] public class CityCenter : FeatureBase { }
[Table("roads")] public class Road : FeatureBase { }

[Table("house_entrances")]
public class HouseEntrance : FeatureBase
{
    [Column("road_id")] public Guid? RoadId { get; set; }
}

[Table("public_buildings")] public class PublicBuilding : FeatureBase { }
[Table("public_spaces")] public class PublicSpace : FeatureBase { }
[Table("naming_panels")] public class NamingPanel : FeatureBase { }
