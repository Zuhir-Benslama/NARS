using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NarsApi.Models;

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

[Table("features")]
public class Feature
{
    [Key, Column("id")]
    public int Id { get; set; }

    [Column("user_id"), Required]
    public int UserId { get; set; }

    // Allowed values: area | road | district | house_entrance |
    //                 public_building | public_space | city_center
    [Column("type"), MaxLength(50), Required]
    public string Type { get; set; } = string.Empty;

    // Sub-type / layer within the top-level type — see FeatureTypes.cs
    [Column("layer"), MaxLength(50), Required]
    public string Layer { get; set; } = string.Empty;

    [Column("label"), MaxLength(500), Required]
    public string Label { get; set; } = string.Empty;

    // Stored as JSONB in PostgreSQL for efficient querying by the validation
    // endpoints (f.data::jsonb->'coordinates', etc.).
    // EF Core maps it as a string; Npgsql transparently handles JSONB <-> string.
    [Column("data", TypeName = "jsonb"), Required]
    public string Data { get; set; } = string.Empty;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime? UpdatedAt { get; set; }
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

    // PostGIS geometry column — queried via raw ADO.NET + ST_AsGeoJSON.
    // NetTopologySuite maps to the PostGIS 'geometry' type via Npgsql.
    [Column("geometry"), Required]
    public NetTopologySuite.Geometries.Geometry Geometry { get; set; } = null!;
}
