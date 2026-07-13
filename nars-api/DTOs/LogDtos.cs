using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace NarsApi.DTOs;

public record LogBatch(
    [property: JsonPropertyName("logs")]
    [Required] List<LogEntry> Logs
);

public record LogEntry(
    [property: JsonPropertyName("level")] string? Level,
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("message")]
    [Required(AllowEmptyStrings = false)] string Message,
    [property: JsonPropertyName("context")] string? Context,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("method")] string? Method
);
