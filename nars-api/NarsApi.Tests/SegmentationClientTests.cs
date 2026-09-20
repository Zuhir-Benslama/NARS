using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using Xunit;

namespace NarsApi.Tests;

/// <summary>
/// Unit tests for <see cref="SegmentationClient"/>. Routes every HTTP call
/// through a stub <see cref="HttpMessageHandler"/> so no network, service or
/// file-system access is involved; verifies payload parsing, error mapping and
/// culture-invariant query formatting.
/// </summary>
public class SegmentationClientTests
{
    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(respond(request));
        }
    }

    private const double MinLon = 1.5;
    private const double MinLat = -2.25;
    private const double MaxLon = 3.75;
    private const double MaxLat = 4.5;

    private static (SegmentationClient Client, StubHandler Handler) CreateClient(
        HttpResponseMessage response, SegmentationOptions? options = null)
    {
        var handler = new StubHandler(_ => response);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://segma.internal") };
        return (
            new SegmentationClient(http, Options.Create(options ?? new SegmentationOptions()),
                Mock.Of<ILogger<SegmentationClient>>()),
            handler);
    }

    private static Stream Tile() => new MemoryStream([1, 2, 3]);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static readonly (double, double, double, double) Bbox = (MinLon, MinLat, MaxLon, MaxLat);

    [Fact]
    public async Task SegmentTileAsync_BuildingsSuccess_ReturnsBuildingFeatures()
    {
        const string json = """{"buildings":{"features":[{"geometry":{"type":"Polygon","coordinates":[]},"properties":{"confidence":0.8}},{"geometry":{"type":"Polygon","coordinates":[]},"properties":{"confidence":0.6}}]}}""";
        var (client, _) = CreateClient(Json(HttpStatusCode.OK, json));

        var result = await client.SegmentTileAsync(AiDraftFeature.TypeBuilding, Tile(), "tile.png", "image/png", Bbox);

        Assert.Empty(result.Roads);
        Assert.Equal(2, result.Buildings.Count);
        Assert.Equal("""{"type":"Polygon","coordinates":[]}""", result.Buildings[0].GeometryGeoJson);
        Assert.Equal(0.8, result.Buildings[0].Confidence);
        Assert.Equal(0.6, result.Buildings[1].Confidence);
        Assert.All(result.Buildings, f => Assert.Equal(AiDraftFeature.TypeBuilding, f.FeatureType));
    }

    [Fact]
    public async Task SegmentTileAsync_RoadsSuccess_ReturnsRoadFeatures()
    {
        const string json = """{"roads":{"features":[{"geometry":{"type":"LineString","coordinates":[]},"properties":{"confidence":0.9}}]}}""";
        var (client, _) = CreateClient(Json(HttpStatusCode.OK, json));

        var result = await client.SegmentTileAsync(AiDraftFeature.TypeRoad, Tile(), "tile.png", "image/png", Bbox);

        Assert.Empty(result.Buildings);
        var road = Assert.Single(result.Roads);
        Assert.Equal("""{"type":"LineString","coordinates":[]}""", road.GeometryGeoJson);
        Assert.Equal(0.9, road.Confidence);
        Assert.Equal(AiDraftFeature.TypeRoad, road.FeatureType);
    }

    [Fact]
    public async Task SegmentTileAsync_FeatureTypeSelectsEndpointNormalized()
    {
        var (client, handler) = CreateClient(Json(HttpStatusCode.OK,
            """{"roads":{"features":[]}}"""));

        // Callers normalize case/whitespace before calling the client; the
        // client itself only has to match case-insensitively.
        await client.SegmentTileAsync("RoAd", Tile(), "tile.png", "image/png", Bbox);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal("http://segma.internal/segment/roads", handler.LastRequest!.RequestUri?.GetLeftPart(UriPartial.Path));
        Assert.Contains("min_lon=1.5", handler.LastRequest.RequestUri!.Query);
        Assert.Contains("min_lat=-2.25", handler.LastRequest.RequestUri.Query);
        Assert.Contains("max_lon=3.75", handler.LastRequest.RequestUri.Query);
        Assert.Contains("max_lat=4.5", handler.LastRequest.RequestUri.Query);
        Assert.Contains("threshold=0.3", handler.LastRequest.RequestUri.Query);
    }

    [Fact]
    public async Task SegmentTileAsync_ThresholdsFollowFeatureTypeAndConfiguration()
    {
        var (roadClient, roadHandler) = CreateClient(
            Json(HttpStatusCode.OK, """{"roads":{"features":[]}}"""),
            new SegmentationOptions { RoadThreshold = 0.37, BuildingThreshold = 0.55 });
        var (buildingClient, buildingHandler) = CreateClient(
            Json(HttpStatusCode.OK, """{"buildings":{"features":[]}}"""),
            new SegmentationOptions { RoadThreshold = 0.37, BuildingThreshold = 0.55 });

        await roadClient.SegmentTileAsync(AiDraftFeature.TypeRoad, Tile(), "tile.png", "image/png", Bbox);
        await buildingClient.SegmentTileAsync(AiDraftFeature.TypeBuilding, Tile(), "tile.png", "image/png", Bbox);

        Assert.Contains("threshold=0.37", roadHandler.LastRequest!.RequestUri!.Query);
        Assert.Contains("threshold=0.55", buildingHandler.LastRequest!.RequestUri!.Query);
        Assert.Equal("http://segma.internal/segment/buildings", buildingHandler.LastRequest.RequestUri!.GetLeftPart(UriPartial.Path));
    }

    [Fact]
    public async Task SegmentTileAsync_UsesInvariantDecimalSeparator()
    {
        // fr-FR uses a comma decimal separator; the query must still be dot-based.
        var current = CultureInfo.CurrentCulture;
        var currentUi = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
        CultureInfo.CurrentUICulture = new CultureInfo("fr-FR");
        try
        {
            var (client, handler) = CreateClient(Json(HttpStatusCode.OK,
                """{"buildings":{"features":[]}}"""));

            await client.SegmentTileAsync(AiDraftFeature.TypeBuilding, Tile(), "tile.png", "image/png", Bbox);

            Assert.NotNull(handler.LastRequest);
            var query = handler.LastRequest!.RequestUri!.Query;
            Assert.Contains("min_lon=1.5", query);
            Assert.Contains("min_lat=-2.25", query);
            Assert.Contains("max_lon=3.75", query);
            Assert.DoesNotContain(",", query);
        }
        finally
        {
            CultureInfo.CurrentCulture = current;
            CultureInfo.CurrentUICulture = currentUi;
        }
    }

    [Fact]
    public async Task SegmentTileAsync_NonSuccessStatus_ThrowsWithStatusAndBodyLogged()
    {
        var (client, handler) = CreateClient(new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("upstream exploded"),
        });

        var ex = await Assert.ThrowsAsync<SegmentationServiceException>(() =>
            client.SegmentTileAsync(AiDraftFeature.TypeBuilding, Tile(), "tile.png", "image/png", Bbox));

        Assert.Equal("Segmentation service returned 502", ex.Message);
        Assert.Null(ex.InnerException);
        Assert.Equal("http://segma.internal/segment/buildings", handler.LastRequest!.RequestUri!.GetLeftPart(UriPartial.Path));
    }

    [Fact]
    public async Task SegmentTileAsync_MalformedJson_ThrowsSegmentationException()
    {
        var (client, _) = CreateClient(Json(HttpStatusCode.OK, "this is not json"));

        var ex = await Assert.ThrowsAsync<SegmentationServiceException>(() =>
            client.SegmentTileAsync(AiDraftFeature.TypeBuilding, Tile(), "tile.png", "image/png", Bbox));

        Assert.IsAssignableFrom<JsonException>(ex.InnerException);
    }

    [Fact]
    public async Task SegmentTileAsync_MissingFeaturesKey_ThrowsSegmentationException()
    {
        var (client, _) = CreateClient(Json(HttpStatusCode.OK, """{"buildings":{}}"""));

        var ex = await Assert.ThrowsAsync<SegmentationServiceException>(() =>
            client.SegmentTileAsync(AiDraftFeature.TypeBuilding, Tile(), "tile.png", "image/png", Bbox));

        // A genuinely missing key must surface as a segmentation error, NOT a
        // KeyNotFoundException (which the controller maps to "commune not found").
        Assert.IsType<KeyNotFoundException>(ex.InnerException);
    }

    [Fact]
    public async Task SegmentTileAsync_FeatureMissingConfidence_ThrowsSegmentationException()
    {
        var (client, _) = CreateClient(Json(HttpStatusCode.OK,
            """{"buildings":{"features":[{"geometry":{"type":"Polygon"}}]}}"""));

        await Assert.ThrowsAsync<SegmentationServiceException>(() =>
            client.SegmentTileAsync(AiDraftFeature.TypeBuilding, Tile(), "tile.png", "image/png", Bbox));
    }
}
