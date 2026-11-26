using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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

    private async Task<(Guid RoadId, EntranceService EntranceService)> CreateRoadWithEntranceServiceAsync()
    {
        var coords = """{"coordinates":[{"lat":36.4,"lng":2.9},{"lat":36.4,"lng":3.1}]}""";
        var roadId = await TestData.AddRoadAsync(Db, _userId, coords, registerInFeatureRegistry: true);
        var entranceService = new EntranceService(Fixture.CreateDbContextFactory());
        return (roadId, entranceService);
    }

    private async Task<Guid> CreateEntranceAsync(EntranceService entranceService, Guid roadId, string side)
    {
        var data = JsonSerializer.Serialize(new { side });
        return await entranceService.CreateEntranceAsync(roadId, _userId, "Entrance", data);
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
        var (roadId, entranceService) = await CreateRoadWithEntranceServiceAsync();
        var left1 = await CreateEntranceAsync(entranceService, roadId, "left");
        var left2 = await CreateEntranceAsync(entranceService, roadId, "left");
        var right1 = await CreateEntranceAsync(entranceService, roadId, "right");
        var right2 = await CreateEntranceAsync(entranceService, roadId, "right");

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
        var (roadId, entranceService) = await CreateRoadWithEntranceServiceAsync();

        // An already-numbered left entrance at 1 (dense start).
        await entranceService.CreateEntranceAsync(
            roadId, _userId, "Existing",
            JsonSerializer.Serialize(new { side = "left", entranceNumber = 1 }));

        var newLeft = await CreateEntranceAsync(entranceService, roadId, "left");

        var numbered = await CreateService().NumberAsync(
            _userId, roadId, [newLeft], CancellationToken.None);

        Assert.NotNull(numbered);
        Assert.Single(numbered);
        // 1 is taken by the existing entrance -> next free odd is 3.
        Assert.Equal(3, numbered[0].EntranceNumber);
    }

    [Fact]
    public async Task NumberAsync_NullIslandEntrance_DoesNotReserveNumber()
    {
        var (roadId, entranceService) = await CreateRoadWithEntranceServiceAsync();

        // A phantom entrance at (0,0) holds #2 in its stored data. It is a
        // placeholder, not a real doorway, so it must not push the first real
        // right-side entrance to #4.
        await entranceService.CreateEntranceAsync(
            roadId, _userId, "Ghost",
            JsonSerializer.Serialize(new
            {
                lat = 0.0,
                lng = 0.0,
                coordinates = new[] { new { lat = 0.0, lng = 0.0 } },
                side = "right",
                entranceNumber = 2,
            }));

        // The test road runs east along lat 36.4; a slightly lower lat is right.
        var realRight = await entranceService.CreateEntranceAsync(
            roadId, _userId, "Real",
            JsonSerializer.Serialize(new { lat = 36.399, lng = 3.0, side = "right" }));

        var numbered = await CreateService().NumberAsync(
            _userId, roadId, [realRight], CancellationToken.None);

        Assert.NotNull(numbered);
        Assert.Single(numbered);
        Assert.Equal("right", numbered[0].Side);
        Assert.Equal(2, numbered[0].EntranceNumber);
    }

    [Fact]
    public async Task NumberAsync_ConcurrentBatchesOnSameRoad_ProduceDisjointNumbers()
    {
        var (roadId, entranceService) = await CreateRoadWithEntranceServiceAsync();
        var left1 = await CreateEntranceAsync(entranceService, roadId, "left");
        var left2 = await CreateEntranceAsync(entranceService, roadId, "left");
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

    private async Task<Guid> CreateEntranceAtAsync(
        EntranceService entranceService, Guid roadId, string? storedSide, double lat, double lng)
    {
        var data = storedSide is null
            ? JsonSerializer.Serialize(new { lat, lng })
            : JsonSerializer.Serialize(new { side = storedSide, lat, lng });
        return await entranceService.CreateEntranceAsync(roadId, _userId, "Entrance", data);
    }

    private async Task<string?> GetEntranceSideAsync(Guid entranceId)
    {
        var data = await GetEntranceDataJsonAsync(entranceId);
        if (data is null)
        {
            return null;
        }
        using var doc = JsonDocument.Parse(data);
        return doc.RootElement.TryGetProperty("side", out var s) ? s.GetString() : null;
    }

    [Fact]
    public async Task NumberAsync_StaleStoredSide_IsRecomputedFromPosition()
    {
        // The road runs east along lat 36.4, so north of it (36.401) is the left
        // and south of it (36.399) is the right, whatever side was stored before
        // (e.g. before the road was reversed or the marker was moved).
        var (roadId, entranceService) = await CreateRoadWithEntranceServiceAsync();
        var north = await CreateEntranceAtAsync(entranceService, roadId, "right", 36.401, 3.0);
        var south = await CreateEntranceAtAsync(entranceService, roadId, "left", 36.399, 3.0);

        var numbered = await CreateService().NumberAsync(
            _userId, roadId, [north, south], CancellationToken.None);

        Assert.NotNull(numbered);
        Assert.Equal("left", numbered[0].Side);
        Assert.Equal(1, numbered[0].EntranceNumber);
        Assert.Equal("right", numbered[1].Side);
        Assert.Equal(2, numbered[1].EntranceNumber);

        // The corrected side is persisted together with the number.
        Assert.Equal("left", await GetEntranceSideAsync(north));
        Assert.Equal("right", await GetEntranceSideAsync(south));
    }

    [Fact]
    public async Task NumberAsync_MissingStoredSide_IsDerivedFromPosition()
    {
        var (roadId, entranceService) = await CreateRoadWithEntranceServiceAsync();
        var south = await CreateEntranceAtAsync(entranceService, roadId, null, 36.399, 3.0);

        var numbered = await CreateService().NumberAsync(
            _userId, roadId, [south], CancellationToken.None);

        Assert.NotNull(numbered);
        Assert.Equal("right", numbered[0].Side);
        Assert.Equal(2, numbered[0].EntranceNumber);
    }

    [Fact]
    public async Task NumberAsync_FaithRoadBend_RecomputesStaleSideFromGeometry()
    {
        // Real "Faith" geometry: the nearest midpoint of this corner entrance
        // belongs to segment 0, but its closest point is on segment 1's left.
        const string faithCoords = """
            {"coordinates":[
              {"lat":36.00989774319092,"lng":7.421284662059691},
              {"lat":36.009630858381286,"lng":7.421949866808546},
              {"lat":36.00763442086685,"lng":7.423032007407272}
            ]}
            """;
        var roadId = await TestData.AddRoadAsync(Db, _userId, faithCoords, registerInFeatureRegistry: true);
        var entranceId = await new EntranceService(Fixture.CreateDbContextFactory()).CreateEntranceAsync(
            roadId, _userId, "Corner entrance",
            JsonSerializer.Serialize(new
            {
                lat = 36.009574647202214,
                lng = 7.422057531298776,
                side = "right",
            }));

        var numbered = await CreateService().NumberAsync(_userId, roadId, [entranceId], CancellationToken.None);

        Assert.NotNull(numbered);
        Assert.Equal("left", numbered[0].Side);
        Assert.Equal(1, numbered[0].EntranceNumber);
        Assert.Equal("left", await GetEntranceSideAsync(entranceId));
    }

    [Fact]
    public async Task NumberAsync_NullIslandCoordinates_FallBackToStoredSide()
    {
        var (roadId, entranceService) = await CreateRoadWithEntranceServiceAsync();
        var entranceId = await entranceService.CreateEntranceAsync(
            roadId, _userId, "Unknown position",
            JsonSerializer.Serialize(new { lat = 0.0, lng = 0.0, side = "right" }));

        var numbered = await CreateService().NumberAsync(_userId, roadId, [entranceId], CancellationToken.None);

        Assert.NotNull(numbered);
        Assert.Equal("right", numbered[0].Side);
        Assert.Equal(2, numbered[0].EntranceNumber);
    }

    [Fact]
    public async Task NumberAsync_RoadNotOwnedOrMissing_ReturnsNull()
    {
        var result = await CreateService().NumberAsync(
            _userId, Guid.NewGuid(), [Guid.NewGuid()], CancellationToken.None);

        Assert.Null(result);
    }
}
