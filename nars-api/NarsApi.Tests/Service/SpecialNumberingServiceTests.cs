using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using Xunit;

namespace NarsApi.Tests.Service;

[Collection(PostgreSqlCollection.CollectionName)]
[Trait("Category", "Service")]
public class SpecialNumberingServiceTests(NarsDatabaseFixture fixture) : ServiceTestBase(fixture)
{
    private Guid _userId;

    protected override async Task SeedAsync()
    {
        await SeedData.SeedBasicLocationsAsync(Db);
        var user = await SeedData.CreateUserAsync(Db, UserRoles.FieldWorker, communeId: 1, name: "Special Numbering User");
        _userId = user.Id;
    }

    private SpecialNumberingService CreateService() => new(Fixture.CreateDbContextFactory());

    private static string RingData(params (double Lat, double Lng)[] vertices) =>
        JsonSerializer.Serialize(new
        {
            coordinates = vertices.Select(v => new { lat = v.Lat, lng = v.Lng }).ToArray(),
        });

    private static string PointData(double lat, double lng) =>
        JsonSerializer.Serialize(new { lat, lng });

    private async Task<Guid> AddDistrictAsync(string layer, string data)
    {
        var id = Guid.NewGuid();
        Db.Districts.Add(new District
        {
            Id = id,
            UserId = _userId,
            Layer = layer,
            Label = layer,
            Data = data,
        });
        await Db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> AddCityCenterAsync(string layer, double lat, double lng, double radius)
    {
        var id = Guid.NewGuid();
        Db.CityCenters.Add(new CityCenter
        {
            Id = id,
            UserId = _userId,
            Layer = layer,
            Label = layer,
            Data = JsonSerializer.Serialize(new { lat, lng, radius }),
        });
        await Db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> AddPublicSpaceAsync(string layer, string data)
    {
        var id = Guid.NewGuid();
        Db.PublicSpaces.Add(new PublicSpace
        {
            Id = id,
            UserId = _userId,
            Layer = layer,
            Label = layer,
            Data = data,
        });
        await Db.SaveChangesAsync();
        return id;
    }

    private async Task<Guid> AddEntranceAsync(double lat, double lng, Guid? mainId = null)
    {
        var id = Guid.NewGuid();
        var data = mainId is null
            ? PointData(lat, lng)
            : JsonSerializer.Serialize(new { lat, lng, mainEntranceDbId = mainId.Value.ToString() });
        Db.HouseEntrances.Add(new HouseEntrance
        {
            Id = id,
            UserId = _userId,
            Layer = mainId is null ? FeatureTypes.HouseEntranceLayers.Main : FeatureTypes.HouseEntranceLayers.Secondary,
            Label = "Entrance",
            Data = data,
        });
        await Db.SaveChangesAsync();
        return id;
    }

    private async Task<JsonDocument> GetDataAsync(Guid id)
    {
        var row = await Db.HouseEntrances.AsNoTracking().FirstAsync(e => e.Id == id);
        return JsonDocument.Parse(row.Data);
    }

    [Fact]
    public async Task NumberAsync_CiteFromGate_AssignsSequentialNumbersAndLetterSuffixes()
    {
        var ring = RingData(
            (35.98, 2.98), (35.98, 3.02), (36.02, 3.02), (36.02, 2.98), (35.98, 2.98));
        var featureId = await AddDistrictAsync(FeatureTypes.DistrictLayers.Cite, ring);

        var east = await AddEntranceAsync(36.0, 3.01);
        var north = await AddEntranceAsync(36.01, 3.0);
        var west = await AddEntranceAsync(36.0, 2.99);
        var south = await AddEntranceAsync(35.99, 3.0);
        var secondA = await AddEntranceAsync(36.0, 3.0101, east);
        var secondB = await AddEntranceAsync(36.0001, 3.01, east);

        var numbered = await CreateService().NumberAsync(
            _userId, FeatureTypes.SpecialNumberingModes.Cite, featureId,
            [east, north, west, south, secondA, secondB], (36.0, 3.01), CancellationToken.None);

        Assert.NotNull(numbered);
        Assert.Equal(6, numbered.Count);
        Assert.Equal(1, numbered.Single(r => r.Id == east.ToString()).EntranceNumber);
        Assert.Equal(2, numbered.Single(r => r.Id == north.ToString()).EntranceNumber);
        Assert.Equal(3, numbered.Single(r => r.Id == west.ToString()).EntranceNumber);
        Assert.Equal(4, numbered.Single(r => r.Id == south.ToString()).EntranceNumber);

        var suffixA = numbered.Single(r => r.Id == secondA.ToString());
        var suffixB = numbered.Single(r => r.Id == secondB.ToString());
        Assert.Equal(1, suffixA.EntranceNumber);
        Assert.Equal("A", suffixA.NumberSuffix);
        Assert.Equal("1/A", suffixA.Label);
        Assert.Equal(1, suffixB.EntranceNumber);
        Assert.Equal("B", suffixB.NumberSuffix);
        Assert.Equal("1/B", suffixB.Label);

        using var stored = await GetDataAsync(secondA);
        Assert.Equal("cite", stored.RootElement.GetProperty("numberingMode").GetString());
        Assert.Equal(featureId.ToString(), stored.RootElement.GetProperty("numberingFeatureId").GetString());
    }

    [Fact]
    public async Task NumberAsync_Roundabout_FromNorth_EvenOutsideOddInside()
    {
        var featureId = await AddCityCenterAsync(
            FeatureTypes.CityCenterLayers.Roundabout, 36.0, 3.0, 500);

        var insideNorth = await AddEntranceAsync(36.002, 3.0);
        var insideWest = await AddEntranceAsync(36.0, 2.998);
        var outsideNorth = await AddEntranceAsync(36.0046, 3.0);
        var outsideSouth = await AddEntranceAsync(35.9954, 3.0);

        var numbered = await CreateService().NumberAsync(
            _userId, FeatureTypes.SpecialNumberingModes.Roundabout, featureId,
            [outsideSouth, insideWest, insideNorth, outsideNorth], null, CancellationToken.None);

        Assert.NotNull(numbered);
        Assert.Equal(1, numbered.Single(r => r.Id == insideNorth.ToString()).EntranceNumber);
        Assert.Equal(3, numbered.Single(r => r.Id == insideWest.ToString()).EntranceNumber);
        Assert.Equal(2, numbered.Single(r => r.Id == outsideNorth.ToString()).EntranceNumber);
        Assert.Equal(4, numbered.Single(r => r.Id == outsideSouth.ToString()).EntranceNumber);
        Assert.Equal("inside", numbered.Single(r => r.Id == insideNorth.ToString()).Side);
        Assert.Equal("outside", numbered.Single(r => r.Id == outsideNorth.ToString()).Side);
    }

    [Fact]
    public async Task NumberAsync_Square_OutsideBandLimitsScope()
    {
        var ring = RingData(
            (35.99, 2.99), (35.99, 3.01), (36.01, 3.01), (36.01, 2.99), (35.99, 2.99));
        var featureId = await AddPublicSpaceAsync(FeatureTypes.PublicSpaceLayers.Square, ring);

        var insideNorth = await AddEntranceAsync(36.005, 3.0);
        var nearOutside = await AddEntranceAsync(35.9895, 3.0);
        var farOutside = await AddEntranceAsync(35.9, 3.0);

        var numbered = await CreateService().NumberAsync(
            _userId, FeatureTypes.SpecialNumberingModes.Square, featureId,
            [insideNorth, nearOutside, farOutside], null, CancellationToken.None);

        Assert.Null(numbered); // the far entrance is out of scope, so nothing is written
        using var unchanged = await GetDataAsync(insideNorth);
        Assert.False(unchanged.RootElement.TryGetProperty("entranceNumber", out _));

        var valid = await CreateService().NumberAsync(
            _userId, FeatureTypes.SpecialNumberingModes.Square, featureId,
            [insideNorth, nearOutside], null, CancellationToken.None);

        Assert.NotNull(valid);
        Assert.Equal(1, valid.Single(r => r.Id == insideNorth.ToString()).EntranceNumber);
        Assert.Equal(2, valid.Single(r => r.Id == nearOutside.ToString()).EntranceNumber);
    }

    [Fact]
    public async Task NumberAsync_WrongFeatureLayerForMode_ReturnsNull()
    {
        var ring = RingData(
            (35.99, 2.99), (35.99, 3.01), (36.01, 3.01), (36.01, 2.99), (35.99, 2.99));
        var featureId = await AddDistrictAsync(FeatureTypes.DistrictLayers.HousingEstate, ring);
        var entrance = await AddEntranceAsync(36.0, 3.0);

        var numbered = await CreateService().NumberAsync(
            _userId, FeatureTypes.SpecialNumberingModes.Cite, featureId,
            [entrance], (36.0, 3.0), CancellationToken.None);

        Assert.Null(numbered);
    }

    [Fact]
    public async Task NumberAsync_CiteWithoutStartPoint_ReturnsNull()
    {
        var ring = RingData(
            (35.99, 2.99), (35.99, 3.01), (36.01, 3.01), (36.01, 2.99), (35.99, 2.99));
        var featureId = await AddDistrictAsync(FeatureTypes.DistrictLayers.Cite, ring);
        var entrance = await AddEntranceAsync(36.0, 3.0);

        var numbered = await CreateService().NumberAsync(
            _userId, FeatureTypes.SpecialNumberingModes.Cite, featureId,
            [entrance], null, CancellationToken.None);

        Assert.Null(numbered);
    }
}
