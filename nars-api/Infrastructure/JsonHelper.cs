using System.Text.Json;
using System.Text.Json.Nodes;

namespace NarsApi.Infrastructure;

public static class JsonHelper
{
    public static JsonNode? DeserializeSafe(string json)
    {
        try { return JsonSerializer.Deserialize<JsonNode>(json); }
        catch (JsonException) { return JsonNode.Parse("{}"); }
    }
}
