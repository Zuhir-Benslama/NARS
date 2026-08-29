using Microsoft.Extensions.Caching.Memory;

namespace NarsApi.Services;

/// <summary>
/// In-memory cache for security stamps. Avoids a DB round-trip on every
/// authenticated request while still invalidating immediately when a stamp
/// is rotated (lockout / password change). Uses a short TTL as a safety
/// net so a missed eviction never lingers longer than 30 seconds.
/// </summary>
public interface ISecurityStampCache
{
    Task<string?> GetStampAsync(Guid userId, CancellationToken ct = default);
    void SetStamp(Guid userId, string stamp);
    void EvictStamp(Guid userId);
}

public sealed class SecurityStampCache(IMemoryCache cache) : ISecurityStampCache
{
    private static readonly TimeSpan s_ttl = TimeSpan.FromSeconds(30);

    private static string CacheKey(Guid userId) => $"stamp:{userId}";

    public Task<string?> GetStampAsync(Guid userId, CancellationToken ct = default)
    {
        cache.TryGetValue<string>(CacheKey(userId), out var stamp);
        return Task.FromResult(stamp);
    }

    public void SetStamp(Guid userId, string stamp)
    {
        var opts = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = s_ttl,
            Priority = CacheItemPriority.Low,
        };
        cache.Set(CacheKey(userId), stamp, opts);
    }

    public void EvictStamp(Guid userId) => cache.Remove(CacheKey(userId));
}
