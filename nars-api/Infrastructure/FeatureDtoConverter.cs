using System.Text.Json;
using NarsApi.Models;

namespace NarsApi.Infrastructure;

public static class FeatureDtoConverter
{
    internal const string IsoDateFormat = "o";

    public static FeatureDto ToDto(FeatureBase f, string type) => new(
        Id: f.Id.ToString(),
        Type: type,
        Layer: f.Layer,
        Label: f.Label,
        Data: string.IsNullOrWhiteSpace(f.Data)
            ? JsonSerializer.Deserialize<JsonElement>("{}")
            : JsonSerializer.Deserialize<JsonElement>(f.Data),
        CreatedAt: f.CreatedAt.ToString(IsoDateFormat),
        UpdatedAt: f.UpdatedAt?.ToString(IsoDateFormat)
    );
}

public record FeatureDto(
    string Id,
    string Type,
    string Layer,
    string? Label,
    JsonElement Data,
    string CreatedAt,
    string? UpdatedAt
);
