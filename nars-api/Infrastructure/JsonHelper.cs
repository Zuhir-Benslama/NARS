using System.Text.Json;

namespace NarsApi.Infrastructure;

public static class JsonHelper
{
    public static JsonElement DeserializeSafe(string json)
    {
        try { return JsonSerializer.Deserialize<JsonElement>(json); }
        catch (JsonException) { return JsonDocument.Parse("{}").RootElement; }
    }
}
