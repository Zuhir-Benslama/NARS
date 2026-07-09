using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.Models;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests;

public class FeatureStatsServiceTests
{
    private static AppDbContext CreateDb() => CreateInMemoryDb("FeatureStatsTest");

    private static readonly Guid UserId1 = Guid.NewGuid();
    private static readonly Guid UserId2 = Guid.NewGuid();

    private static async Task<AppDbContext> SeedWithFeaturesAsync()
    {
        var db = CreateDb();

        db.Areas.Add(new Area { Id = Guid.CreateVersion7(), UserId = UserId1, Layer = "central_urban", Label = "A1", Data = "{}", CreatedAt = FixedUtcNow });
        db.Areas.Add(new Area { Id = Guid.CreateVersion7(), UserId = UserId1, Layer = "secondary_urban", Label = "A2", Data = "{}", CreatedAt = FixedUtcNow });
        db.Roads.Add(new Road { Id = Guid.CreateVersion7(), UserId = UserId1, Layer = "street", Label = "R1", Data = "{}", CreatedAt = FixedUtcNow });
        db.Areas.Add(new Area { Id = Guid.CreateVersion7(), UserId = UserId2, Layer = "central_urban", Label = "A3", Data = "{}", CreatedAt = FixedUtcNow });
        db.Districts.Add(new District { Id = Guid.CreateVersion7(), UserId = UserId2, Layer = "housing_estate", Label = "D1", Data = "{}", CreatedAt = FixedUtcNow });
        db.Districts.Add(new District { Id = Guid.CreateVersion7(), UserId = UserId2, Layer = "district", Label = "D2", Data = "{}", CreatedAt = FixedUtcNow });
        db.Users.Add(new User { Id = UserId1, Username = "user1", Name = "User 1", Email = "u1@test.com", Phone = DefaultPhone, PasswordHash = "hash", Role = "commune_user", CommuneId = 1 });
        db.Users.Add(new User { Id = UserId2, Username = "user2", Name = "User 2", Email = "u2@test.com", Phone = "0555000001", PasswordHash = "hash", Role = "commune_user", CommuneId = 1 });

        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task GetFeatureCountsAsync_ReturnsCorrectCounts()
    {
        var db = await SeedWithFeaturesAsync();
        var svc = new FeatureStatsService(db);

        var counts = await svc.GetFeatureCountsAsync(UserId1);

        Assert.Equal(2, counts[FeatureTypes.Area]);
        Assert.Equal(1, counts[FeatureTypes.Road]);
        Assert.Equal(0, counts[FeatureTypes.District]);
        Assert.Equal(0, counts[FeatureTypes.CityCenter]);
        Assert.Equal(0, counts[FeatureTypes.HouseEntrance]);
        Assert.Equal(0, counts[FeatureTypes.PublicBuilding]);
        Assert.Equal(0, counts[FeatureTypes.PublicSpace]);
        Assert.Equal(0, counts[FeatureTypes.NamingPanel]);
    }

    [Fact]
    public async Task GetFeatureCountsAsync_UnknownUser_ReturnsAllZeros()
    {
        var db = await SeedWithFeaturesAsync();
        var svc = new FeatureStatsService(db);

        var counts = await svc.GetFeatureCountsAsync(Guid.NewGuid());

        Assert.All(counts.Values, v => Assert.Equal(0, v));
    }

    [Fact]
    public async Task GetFeatureCountsAsync_EmptyDb_ReturnsAllZeros()
    {
        var db = CreateDb();
        var svc = new FeatureStatsService(db);

        var counts = await svc.GetFeatureCountsAsync(UserId1);

        Assert.All(counts.Values, v => Assert.Equal(0, v));
    }

    [Fact]
    public async Task GetUserFeatureCountsAsync_ReturnsPerUserCounts()
    {
        var db = await SeedWithFeaturesAsync();
        var svc = new FeatureStatsService(db);

        var result = await svc.GetUserFeatureCountsAsync([UserId1, UserId2]);

        Assert.Equal(2, result.Count);

        var u1 = result[UserId1];
        Assert.Equal(2, u1.Areas);
        Assert.Equal(0, u1.Districts);
        Assert.Equal(1, u1.Roads);
        Assert.Equal(3, u1.Total);

        var u2 = result[UserId2];
        Assert.Equal(1, u2.Areas);
        Assert.Equal(2, u2.Districts);
        Assert.Equal(0, u2.Roads);
        Assert.Equal(3, u2.Total);
    }

    [Fact]
    public async Task GetUserFeatureCountsAsync_EmptyArray_ReturnsEmpty()
    {
        var db = CreateDb();
        var svc = new FeatureStatsService(db);

        var result = await svc.GetUserFeatureCountsAsync([]);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetUserFeatureCountsAsync_UserWithNoFeatures_ReturnsZeros()
    {
        var db = await SeedWithFeaturesAsync();
        var unknownId = Guid.NewGuid();
        db.Users.Add(new User { Id = unknownId, Username = "empty", Name = "Empty", Email = "empty@test.com", Phone = DefaultPhone, PasswordHash = "hash", Role = "commune_user", CommuneId = 1 });
        await db.SaveChangesAsync();
        var svc = new FeatureStatsService(db);

        var result = await svc.GetUserFeatureCountsAsync([unknownId]);

        var stats = result[unknownId];
        Assert.Equal(0, stats.Areas);
        Assert.Equal(0, stats.Districts);
        Assert.Equal(0, stats.Roads);
        Assert.Equal(0, stats.Total);
    }
}
