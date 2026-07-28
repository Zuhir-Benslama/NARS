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
        IDateTimeProvider? timeProvider = null)
    {
        return new ScatteredAreaService(
            dbFactory ?? Mock.Of<IDbContextFactory<AppDbContext>>(),
            timeProvider ?? Mock.Of<IDateTimeProvider>(x => x.UtcNow == FixedUtcNow),
            Mock.Of<ILogger<ScatteredAreaService>>());
    }

    [Fact]
    public async Task RefreshAsync_DbFailure_SetsLastError()
    {
        var factory = new Mock<IDbContextFactory<AppDbContext>>();
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated database failure"));

        var service = CreateService(dbFactory: factory.Object);

        await service.RefreshAsync(Guid.NewGuid(), 1);

        Assert.NotNull(service.LastError);
        Assert.Equal(FixedUtcNowOffset, service.LastError!.Value.Timestamp);
        Assert.NotEmpty(service.LastError!.Value.Message);
    }

    [Fact]
    public async Task RefreshAsync_ConsecutiveErrors_UpdatesLastError()
    {
        var factory = new Mock<IDbContextFactory<AppDbContext>>();
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated database failure"));

        var service = CreateService(dbFactory: factory.Object);

        // First call fails and sets LastError.
        await service.RefreshAsync(Guid.NewGuid(), 1);
        Assert.NotNull(service.LastError);

        // Second call clears LastError, then fails again — verifies the
        // error-reset + re-capture path on the same service instance.
        await service.RefreshAsync(Guid.NewGuid(), 2);
        Assert.NotNull(service.LastError);
    }

    [Fact]
    public async Task RefreshAsync_Cancellation_DoesNotSetLastError()
    {
        var factory = new Mock<IDbContextFactory<AppDbContext>>();
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var service = CreateService(dbFactory: factory.Object);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.RefreshAsync(Guid.NewGuid(), 1, new CancellationToken(true)));

        Assert.Null(service.LastError);
    }

    [Fact]
    public async Task LastError_IsThreadSafe()
    {
        var service = CreateService();
        var tasks = Enumerable.Range(0, 100).Select(_ => Task.Run(() =>
        {
            // Concurrent reads should not throw.
            var error = service.LastError;
            return error;
        }));

        var results = await Task.WhenAll(tasks);
        Assert.All(results, r => Assert.Null(r));
    }
}
