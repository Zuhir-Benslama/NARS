using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NarsApi.Models;

[Table("error_logs")]
public class ErrorLog
{
    [Key, Column("id")]
    public Guid Id { get; set; }

    [Column("user_id")]
    public Guid? UserId { get; set; }

    [Column("level")]
    [MaxLength(20)]
    public string Level { get; set; } = "error";

    [Column("code")]
    [MaxLength(50)]
    public string Code { get; set; } = "";

    [Column("message")]
    public string Message { get; set; } = "";

    [Column("context")]
    public string? Context { get; set; }

    [Column("url")]
    [MaxLength(2048)]
    public string? Url { get; set; }

    [Column("method")]
    [MaxLength(10)]
    public string? Method { get; set; }

    [Column("ip_address")]
    [MaxLength(45)]
    public string? IpAddress { get; set; }

    [Column("user_agent")]
    [MaxLength(500)]
    public string? UserAgent { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
