using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests;

public class ScatteredAreaServiceTests
{
    private static ScatteredAreaService CreateService(
        IDbContextFactory<AppDbContext>? dbFactory = null,
        IDateTimeProvider? timeProvider = null,
        IMemoryCache? cache = null) => new(
            dbFactory ?? Mock.Of<IDbContextFactory<AppDbContext>>(),
            timeProvider ?? Mock.Of<IDateTimeProvider>(x => x.UtcNow == FixedUtcNow),
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            Mock.Of<ILogger<ScatteredAreaService>>());

    [Fact]
    public async Task RefreshAsync_DbFailure_SetsLastError()
    {
        var factory = new Mock<IDbContextFactory<AppDbContext>>();
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated database failure"));

        var service = CreateService(dbFactory: factory.Object);
        var userId = Guid.NewGuid();

        var geojson = await service.RefreshAsync(userId, 1);

        Assert.Null(geojson);
        var error = service.GetLastError(userId, 1);
        Assert.NotNull(error);
        Assert.Equal(FixedUtcNowOffset, error!.Value.Timestamp);
        Assert.NotEmpty(error.Value.Message);
    }

    [Fact]
    public async Task RefreshAsync_ConsecutiveErrors_UpdatesLastError()
    {
        var factory = new Mock<IDbContextFactory<AppDbContext>>();
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated database failure"));

        // The stored message is generic and the payload identical across
        // failures, so replacement is observable only through the timestamp:
        // an implementation that kept the stale entry would retain T0.
        var now = FixedUtcNow;
        var timeProvider = new Mock<IDateTimeProvider>();
        timeProvider.SetupGet(x => x.UtcNow).Returns(() => now);
        var service = CreateService(dbFactory: factory.Object, timeProvider: timeProvider.Object);
        var userId = Guid.NewGuid();

        await service.RefreshAsync(userId, 1);

        now = FixedUtcNow.AddMinutes(5);
        await service.RefreshAsync(userId, 1);

        var error = service.GetLastError(userId, 1);
        Assert.NotNull(error);
        Assert.Equal(FixedUtcNow.AddMinutes(5), error!.Value.Timestamp);
    }

    [Fact]
    public async Task RefreshAsync_Cancellation_DoesNotSetLastError()
    {
        var factory = new Mock<IDbContextFactory<AppDbContext>>();
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var service = CreateService(dbFactory: factory.Object);
        var userId = Guid.NewGuid();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.RefreshAsync(userId, 1, new CancellationToken(true)));

        Assert.Null(service.GetLastError(userId, 1));
        factory.Verify(f => f.CreateDbContextAsync(
            It.Is<CancellationToken>(t => t.IsCancellationRequested)), Times.Once);
    }

    [Fact]
    public async Task LastError_IsThreadSafe_AndKeyedPerUser()
    {
        var factory = new Mock<IDbContextFactory<AppDbContext>>();
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated database failure"));

        var service = CreateService(dbFactory: factory.Object);

        // Concurrent writers (failing RefreshAsync calls) interleaved with
        // concurrent readers must not throw or observe a torn error state, and
        // each user's error must be isolated to that user's key.
        var userIds = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToArray();
        var writers = userIds
            .Select(uid => Task.Run(() => service.RefreshAsync(uid, 1)))
            .ToArray();
        var readErrors = new List<Exception>();
        var reads = userIds.Select(async uid =>
        {
            try
            {
                service.GetLastError(uid, 1);
            }
            catch (Exception ex)
            {
                lock (readErrors) { readErrors.Add(ex); }
            }
        });

        var writeResults = await Task.WhenAll(writers);
        await Task.WhenAll(reads);

        Assert.Empty(readErrors);
        Assert.All(writeResults, Assert.Null);
        Assert.All(userIds, uid => Assert.NotNull(service.GetLastError(uid, 1)));
        // Another user's error must not appear under a different user's key.
        Assert.Null(service.GetLastError(Guid.NewGuid(), 1));
    }

    // ── ExtractAllRings (geometry ring extraction) ────────────────────────────

    [Fact]
    public void ExtractAllRings_Polygon_ReturnsOuterAndInteriorRings()
    {
        var geo = ToJsonElement(
            """{"type":"Polygon","coordinates":[[[0,0],[1,0],[1,1],[0,0]],[[0.2,0.2],[0.8,0.2],[0.5,0.5],[0.2,0.2]]]}""");

        var rings = ScatteredAreaService.ExtractAllRings(geo);

        Assert.Equal(2, rings.Count);
        AssertRing(rings[0], 4, lng: 0.0, lat: 0.0);
        AssertRing(rings[1], 4, lng: 0.2, lat: 0.2);
    }

    [Fact]
    public void ExtractAllRings_MultiPolygon_ReturnsAllPolygonRings()
    {
        var geo = ToJsonElement(
            """{"type":"MultiPolygon","coordinates":[[[[0,0],[1,0],[1,1],[0,0]]],[[[2,2],[3,2],[3,3],[2,2]]]]}""");

        var rings = ScatteredAreaService.ExtractAllRings(geo);

        Assert.Equal(2, rings.Count);
        AssertRing(rings[0], 4, lng: 0.0, lat: 0.0);
        AssertRing(rings[1], 4, lng: 2.0, lat: 2.0);
    }

    [Fact]
    public void ExtractAllRings_UnknownGeometryType_ReturnsEmpty()
    {
        var geo = ToJsonElement("""{"type":"LineString","coordinates":[[0,0],[1,1]]}""");

        var rings = ScatteredAreaService.ExtractAllRings(geo);

        Assert.Empty(rings);
    }

    [Fact]
    public void ExtractAllRings_MissingType_ReturnsEmpty()
    {
        var geo = ToJsonElement("""{"coordinates":[[[0,0],[1,0]]]}""");

        var rings = ScatteredAreaService.ExtractAllRings(geo);

        Assert.Empty(rings);
    }

    [Fact]
    public void ExtractAllRings_PointsWithFewerThanTwoCoordinates_AreSkipped()
    {
        var geo = ToJsonElement(
            """{"type":"Polygon","coordinates":[[[0,0],[1,0],[1],[1,0],[0,0]]]}""");

        var rings = ScatteredAreaService.ExtractAllRings(geo);

        var ring = Assert.Single(rings);
        Assert.Equal(4, ring.Count); // the degenerate [1] point is dropped
    }

    private static void AssertRing(List<object> ring, int expectedCount, double lng, double lat)
    {
        Assert.Equal(expectedCount, ring.Count);
        var serialized = System.Text.Json.JsonSerializer.Serialize(ring[0]);
        Assert.Contains($"\"lng\":{lng}", serialized);
        Assert.Contains($"\"lat\":{lat}", serialized);
    }
}
