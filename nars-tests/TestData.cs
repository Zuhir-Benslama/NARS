using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using NarsApi.Data;
using NarsApi.Infrastructure;

namespace NarsApi.Tests;

/// <summary>Shared constants used across test files to avoid magic-string duplication.</summary>
public static class TestData
{
    // ── Passwords ──────────────────────────────────────────────────────
    public const string DefaultPassword = "Str0ng!Pass";
    public const string AltPassword = "StrongP@ss1";

    // ── Phone numbers ──────────────────────────────────────────────────
    public const string DefaultPhone = "0555000000";
    public const string AltPhone = "0555123456";

    // ── Names & emails ─────────────────────────────────────────────────
    public const string DefaultEmail = "test@example.com";
    public const string AltEmail = "test@test.com";

    // ── JWT ────────────────────────────────────────────────────────────
    public static IOptions<JwtOptions> DefaultJwtOptions { get; } =
        Options.Create(new JwtOptions { ExpiresInMinutes = 60, RefreshExpiresInDays = 30 });

    // ── Location IDs ───────────────────────────────────────────────────
    public const int CommuneId1 = 1;
    public const int CommuneId2 = 2;
    public const int DairaId1 = 1;
    public const int DairaId2 = 2;
    public const int WilayaId1 = 1;
    public const int WilayaId2 = 2;

    // ── Date / time ────────────────────────────────────────────────────
    public static readonly DateTime FixedUtcNow = new(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);
    public static readonly DateTimeOffset FixedUtcNowOffset = new(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);

    // ── Helpers ────────────────────────────────────────────────────────
    public static string UniqueEmail(string prefix = "test") =>
        $"{prefix}-{Guid.NewGuid():N}@test.com";

    public static string UniqueUsername(string prefix = "user") =>
        $"{prefix}_{Guid.NewGuid():N}";

    public static AppDbContext CreateInMemoryDb(string prefix)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"{prefix}_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    public static IDbContextFactory<AppDbContext> CreateInMemoryDbFactory(string prefix)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"{prefix}_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new InMemoryDbContextFactory(options);
    }

    private sealed class InMemoryDbContextFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult<AppDbContext>(new(options));
    }
}
