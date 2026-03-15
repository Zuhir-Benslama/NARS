using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NarsApi.Models;

// ── Shared base ───────────────────────────────────────────────────────────────

public abstract class FeatureBase
{
    [Key, Column("id")]
    public long Id { get; set; }

    [Column("user_id"), Required]
    public int UserId { get; set; }

    [Column("layer"), MaxLength(50), Required]
    public string Layer { get; set; } = string.Empty;

    [Column("label"), MaxLength(500), Required]
    public string Label { get; set; } = string.Empty;

    [Column("data", TypeName = "jsonb"), Required]
    public string Data { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
}

// ── Feature registry ──────────────────────────────────────────────────────────

[Table("feature_registry")]
public class FeatureRegistry
{
    [Key, Column("id")]
    public long Id { get; set; }

    [Column("feature_type"), MaxLength(30), Required]
    public string FeatureType { get; set; } = string.Empty;
}

// ── Feature entities ──────────────────────────────────────────────────────────

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

// ── Location / reference entities ─────────────────────────────────────────────

[Table("users")]
public class User
{
    [Key, Column("id")]
    public int Id { get; set; }

    [Column("name"), MaxLength(255), Required]
    public string Name { get; set; } = string.Empty;

    [Column("email"), MaxLength(255), Required]
    public string Email { get; set; } = string.Empty;

    [Column("phone"), MaxLength(50), Required]
    public string Phone { get; set; } = string.Empty;

    [Column("username"), MaxLength(100), Required]
    public string Username { get; set; } = string.Empty;

    [Column("password_hash"), MaxLength(255), Required]
    public string PasswordHash { get; set; } = string.Empty;

    [Column("commune_id"), Required]
    public int CommuneId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[Table("wilayas")]
public class Wilaya
{
    [Key, Column("wilaya_id"), DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int WilayaId { get; set; }

    [Column("wilaya_ar")]
    public string? WilayaAr { get; set; }

    [Column("wilaya_fr")]
    public string? WilayaFr { get; set; }

    [Column("wilaya_latitude")]
    public double? WilayaLatitude { get; set; }

    [Column("wilaya_longitude")]
    public double? WilayaLongitude { get; set; }
}

[Table("dairas")]
public class Daira
{
    [Key, Column("daira_id")]
    public int DairaId { get; set; }

    [Column("wilaya_id"), Required]
    public int WilayaId { get; set; }

    [Column("daira_ar"), MaxLength(50), Required]
    public string DairaAr { get; set; } = string.Empty;

    [Column("daira_fr"), MaxLength(50), Required]
    public string DairaFr { get; set; } = string.Empty;

    [Column("daira_latitude")]
    public double? DairaLatitude { get; set; }

    [Column("daira_longitude")]
    public double? DairaLongitude { get; set; }

    [Column("daira_name"), MaxLength(255)]
    public string? DairaName { get; set; }
}

[Table("communes")]
public class Commune
{
    [Key, Column("commune_id")]
    public int CommuneId { get; set; }

    [Column("daira_id"), Required]
    public int DairaId { get; set; }

    [Column("commune_code")]
    public int? CommuneCode { get; set; }

    [Column("commune_ar"), MaxLength(100), Required]
    public string CommuneAr { get; set; } = string.Empty;

    [Column("commune_fr"), MaxLength(100), Required]
    public string CommuneFr { get; set; } = string.Empty;

    [Column("commune_latitude")]
    public double? CommuneLatitude { get; set; }

    [Column("commune_longitude")]
    public double? CommuneLongitude { get; set; }

    [Column("commune_name"), MaxLength(255)]
    public string? CommuneName { get; set; }
}

[Table("communes_boundaries")]
public class CommuneBoundary
{
    [Key, Column("commune_id")]
    public int CommuneId { get; set; }

    [Column("geometry"), Required]
    public NetTopologySuite.Geometries.Geometry Geometry { get; set; } = null!;
}
