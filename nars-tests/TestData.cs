using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Tests;

/// <summary>Shared constants used across test files to avoid magic-string duplication.</summary>
public static class TestData
{
    // ── Passwords ──────────────────────────────────────────────────────
    public const string DefaultPassword = "Str0ng!Pass";
    public const string AltPassword = "StrongP@ss1";

    // ── Auth tokens ────────────────────────────────────────────────────
    public const string AdminSignupToken = "nars-admin-signup-v1";

    // ── Phone numbers ──────────────────────────────────────────────────
    public const string DefaultPhone = "0555000000";
    public const string AltPhone = "0555123456";

    // ── Names & emails ─────────────────────────────────────────────────
    public const string DefaultEmail = "test@example.com";
    public const string AltEmail = "test@test.com";

    // ── JWT ────────────────────────────────────────────────────────────
    public static IOptions<JwtOptions> DefaultJwtOptions { get; } =
        Options.Create(new JwtOptions { ExpiresInMinutes = 60, RefreshExpiresInDays = 30 });

    // ── Feature type counts ────────────────────────────────────────────
    public const int ExpectedFeatureTypeCount = 8;

    // ── Logging limits ─────────────────────────────────────────────────
    public const int DefaultMaxBatchSize = 100;
    public const int DefaultMaxEntryLength = 1000;

    // ── Feature data sizes ─────────────────────────────────────────────
    public const int OversizedDataLength = 600_000;

    // ── User IDs ───────────────────────────────────────────────────────
    public static readonly Guid UserId = Guid.NewGuid();

    // ── Location IDs ───────────────────────────────────────────────────
    public const int CommuneId1 = 1;

    // ── Date / time ────────────────────────────────────────────────────
    public static readonly DateTime FixedUtcNow = new(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);
    public static readonly DateTimeOffset FixedUtcNowOffset = new(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);

    // ── Helpers ────────────────────────────────────────────────────────
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

    /// <summary>
    /// Creates a seed context and a factory that share the same in-memory database.
    /// Seed data via <c>db</c>, query via the returned <c>factory</c>.
    /// </summary>
    public static (AppDbContext db, IDbContextFactory<AppDbContext> factory) CreateInMemoryDbPair(string prefix)
    {
        var dbName = $"{prefix}_{Guid.NewGuid()}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return (new AppDbContext(options), new InMemoryDbContextFactory(options));
    }

    /// <summary>
    /// Inserts a road into the database. Used by both unit and integration tests.
    /// </summary>
    public static async Task<Guid> AddRoadAsync(AppDbContext db, Guid userId, string coordsJson,
        bool registerInFeatureRegistry = false)
    {
        var id = Guid.NewGuid();
        db.Roads.Add(new Road
        {
            Id = id,
            UserId = userId,
            Data = coordsJson,
            Label = "Test Road",
            Layer = "main",
            UpdatedAt = FixedUtcNow,
        });
        if (registerInFeatureRegistry)
        {
            db.FeatureRegistry.Add(new FeatureRegistry { Id = id, FeatureType = FeatureTypes.Road });
        }
        await db.SaveChangesAsync();
        return id;
    }
}
