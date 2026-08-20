using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace NarsApi.Infrastructure;

public static class JsonHelper
{
    /// <summary>ISO 8601 round-trip format string for date/time serialization.</summary>
    internal const string IsoDateFormat = "o";

    public static JsonNode? DeserializeSafe(string json, ILogger? logger = null)
    {
        try { return JsonSerializer.Deserialize<JsonNode>(json); }
        catch (JsonException ex)
        {
            logger?.LogWarning(ex, "Failed to deserialize JSON, returning empty object");
            return JsonNode.Parse("{}");
        }
    }
}
