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

        var success = await service.RefreshAsync(Guid.NewGuid(), 1);

        Assert.False(success);
        Assert.NotNull(service.LastError);
        Assert.Equal(FixedUtcNowOffset, service.LastError!.Value.Timestamp);
        Assert.NotEmpty(service.LastError!.Value.Message);
    }

    [Fact]
    public async Task RefreshAsync_ConsecutiveErrors_UpdatesLastError()
    {
        var factory = new Mock<IDbContextFactory<AppDbContext>>();
        factory.SetupSequence(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated database failure #1"))
            .ThrowsAsync(new InvalidOperationException("Simulated database failure #2"));

        var service = CreateService(dbFactory: factory.Object);

        // First call fails and sets LastError.
        var firstSuccess = await service.RefreshAsync(Guid.NewGuid(), 1);
        Assert.False(firstSuccess);
        Assert.NotNull(service.LastError);
        var firstMessage = service.LastError!.Value.Message;

        // Second call fails again — LastError must reflect the LATEST failure,
        // not a stale value captured by the first call.
        var secondSuccess = await service.RefreshAsync(Guid.NewGuid(), 2);
        Assert.False(secondSuccess);
        Assert.NotNull(service.LastError);
        Assert.Equal("Simulated database failure #2", service.LastError!.Value.Message);
        Assert.NotEqual(firstMessage, service.LastError!.Value.Message);
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
        factory.Verify(f => f.CreateDbContextAsync(
            It.Is<CancellationToken>(t => t.IsCancellationRequested)), Times.Once);
    }

    [Fact]
    public async Task LastError_IsThreadSafe()
    {
        var factory = new Mock<IDbContextFactory<AppDbContext>>();
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated database failure"));

        var service = CreateService(dbFactory: factory.Object);

        // Concurrent writers (failing RefreshAsync calls) interleaved with
        // concurrent readers must not throw or observe a torn LastError.
        var writers = Enumerable.Range(0, 50)
            .Select(_ => Task.Run(() => service.RefreshAsync(Guid.NewGuid(), 1)))
            .ToArray();
        var readErrors = new List<Exception>();
        var readValues = new List<string?>();
        var reads = Enumerable.Range(0, 50).Select(async _ =>
        {
            try
            {
                var error = service.LastError;
                lock (readValues)
                {
                    readValues.Add(error?.Message);
                }
            }
            catch (Exception ex)
            {
                lock (readErrors) { readErrors.Add(ex); }
            }
        });

        await Task.WhenAll(writers);
        await Task.WhenAll(reads);

        Assert.Empty(readErrors);
        Assert.All(writers, w => Assert.False(w.Result));
        Assert.All(readValues, v =>
            Assert.True(v is null || v == "Simulated database failure",
                $"Observed inconsistent LastError value: '{v}'"));
        Assert.Equal("Simulated database failure", service.LastError!.Value.Message);
    }
}
