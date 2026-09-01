using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests.Service;

[Collection(PostgreSqlCollection.CollectionName)]
[Trait("Category", "Service")]
public class NumberEntrancesServiceTests(NarsDatabaseFixture fixture) : ServiceTestBase(fixture)
{
    private Guid _userId;

    protected override async Task SeedAsync()
    {
        await SeedData.SeedBasicLocationsAsync(Db);
        var user = await SeedData.CreateUserAsync(Db, UserRoles.FieldWorker, communeId: 1, name: "Numbering Test User");
        _userId = user.Id;
    }

    private NumberEntrancesService CreateService() =>
        new(Fixture.CreateDbContextFactory());

    private async Task<(Guid RoadId, FieldService FieldService)> CreateRoadWithFieldServiceAsync()
    {
        var coords = """{"coordinates":[{"lat":36.4,"lng":2.9},{"lat":36.4,"lng":3.1}]}""";
        var roadId = await TestData.AddRoadAsync(Db, _userId, coords, registerInFeatureRegistry: true);
        var fieldService = new FieldService(
            Fixture.CreateDbContextFactory(),
            Mock.Of<IFeatureService>(),
            Mock.Of<ILogger<FieldService>>());
        return (roadId, fieldService);
    }

    private async Task<Guid> CreateEntranceAsync(FieldService fieldService, Guid roadId, string side)
    {
        var data = JsonSerializer.Serialize(new { side });
        return await fieldService.CreateEntranceAsync(roadId, _userId, _userId, "Entrance", data);
    }

    private async Task<string?> GetEntranceDataJsonAsync(Guid entranceId)
    {
        var row = await Db.HouseEntrances.AsNoTracking().FirstOrDefaultAsync(e => e.Id == entranceId);
        return row?.Data;
    }

    /// <summary>
    /// Extracts the entranceNumber stored in a feature's JSONB data column.
    /// </summary>
    private async Task<int?> GetEntranceNumberAsync(Guid entranceId)
    {
        var data = await GetEntranceDataJsonAsync(entranceId);
        if (data is null)
        {
            return null;
        }
        using var doc = JsonDocument.Parse(data);
        return doc.RootElement.TryGetProperty("entranceNumber", out var n) ? n.GetInt32() : null;
    }

    [Fact]
    public async Task NumberAsync_DenseSequenceInRequestOrder_AssignsOddLeftEvenRight()
    {
        var (roadId, fieldService) = await CreateRoadWithFieldServiceAsync();
        var left1 = await CreateEntranceAsync(fieldService, roadId, "left");
        var left2 = await CreateEntranceAsync(fieldService, roadId, "left");
        var right1 = await CreateEntranceAsync(fieldService, roadId, "right");
        var right2 = await CreateEntranceAsync(fieldService, roadId, "right");

        var numbered = await CreateService().NumberAsync(
            _userId, roadId, [left1, left2, right1, right2], CancellationToken.None);

        Assert.NotNull(numbered);
        Assert.Equal(4, numbered.Count);

        Assert.Equal(left1.ToString(), numbered[0].Id);
        Assert.Equal("left", numbered[0].Side);
        Assert.Equal(1, numbered[0].EntranceNumber);
        Assert.Equal("1", numbered[0].Label);

        Assert.Equal(left2.ToString(), numbered[1].Id);
        Assert.Equal("left", numbered[1].Side);
        Assert.Equal(3, numbered[1].EntranceNumber);

        Assert.Equal(right1.ToString(), numbered[2].Id);
        Assert.Equal("right", numbered[2].Side);
        Assert.Equal(2, numbered[2].EntranceNumber);

        Assert.Equal(right2.ToString(), numbered[3].Id);
        Assert.Equal("right", numbered[3].Side);
        Assert.Equal(4, numbered[3].EntranceNumber);

        // Persisted to the data column (parse JSONB, not raw substring).
        Assert.Equal(1, await GetEntranceNumberAsync(left1));
        Assert.Equal(3, await GetEntranceNumberAsync(left2));
        Assert.Equal(2, await GetEntranceNumberAsync(right1));
        Assert.Equal(4, await GetEntranceNumberAsync(right2));
    }

    [Fact]
    public async Task NumberAsync_ExistingNumberedEntrances_SeedsUsedSet()
    {
        var (roadId, fieldService) = await CreateRoadWithFieldServiceAsync();

        // An already-numbered left entrance at 1 (dense start).
        await fieldService.CreateEntranceAsync(
            roadId, _userId, _userId, "Existing",
            JsonSerializer.Serialize(new { side = "left", entranceNumber = 1 }));

        var newLeft = await CreateEntranceAsync(fieldService, roadId, "left");

        var numbered = await CreateService().NumberAsync(
            _userId, roadId, [newLeft], CancellationToken.None);

        Assert.NotNull(numbered);
        Assert.Single(numbered);
        // 1 is taken by the existing entrance -> next free odd is 3.
        Assert.Equal(3, numbered[0].EntranceNumber);
    }

    [Fact]
    public async Task NumberAsync_ConcurrentBatchesOnSameRoad_ProduceDisjointNumbers()
    {
        var (roadId, fieldService) = await CreateRoadWithFieldServiceAsync();
        var left1 = await CreateEntranceAsync(fieldService, roadId, "left");
        var left2 = await CreateEntranceAsync(fieldService, roadId, "left");
        var service = CreateService();

        var t1 = service.NumberAsync(_userId, roadId, [left1], CancellationToken.None);
        var t2 = service.NumberAsync(_userId, roadId, [left2], CancellationToken.None);

        var r1 = await t1;
        var r2 = await t2;

        Assert.NotNull(r1);
        Assert.NotNull(r2);
        var numbers = new[] { r1[0].EntranceNumber, r2[0].EntranceNumber };
        Assert.Equal(2, numbers.Distinct().Count()); // no collisions
        Assert.Contains(1, numbers);
        Assert.Contains(3, numbers);
    }

    [Fact]
    public async Task NumberAsync_RoadNotOwnedOrMissing_ReturnsNull()
    {
        var result = await CreateService().NumberAsync(
            _userId, Guid.NewGuid(), [Guid.NewGuid()], CancellationToken.None);

        Assert.Null(result);
    }
}
