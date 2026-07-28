using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NarsApi.Models;

[Table("communes_boundaries")]
public class CommuneBoundary
{
    [Key, Column("commune_id")]
    public int CommuneId { get; set; }

    [Column("geometry"), Required]
    public NetTopologySuite.Geometries.Geometry Geometry { get; set; } = null!;
}
