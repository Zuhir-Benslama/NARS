using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NarsApi.Models;

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
