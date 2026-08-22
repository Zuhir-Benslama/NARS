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
        Assert.All(writeResults, Assert.False);
        Assert.All(userIds, uid => Assert.NotNull(service.GetLastError(uid, 1)));
        // Another user's error must not appear under a different user's key.
        Assert.Null(service.GetLastError(Guid.NewGuid(), 1));
    }
}
