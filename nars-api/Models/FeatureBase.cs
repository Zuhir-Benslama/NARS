using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NarsApi.Models;

public abstract class FeatureBase
{
    [Key, Column("id")] public Guid Id { get; set; }
    [Column("user_id"), ForeignKey(nameof(User))] public Guid UserId { get; set; }
    [Column("layer"), MaxLength(50), Required] public string Layer { get; set; } = string.Empty;
    [Column("label"), MaxLength(500), Required] public string Label { get; set; } = string.Empty;
    [Column("data", TypeName = "jsonb"), Required, MaxLength(1048576)] public string Data { get; set; } = string.Empty;
    [Column("created_at")] public DateTime CreatedAt { get; set; }
    [Column("updated_at")] public DateTime? UpdatedAt { get; set; }
    [Timestamp] public uint Version { get; set; }
}

[Table("feature_registry")]
public class FeatureRegistry
{
    [Key, Column("id")] public Guid Id { get; set; }
    [Column("feature_type"), MaxLength(30), Required] public string FeatureType { get; set; } = string.Empty;
}
