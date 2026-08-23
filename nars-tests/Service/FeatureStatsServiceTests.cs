using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using Xunit;
using System.Text.Json;

namespace NarsApi.Tests.Service;

[Collection(PostgreSqlCollection.CollectionName)]
[Trait("Category", "Service")]
public class FeatureStatsServiceTests(NarsDatabaseFixture fixture) : IAsyncLifetime
{
    private readonly NarsDatabaseFixture _fixture = fixture;
    private AppDbContext _db = null!;
    private Guid _userId1;
    private Guid _userId2;

    public async Task InitializeAsync()
    {
        _db = _fixture.CreateDbContext();
        await SeedData.SeedBasicLocationsAsync(_db);
        _userId1 = Guid.NewGuid();
        _userId2 = Guid.NewGuid();

        _db.Areas.Add(new Area { Id = Guid.CreateVersion7(), UserId = _userId1, Layer = FeatureTypes.AreaLayers.CentralUrban, Label = "A1", Data = "{}", CreatedAt = FixedUtcNow });
        _db.Areas.Add(new Area { Id = Guid.CreateVersion7(), UserId = _userId1, Layer = FeatureTypes.AreaLayers.SecondaryUrban, Label = "A2", Data = "{}", CreatedAt = FixedUtcNow });
        _db.Roads.Add(new Road { Id = Guid.CreateVersion7(), UserId = _userId1, Layer = FeatureTypes.RoadLayers.Street, Label = "R1", Data = "{}", CreatedAt = FixedUtcNow });
        _db.Areas.Add(new Area { Id = Guid.CreateVersion7(), UserId = _userId2, Layer = FeatureTypes.AreaLayers.CentralUrban, Label = "A3", Data = "{}", CreatedAt = FixedUtcNow });
        _db.Districts.Add(new District { Id = Guid.CreateVersion7(), UserId = _userId2, Layer = FeatureTypes.DistrictLayers.HousingEstate, Label = "D1", Data = "{}", CreatedAt = FixedUtcNow });
        _db.Districts.Add(new District { Id = Guid.CreateVersion7(), UserId = _userId2, Layer = FeatureTypes.DistrictLayers.DistrictLayer, Label = "D2", Data = "{}", CreatedAt = FixedUtcNow });
        _db.Users.Add(new User { Id = _userId1, Username = "user1", Name = "User 1", Email = "u1@test.com", Phone = DefaultPhone, PasswordHash = "hash", Role = UserRoles.CommuneUser, CommuneId = 1, SecurityStamp = User.GenerateSecurityStamp() });
        _db.Users.Add(new User { Id = _userId2, Username = "user2", Name = "User 2", Email = "u2@test.com", Phone = AltPhone, PasswordHash = "hash", Role = UserRoles.CommuneUser, CommuneId = 1, SecurityStamp = User.GenerateSecurityStamp() });
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
        _db.Users.Add(new User { Id = unknownId, Username = "empty", Name = "Empty", Email = "empty@test.com", Phone = DefaultPhone, PasswordHash = "hash", Role = UserRoles.CommuneUser, CommuneId = 1, SecurityStamp = User.GenerateSecurityStamp() });
        await _db.SaveChangesAsync();
        var svc = new FeatureStatsService(_fixture.CreateDbContextFactory());

        var result = await svc.GetUserFeatureCountsAsync([unknownId]);

        var stats = result[unknownId];
        Assert.Equal(0, stats.Areas);
        Assert.Equal(0, stats.Districts);
        Assert.Equal(0, stats.Roads);
        Assert.Equal(0, stats.Total);
    }

    [Fact]
    public async Task LoadAllFeaturesAsync_CorruptFeatureData_ReturnsEmptyObjectAndLogsWarning()
    {
        // Simulates out-of-band corruption reaching the read path: Postgres
        // imposes no nesting limit on jsonb, but System.Text.Json refuses to
        // parse beyond depth 64, so this row is unreadable by the API even
        // though it is perfectly valid jsonb. The query must degrade the bad
        // row to an empty object and log a warning naming it, without
        // dropping the healthy rows in the same page.
        const int depth = 100;
        var deepJson = new string('[', depth) + new string(']', depth);
        var corruptId = Guid.CreateVersion7();
        _db.Areas.Add(new Area { Id = corruptId, UserId = _userId1, Layer = FeatureTypes.AreaLayers.CentralUrban, Label = "CORRUPT", Data = deepJson, CreatedAt = FixedUtcNow });
        await _db.SaveChangesAsync();

        var loggerMock = new Mock<ILogger<FeatureStatsService>>();
        var svc = new FeatureStatsService(_fixture.CreateDbContextFactory(), loggerMock.Object);

        var (features, totalCount) = await svc.LoadAllFeaturesAsync(_userId1, skip: 0, take: 10);

        Assert.Equal(4, totalCount);
        Assert.Equal(4, features.Count);

        var corruptRow = features.Single(f => f.Id == corruptId.ToString());
        Assert.Equal(JsonValueKind.Object, corruptRow.Data.ValueKind);
        Assert.False(corruptRow.Data.EnumerateObject().Any());

        foreach (var healthy in features.Where(f => f.Id != corruptId.ToString()))
        {
            Assert.True(healthy.Data.EnumerateObject().Any() || healthy.Data.ValueKind == JsonValueKind.Object);
        }

        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains(corruptId.ToString())),
                It.Is<Exception>(ex => ex is JsonException),
                (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()),
            Times.Once);
    }
}
