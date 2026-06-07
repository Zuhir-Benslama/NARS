using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NarsApi.Models;

[Table("inspections")]
public class Inspection
{
    [Key, Column("id")] public Guid Id { get; set; }
    [Column("feature_id"), Required] public Guid FeatureId { get; set; }
    [Column("user_id"), Required] public Guid UserId { get; set; }
    [Column("type"), MaxLength(30), Required] public string Type { get; set; } = string.Empty;
    [Column("data", TypeName = "jsonb"), Required] public string Data { get; set; } = string.Empty;
    [Column("status"), MaxLength(20), Required] public string Status { get; set; } = string.Empty;
    [Column("created_at")] public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [Column("updated_at")] public DateTime? UpdatedAt { get; set; }
}
