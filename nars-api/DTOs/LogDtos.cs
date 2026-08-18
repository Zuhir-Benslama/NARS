using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace NarsApi.DTOs;

public record LogBatch(
    [property: JsonPropertyName("logs")]
    [Required] List<LogEntry> Logs
);

public record LogEntry(
    [property: JsonPropertyName("level")][MaxLength(20)] string? Level,
    [property: JsonPropertyName("code")][MaxLength(50)] string? Code,
    [property: JsonPropertyName("message")]
    [Required(AllowEmptyStrings = false), MaxLength(4096)] string Message,
    [property: JsonPropertyName("context")][MaxLength(4096)] string? Context,
    [property: JsonPropertyName("url")][MaxLength(2048)] string? Url,
    [property: JsonPropertyName("method")][MaxLength(10)] string? Method
);
