using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NarsApi.Models;

/// <summary>
/// Stores hashed refresh tokens. Allows token revocation and rotation.
/// </summary>
[Table("refresh_tokens")]
public class RefreshToken
{
    [Key, Column("id")] public Guid Id { get; set; }

    [Column("user_id"), Required] public Guid UserId { get; set; }

    /// <summary>SHA-256 hash of the raw refresh token.</summary>
    [Column("token_hash"), MaxLength(64), Required]
    public string TokenHash { get; set; } = string.Empty;

    [Column("expires_at")] public DateTime ExpiresAt { get; set; }

    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("revoked")] public bool Revoked { get; set; }
}
