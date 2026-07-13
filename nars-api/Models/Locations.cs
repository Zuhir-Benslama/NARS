using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NarsApi.Models;

[Table("users")]
public class User
{
    [Key, Column("id")] public Guid Id { get; set; }
    [Column("name"), MaxLength(255), Required] public string Name { get; set; } = string.Empty;
    [Column("email"), MaxLength(255), Required] public string Email { get; set; } = string.Empty;
    [Column("phone"), MaxLength(50), Required] public string Phone { get; set; } = string.Empty;
    [Column("username"), MaxLength(100), Required] public string Username { get; set; } = string.Empty;
    [Column("password_hash"), MaxLength(255), Required] public string PasswordHash { get; set; } = string.Empty;
    /// <summary>
    /// Geographic scope for commune users. Null for admin accounts which are
    /// scoped to a daira, wilaya, or the whole country instead.
    /// </summary>
    [Column("commune_id")] public int? CommuneId { get; set; }
    /// <summary>Geographic scope for daira_admin accounts.</summary>
    [Column("daira_id")] public int? DairaId { get; set; }
    /// <summary>Geographic scope for wilaya_admin accounts.</summary>
    [Column("wilaya_id")] public int? WilayaId { get; set; }
    /// <summary>Role: commune_user | daira_admin | wilaya_admin | national_admin</summary>
    [Column("role"), MaxLength(20), Required] public string Role { get; set; } = NarsApi.Infrastructure.UserRoles.CommuneUser;
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [Column("failed_login_attempts")] public int? FailedLoginAttempts { get; set; }
    [Column("locked_until")] public DateTime? LockedUntil { get; set; }
}

[Table("wilayas")]
public class Wilaya
{
    [Key, Column("wilaya_id"), DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int WilayaId { get; set; }

    [Column("wilaya_ar"), MaxLength(100)] public string? WilayaAr { get; set; }
    [Column("wilaya_fr"), MaxLength(100)] public string? WilayaFr { get; set; }
    [Column("wilaya_latitude")] public double? WilayaLatitude { get; set; }
    [Column("wilaya_longitude")] public double? WilayaLongitude { get; set; }
}

[Table("dairas")]
public class Daira
{
    [Key, Column("daira_id")]
    public int DairaId { get; set; }

    [Column("wilaya_id"), Required]
    public int WilayaId { get; set; }

    [Column("daira_ar"), MaxLength(50), Required] public string DairaAr { get; set; } = string.Empty;
    [Column("daira_fr"), MaxLength(50), Required] public string DairaFr { get; set; } = string.Empty;
    [Column("daira_latitude")] public double? DairaLatitude { get; set; }
    [Column("daira_longitude")] public double? DairaLongitude { get; set; }
    [Column("daira_name"), MaxLength(255)] public string? DairaName { get; set; }
}

[Table("communes")]
public class Commune
{
    [Key, Column("commune_id")]
    public int CommuneId { get; set; }

    [Column("daira_id"), Required]
    public int DairaId { get; set; }

    [Column("commune_code")] public int? CommuneCode { get; set; }

    [Column("commune_ar"), MaxLength(100), Required] public string CommuneAr { get; set; } = string.Empty;
    [Column("commune_fr"), MaxLength(100), Required] public string CommuneFr { get; set; } = string.Empty;
    [Column("commune_latitude")] public double? CommuneLatitude { get; set; }
    [Column("commune_longitude")] public double? CommuneLongitude { get; set; }
    [Column("commune_name"), MaxLength(255)] public string? CommuneName { get; set; }
}

[Table("communes_boundaries")]
public class CommuneBoundary
{
    [Key, Column("commune_id")]
    public int CommuneId { get; set; }

    [Column("geometry"), Required]
    public NetTopologySuite.Geometries.Geometry Geometry { get; set; } = null!;
}
