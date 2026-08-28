using Microsoft.Extensions.Caching.Memory;
using NarsApi.Services;
using Xunit;

namespace NarsApi.Tests;

public sealed class SecurityStampCacheTests
{
    private static SecurityStampCache Create() => new(new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task GetStampAsync_NoEntry_ReturnsNull()
    {
        var cache = Create();
        Assert.Null(await cache.GetStampAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task SetThenGet_RoundTripsValue()
    {
        var cache = Create();
        var userId = Guid.NewGuid();
        cache.SetStamp(userId, "stamp-abc");
        Assert.Equal("stamp-abc", await cache.GetStampAsync(userId));
    }

    [Fact]
    public async Task Set_OverwritesPreviousValue()
    {
        var cache = Create();
        var userId = Guid.NewGuid();
        cache.SetStamp(userId, "stamp-one");
        cache.SetStamp(userId, "stamp-two");
        Assert.Equal("stamp-two", await cache.GetStampAsync(userId));
    }

    [Fact]
    public async Task Evict_RemovesStoredValue()
    {
        var cache = Create();
        var userId = Guid.NewGuid();
        cache.SetStamp(userId, "stamp-abc");
        cache.EvictStamp(userId);
        Assert.Null(await cache.GetStampAsync(userId));
    }

    [Fact]
    public async Task Evict_UnknownId_IsNoOp()
    {
        var cache = Create();
        cache.EvictStamp(Guid.NewGuid());
        Assert.Null(await cache.GetStampAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task DistinctUsers_DoNotShareEntries()
    {
        var cache = Create();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        cache.SetStamp(first, "stamp-first");
        Assert.Equal("stamp-first", await cache.GetStampAsync(first));
        Assert.Null(await cache.GetStampAsync(second));
    }
}
