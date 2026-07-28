using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NarsApi.Models;

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
