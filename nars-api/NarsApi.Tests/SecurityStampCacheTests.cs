using Microsoft.Extensions.Caching.Memory;
using NarsApi.Infrastructure;
using NarsApi.Services;
using Xunit;
using static NarsApi.Tests.TestData;
using static NarsApi.Tests.SeedData;

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

    [Fact]
    public async Task GetStampWithDbFallbackAsync_NoCacheEntry_LoadsFromDbAndPopulatesCache()
    {
        var cache = Create();
        await using var db = CreateInMemoryDb("StampCacheFallback");
        var user = await CreateUserAsync(db, UserRoles.CommuneUser, securityStamp: "stamp-db");

        var stamp = await cache.GetStampWithDbFallbackAsync(db, user.Id);

        Assert.Equal("stamp-db", stamp);
        Assert.Equal("stamp-db", await cache.GetStampAsync(user.Id));
    }

    [Fact]
    public async Task GetStampWithDbFallbackAsync_CachedValue_ShortCircuitsDbQuery()
    {
        var cache = Create();
        await using var db = CreateInMemoryDb("StampCacheFallbackHit");
        // Empty DB — a cached value proves the database query was skipped.
        var userId = Guid.NewGuid();
        cache.SetStamp(userId, "stamp-cached");

        var stamp = await cache.GetStampWithDbFallbackAsync(db, userId);

        Assert.Equal("stamp-cached", stamp);
    }

    [Fact]
    public async Task GetStampWithDbFallbackAsync_MissingUser_ReturnsNull()
    {
        var cache = Create();
        await using var db = CreateInMemoryDb("StampCacheFallbackMissing");

        var stamp = await cache.GetStampWithDbFallbackAsync(db, Guid.NewGuid());

        Assert.Null(stamp);
    }
}
