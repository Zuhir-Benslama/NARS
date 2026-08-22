using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace NarsApi.Services;

public record SegmentedFeature(string GeometryGeoJson, double Confidence, string FeatureType);

public record SegmentationResult(IReadOnlyList<SegmentedFeature> Buildings);

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

        // InvariantCulture so the service never sees locale-dependent decimal
        // separators (e.g. "2,95" from a comma-locale) in the coordinates.
        var query = string.Create(
            CultureInfo.InvariantCulture,
            $"?min_lon={bbox.MinLon}&min_lat={bbox.MinLat}&max_lon={bbox.MaxLon}&max_lat={bbox.MaxLat}");

        using var response = await _httpClient.PostAsync($"/segment/buildings{query}", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            const int maxErrorBodyLength = 4096;
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(cancellationToken));
            var buffer = new char[maxErrorBodyLength];
            var read = await reader.ReadAsync(buffer, cancellationToken);
            var body = new string(buffer, 0, read);
            _logger.LogError(
                "Segmentation request failed with {StatusCode}: {Body}",
                response.StatusCode, body);
            throw new SegmentationServiceException(
                $"Segmentation service returned {(int)response.StatusCode}");
        }

        // A 200 response can still carry a malformed or unexpected payload
        // (truncated JSON, missing "buildings" key, wrong shape). Surface these
        // as SegmentationServiceException — NOT KeyNotFoundException, which the
        // controller maps to "commune not found" for genuine scope errors.
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        JsonDocument doc;
        try
        {
            doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new SegmentationServiceException("Segmentation service returned malformed JSON.", ex);
        }

        using (doc)
        {
            try
            {
                var buildings = ExtractFeatures(doc.RootElement.GetProperty("buildings").GetProperty("features"), "building");
                return new SegmentationResult(buildings);
            }
            catch (KeyNotFoundException ex)
            {
                throw new SegmentationServiceException(
                    "Segmentation service response has an unexpected shape.", ex);
            }
        }
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

    public SegmentationServiceException(string message, Exception innerException) : base(message, innerException) { }
}
