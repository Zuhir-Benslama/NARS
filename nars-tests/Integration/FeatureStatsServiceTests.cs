using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests.Integration;

[Collection(PostgreSqlCollection.CollectionName)]
public class FeatureStatsServiceTests : IAsyncLifetime
{
    private readonly NarsDatabaseFixture _fixture;
    private AppDbContext _db = null!;
    private Guid _userId1;
    private Guid _userId2;

    public FeatureStatsServiceTests(NarsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _db = _fixture.CreateDbContext();
        _userId1 = Guid.NewGuid();
        _userId2 = Guid.NewGuid();

        _db.Areas.Add(new Area { Id = Guid.CreateVersion7(), UserId = _userId1, Layer = "central_urban", Label = "A1", Data = "{}", CreatedAt = FixedUtcNow });
        _db.Areas.Add(new Area { Id = Guid.CreateVersion7(), UserId = _userId1, Layer = "secondary_urban", Label = "A2", Data = "{}", CreatedAt = FixedUtcNow });
        _db.Roads.Add(new Road { Id = Guid.CreateVersion7(), UserId = _userId1, Layer = "street", Label = "R1", Data = "{}", CreatedAt = FixedUtcNow });
        _db.Areas.Add(new Area { Id = Guid.CreateVersion7(), UserId = _userId2, Layer = "central_urban", Label = "A3", Data = "{}", CreatedAt = FixedUtcNow });
        _db.Districts.Add(new District { Id = Guid.CreateVersion7(), UserId = _userId2, Layer = "housing_estate", Label = "D1", Data = "{}", CreatedAt = FixedUtcNow });
        _db.Districts.Add(new District { Id = Guid.CreateVersion7(), UserId = _userId2, Layer = "district", Label = "D2", Data = "{}", CreatedAt = FixedUtcNow });
        _db.Users.Add(new User { Id = _userId1, Username = "user1", Name = "User 1", Email = "u1@test.com", Phone = DefaultPhone, PasswordHash = "hash", Role = UserRoles.CommuneUser, CommuneId = 1 });
        _db.Users.Add(new User { Id = _userId2, Username = "user2", Name = "User 2", Email = "u2@test.com", Phone = AltPhone, PasswordHash = "hash", Role = UserRoles.CommuneUser, CommuneId = 1 });
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        try { await _db.DisposeAsync(); }
        finally { await _fixture.CleanTablesAsync(); }
    }

    [Fact]
    public async Task GetFeatureCountsAsync_ReturnsCorrectCounts()
    {
        var svc = new FeatureStatsService(_fixture.CreateDbContextFactory());

        var counts = await svc.GetFeatureCountsAsync(_userId1);

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
        var svc = new FeatureStatsService(_fixture.CreateDbContextFactory());

        var counts = await svc.GetFeatureCountsAsync(Guid.NewGuid());

        Assert.All(counts.Values, v => Assert.Equal(0, v));
    }

    [Fact]
    public async Task GetUserFeatureCountsAsync_ReturnsPerUserCounts()
    {
        var svc = new FeatureStatsService(_fixture.CreateDbContextFactory());

        var result = await svc.GetUserFeatureCountsAsync([_userId1, _userId2]);

        Assert.Equal(2, result.Count);

        var u1 = result[_userId1];
        Assert.Equal(2, u1.Areas);
        Assert.Equal(0, u1.Districts);
        Assert.Equal(1, u1.Roads);
        Assert.Equal(3, u1.Total);

        var u2 = result[_userId2];
        Assert.Equal(1, u2.Areas);
        Assert.Equal(2, u2.Districts);
        Assert.Equal(0, u2.Roads);
        Assert.Equal(3, u2.Total);
    }

    [Fact]
    public async Task GetUserFeatureCountsAsync_EmptyArray_ReturnsEmpty()
    {
        var svc = new FeatureStatsService(_fixture.CreateDbContextFactory());

        var result = await svc.GetUserFeatureCountsAsync([]);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetUserFeatureCountsAsync_UserWithNoFeatures_ReturnsZeros()
    {
        var unknownId = Guid.NewGuid();
        _db.Users.Add(new User { Id = unknownId, Username = "empty", Name = "Empty", Email = "empty@test.com", Phone = DefaultPhone, PasswordHash = "hash", Role = UserRoles.CommuneUser, CommuneId = 1 });
        await _db.SaveChangesAsync();
        var svc = new FeatureStatsService(_fixture.CreateDbContextFactory());

        var result = await svc.GetUserFeatureCountsAsync([unknownId]);

        var stats = result[unknownId];
        Assert.Equal(0, stats.Areas);
        Assert.Equal(0, stats.Districts);
        Assert.Equal(0, stats.Roads);
        Assert.Equal(0, stats.Total);
    }
}
