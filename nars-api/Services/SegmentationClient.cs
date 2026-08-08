using System.Net.Http.Headers;
using System.Text.Json;

namespace NarsApi.Services;

public record SegmentedFeature(string GeometryGeoJson, double Confidence, string FeatureType);

public record SegmentationResult(IReadOnlyList<SegmentedFeature> Roads, IReadOnlyList<SegmentedFeature> Buildings);

public interface ISegmentationClient
{
    Task<SegmentationResult> SegmentTileAsync(
        Stream tileStream,
        string fileName,
        string contentType,
        (double MinLon, double MinLat, double MaxLon, double MaxLat) bbox,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Talks to the nars-roads microservice over the internal cluster network.
/// This client only fetches suggested features - it does not persist
/// anything. Callers (e.g. a draft-features endpoint) are responsible for
/// writing accepted results into ai_draft_features via EF Core.
/// </summary>
public sealed class SegmentationClient : ISegmentationClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SegmentationClient> _logger;

    public SegmentationClient(HttpClient httpClient, ILogger<SegmentationClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<SegmentationResult> SegmentTileAsync(
        Stream tileStream,
        string fileName,
        string contentType,
        (double MinLon, double MinLat, double MaxLon, double MaxLat) bbox,
        CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        using var streamContent = new StreamContent(tileStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(streamContent, "tile", fileName);

        var query = $"?min_lon={bbox.MinLon}&min_lat={bbox.MinLat}&max_lon={bbox.MaxLon}&max_lat={bbox.MaxLat}";

        using var response = await _httpClient.PostAsync($"/segment{query}", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "Segmentation request failed with {StatusCode}: {Body}",
                response.StatusCode, body);
            throw new SegmentationServiceException(
                $"Segmentation service returned {(int)response.StatusCode}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var roads = ExtractFeatures(doc.RootElement.GetProperty("roads").GetProperty("features"), "road");
        var buildings = ExtractFeatures(doc.RootElement.GetProperty("buildings").GetProperty("features"), "building");

        return new SegmentationResult(roads, buildings);
    }

    private static List<SegmentedFeature> ExtractFeatures(JsonElement featureArray, string featureType)
    {
        var results = new List<SegmentedFeature>();

        foreach (var feature in featureArray.EnumerateArray())
        {
            var geometry = feature.GetProperty("geometry").GetRawText();
            var confidence = feature.GetProperty("properties").GetProperty("confidence").GetDouble();
            results.Add(new SegmentedFeature(geometry, confidence, featureType));
        }

        return results;
    }
}

public sealed class SegmentationServiceException : Exception
{
    public SegmentationServiceException(string message) : base(message) { }
}
