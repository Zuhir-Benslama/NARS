using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.Models;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests;

/// <summary>
/// Unit tests for the InMemory-friendly methods of <see cref="ValidationService"/>
/// (the raw-SQL geometry checks need PostgreSQL and stay in the service suite).
/// </summary>
public class ValidationServiceTests
{
    private static ValidationService CreateService(AppDbContext db) => new(new TestDbContextFactory(db));

    private static Guid AddArea(AppDbContext db, string layer, Guid? userId = null)
    {
        var id = Guid.NewGuid();
        db.Areas.Add(new Area
        {
            Id = id,
            UserId = userId ?? Guid.NewGuid(),
            Layer = layer,
            Label = "Area",
            Data = "{}",
        });
        return id;
    }

    // ── UserHasCentralUrbanAreaAsync ─────────────────────────────────────────

    [Fact]
    public async Task UserHasCentralUrbanAreaAsync_TrueWhenCentralUrbanAreaExists()
    {
        using var db = CreateInMemoryDb("ValidationCentralUrban");
        var userId = Guid.NewGuid();
        db.Areas.Add(new Area { Id = Guid.NewGuid(), UserId = userId, Layer = FeatureTypes.AreaLayers.Scattered, Label = "S", Data = "{}" });
        AddArea(db, FeatureTypes.AreaLayers.CentralUrban, userId);
        await db.SaveChangesAsync();

        var svc = CreateService(db);

        Assert.True(await svc.UserHasCentralUrbanAreaAsync(userId));
    }

    [Fact]
    public async Task UserHasCentralUrbanAreaAsync_FalseWhenOnlyScatteredAreaExists()
    {
        using var db = CreateInMemoryDb("ValidationNoCentral");
        var userId = Guid.NewGuid();
        AddArea(db, FeatureTypes.AreaLayers.Scattered, userId);
        await db.SaveChangesAsync();

        var svc = CreateService(db);

        Assert.False(await svc.UserHasCentralUrbanAreaAsync(userId));
    }

    [Fact]
    public async Task UserHasCentralUrbanAreaAsync_FalseWhenAnotherUserHasCentralArea()
    {
        using var db = CreateInMemoryDb("ValidationOtherUser");
        AddArea(db, FeatureTypes.AreaLayers.CentralUrban);
        await db.SaveChangesAsync();

        var svc = CreateService(db);

        Assert.False(await svc.UserHasCentralUrbanAreaAsync(Guid.NewGuid()));
    }

    // ── CountUserRoadsAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task CountUserRoadsAsync_CountsOnlyTheUsersRoads()
    {
        using var db = CreateInMemoryDb("ValidationCountRoads");
        var userId = Guid.NewGuid();
        _ = await AddRoadAsync(db, userId, """{"type":"road"}""");
        _ = await AddRoadAsync(db, userId, """{"type":"road"}""");
        _ = await AddRoadAsync(db, Guid.NewGuid(), """{"type":"road"}""");

        var svc = CreateService(db);

        Assert.Equal(2, await svc.CountUserRoadsAsync(userId));
    }

    // ── CountUserDistrictsAsync ──────────────────────────────────────────────

    [Fact]
    public async Task CountUserDistrictsAsync_CountsDistricts()
    {
        using var db = CreateInMemoryDb("ValidationCountDistricts");
        var userId = Guid.NewGuid();
        db.Districts.AddRange(
            new District { Id = Guid.NewGuid(), UserId = userId, Layer = FeatureTypes.DistrictLayers.HousingEstate, Label = "D", Data = "{}" },
            new District { Id = Guid.NewGuid(), UserId = userId, Layer = FeatureTypes.DistrictLayers.UrbanPole, Label = "D", Data = "{}" },
            new District { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Layer = FeatureTypes.DistrictLayers.HousingEstate, Label = "D", Data = "{}" });
        await db.SaveChangesAsync();

        var svc = CreateService(db);

        Assert.Equal(2, await svc.CountUserDistrictsAsync(userId));
    }

    // ── CountUserUrbanAreasAsync ──────────────────────────────────────────────

    [Fact]
    public async Task CountUserUrbanAreasAsync_CountsOnlyUrbanLayers()
    {
        using var db = CreateInMemoryDb("ValidationCountUrban");
        var userId = Guid.NewGuid();
        AddArea(db, FeatureTypes.AreaLayers.CentralUrban, userId);
        AddArea(db, FeatureTypes.AreaLayers.SecondaryUrban, userId);
        AddArea(db, FeatureTypes.AreaLayers.Scattered, userId); // non-urban → excluded
        await db.SaveChangesAsync();

        var svc = CreateService(db);

        Assert.Equal(2, await svc.CountUserUrbanAreasAsync(userId));
    }
}
