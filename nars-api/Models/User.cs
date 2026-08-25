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
    [Column("created_at")] public DateTime CreatedAt { get; set; }
    [Column("failed_login_attempts")] public int FailedLoginAttempts { get; set; }
    [Column("locked_until")] public DateTime? LockedUntil { get; set; }
    /// <summary>
    /// Random per-user value embedded in issued JWTs and re-checked against the
    /// database on every authenticated request. Rotating it (on lockout or a
    /// password change) instantly invalidates all previously issued access
    /// tokens. Null/empty on legacy rows until they next sign in.
    /// </summary>
    [Column("security_stamp"), MaxLength(64)] public string SecurityStamp { get; set; } = string.Empty;

    /// <summary>Generates a fresh, unpredictable security stamp.</summary>
    public static string GenerateSecurityStamp() => Guid.NewGuid().ToString("N");
}
