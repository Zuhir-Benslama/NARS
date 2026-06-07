using System.Text.Json;
using NarsApi.Models;

namespace NarsApi.Infrastructure;

/// <summary>
/// Shared helper for converting FeatureBase entities to DTO objects.
/// </summary>
public static class FeatureDtoConverter
{
    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement;

    public static object ToDto(FeatureBase f, string type) => new
    {
        id = f.Id.ToString(),
        type,
        layer = f.Layer,
        label = f.Label,
        data = string.IsNullOrWhiteSpace(f.Data)
            ? EmptyObject
            : JsonSerializer.Deserialize<JsonElement>(f.Data),
        created_at = f.CreatedAt.ToString("o"),
        updated_at = f.UpdatedAt?.ToString("o"),
    };
}
