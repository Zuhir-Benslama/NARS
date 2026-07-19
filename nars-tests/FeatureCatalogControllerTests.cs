using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NarsApi.Controllers;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests;

public class FeatureCatalogControllerTests
{
    private const int ExpectedTypeCount = 8;
    private static FeatureCatalogController CreateController(
        IFeatureStatsService? featureStatsService = null,
        Guid? authenticatedUserId = null)
    {
        var ctrl = new FeatureCatalogController(
            featureStatsService ?? Mock.Of<IFeatureStatsService>(),
            Mock.Of<IWebHostEnvironment>());

        var claims = new List<Claim>();
        if (authenticatedUserId.HasValue)
        {
            claims.Add(new Claim(ClaimNames.UserId, authenticatedUserId.Value.ToString()));
        }

        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth")),
            }
        };

        return ctrl;
    }

    // ── GetFeatureTypes ─────────────────────────────────────────────────

    [Fact]
    public void GetFeatureTypes_ReturnsAllTypes()
    {
        var ctrl = CreateController();

        var result = ctrl.GetFeatureTypes();

        var ok = Assert.IsType<OkObjectResult>(result);
        var types = Assert.IsType<List<FeatureTypeDefinition>>(ok.Value);
        Assert.Equal(ExpectedTypeCount, types.Count);
    }

    [Fact]
    public void GetFeatureTypes_ReturnsCorrectKeys()
    {
        var ctrl = CreateController();

        var result = ctrl.GetFeatureTypes();

        var ok = Assert.IsType<OkObjectResult>(result);
        var types = Assert.IsType<List<FeatureTypeDefinition>>(ok.Value);
        var keys = types.Select(t => t.Key).ToHashSet();
        Assert.Contains(FeatureTypes.Area, keys);
        Assert.Contains(FeatureTypes.Road, keys);
        Assert.Contains(FeatureTypes.District, keys);
        Assert.Contains(FeatureTypes.HouseEntrance, keys);
        Assert.Contains(FeatureTypes.PublicBuilding, keys);
        Assert.Contains(FeatureTypes.PublicSpace, keys);
        Assert.Contains(FeatureTypes.CityCenter, keys);
        Assert.Contains(FeatureTypes.NamingPanel, keys);
    }

    // ── LoadByLayer ─────────────────────────────────────────────────────

    [Fact]
    public async Task LoadByLayer_ValidLayer_ReturnsFeatures()
    {
        var userId = Guid.NewGuid();
        var features = new List<FeatureResult>
        {
            new("id-1", FeatureTypes.Area, FeatureTypes.AreaLayers.CentralUrban, "Area 1",
                System.Text.Json.JsonDocument.Parse("{}").RootElement, "2025-06-01T12:00:00Z"),
        };
        var mock = new Mock<IFeatureStatsService>();
        mock.Setup(s => s.LoadByLayerAsync(
                userId, FeatureTypes.AreaLayers.CentralUrban, 0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((features, 1));

        var ctrl = CreateController(featureStatsService: mock.Object, authenticatedUserId: userId);

        var result = await ctrl.LoadByLayer(FeatureTypes.AreaLayers.CentralUrban, 0, 100);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<LoadFeaturesResponse<FeatureResult>>(ok.Value);
        Assert.Single(resp.Features);
        Assert.Equal(1, resp.Count);
    }

    [Fact]
    public async Task LoadByLayer_TakeClampedTo500()
    {
        var userId = Guid.NewGuid();
        var mock = new Mock<IFeatureStatsService>();
        mock.Setup(s => s.LoadByLayerAsync(
                userId, FeatureTypes.RoadLayers.Boulevard, 0, 500, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<FeatureResult>(), 0));

        var ctrl = CreateController(featureStatsService: mock.Object, authenticatedUserId: userId);

        var result = await ctrl.LoadByLayer(FeatureTypes.RoadLayers.Boulevard, 0, 9999);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<LoadFeaturesResponse<FeatureResult>>(ok.Value);
        Assert.Equal(500, resp.Take);
    }

    [Fact]
    public void GetFeatureTypes_EachTypeHasLayers()
    {
        var ctrl = CreateController();

        var result = ctrl.GetFeatureTypes();

        var ok = Assert.IsType<OkObjectResult>(result);
        var types = Assert.IsType<List<FeatureTypeDefinition>>(ok.Value);
        foreach (var type in types)
        {
            Assert.NotEmpty(type.Layers);
            Assert.NotEmpty(type.Label);
            Assert.NotEmpty(type.Icon);
        }
    }
}
