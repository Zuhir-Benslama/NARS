using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NarsApi.Models;

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
