namespace NarsApi.Infrastructure;

public static class FeatureDtoConverter
{
    internal const string IsoDateFormat = "o";
}

public record FeatureDto(
    string Id,
    string Type,
    string Layer,
    string? Label,
    System.Text.Json.JsonElement Data,
    string CreatedAt,
    string? UpdatedAt
);
