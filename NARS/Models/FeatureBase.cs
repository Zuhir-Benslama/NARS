using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NarsApi.Models;

/// <summary>
/// Abstract base class shared by all 8 feature tables.
/// </summary>
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

/// <summary>
/// O(1) routing table: maps a global feature ID to its concrete table type,
/// enabling PUT/DELETE to resolve the right DbSet without scanning all tables.
/// </summary>
[Table("feature_registry")]
public class FeatureRegistry
{
    [Key, Column("id")]
    public long Id { get; set; }

    [Column("feature_type"), MaxLength(30), Required]
    public string FeatureType { get; set; } = string.Empty;
}
