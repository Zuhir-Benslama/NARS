using System.Text.Json.Serialization;

namespace NarsApi.DTOs;

public class LogBatch
{
    [JsonPropertyName("logs")]
    public List<LogEntry> Logs { get; set; } = [];
}

public class LogEntry
{
    [JsonPropertyName("level")]
    public string? Level { get; set; }
    [JsonPropertyName("code")]
    public string? Code { get; set; }
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
    [JsonPropertyName("context")]
    public string? Context { get; set; }
    [JsonPropertyName("url")]
    public string? Url { get; set; }
    [JsonPropertyName("method")]
    public string? Method { get; set; }
}
