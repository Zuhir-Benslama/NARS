using Microsoft.EntityFrameworkCore;
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
        IDateTimeProvider? timeProvider = null) => new(
            dbFactory ?? Mock.Of<IDbContextFactory<AppDbContext>>(),
            timeProvider ?? Mock.Of<IDateTimeProvider>(x => x.UtcNow == FixedUtcNow),
            Mock.Of<ILogger<ScatteredAreaService>>());

    [Fact]
    public async Task RefreshAsync_DbFailure_SetsLastError()
    {
        var factory = new Mock<IDbContextFactory<AppDbContext>>();
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated database failure"));

        var service = CreateService(dbFactory: factory.Object);
        var userId = Guid.NewGuid();

        var success = await service.RefreshAsync(userId, 1);

        Assert.False(success);
        var error = service.GetLastError(userId, 1);
        Assert.NotNull(error);
        Assert.Equal(FixedUtcNowOffset, error!.Value.Timestamp);
        Assert.NotEmpty(error.Value.Message);
    }

    [Fact]
    public async Task RefreshAsync_ConsecutiveErrors_UpdatesLastError()
    {
        var factory = new Mock<IDbContextFactory<AppDbContext>>();
        factory.SetupSequence(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated database failure #1"))
            .ThrowsAsync(new InvalidOperationException("Simulated database failure #2"));

        var service = CreateService(dbFactory: factory.Object);
        var userId = Guid.NewGuid();

        // First call fails for (user, commune 1).
        var firstSuccess = await service.RefreshAsync(userId, 1);
        Assert.False(firstSuccess);
        Assert.NotNull(service.GetLastError(userId, 1));

        // Second call fails for (user, commune 2) — each key tracks its own error.
        var secondSuccess = await service.RefreshAsync(userId, 2);
        Assert.False(secondSuccess);
        Assert.NotNull(service.GetLastError(userId, 2));
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
        Assert.All(writeResults, Assert.False);
        Assert.All(userIds, uid => Assert.NotNull(service.GetLastError(uid, 1)));
        // Another user's error must not appear under a different user's key.
        Assert.Null(service.GetLastError(Guid.NewGuid(), 1));
    }

    [Fact]
    public async Task LastError_ExceedingCap_EvictsOldestEntries()
    {
        var factory = new Mock<IDbContextFactory<AppDbContext>>();
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated database failure"));

        var now = FixedUtcNow;
        var ticking = new Mock<IDateTimeProvider>();
        ticking.Setup(t => t.UtcNow).Returns(() => now = now.AddSeconds(1));

        var service = CreateService(dbFactory: factory.Object, timeProvider: ticking.Object);

        // 1005 distinct failing keys exceeds the 1000-entry cap.
        const int keys = 1005;
        var ids = Enumerable.Range(0, keys).Select(_ => Guid.NewGuid()).ToArray();
        for (var i = 0; i < keys; i++)
        {
            Assert.False(await service.RefreshAsync(ids[i], 1));
        }

        // The newest entry survives; the very first (oldest) entries were evicted.
        Assert.NotNull(service.GetLastError(ids[^1], 1));
        Assert.Null(service.GetLastError(ids[0], 1));
        Assert.Null(service.GetLastError(ids[1], 1));
    }
}
