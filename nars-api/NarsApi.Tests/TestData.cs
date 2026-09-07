using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;
using System.Text.Json;

namespace NarsApi.Tests;

/// <summary>Shared constants used across test files to avoid magic-string duplication.</summary>
public static class TestData
{
    // ── Passwords ──────────────────────────────────────────────────────
    public const string DefaultPassword = "Str0ng!Pass";
    public const string AltPassword = "StrongP@ss1";
    /// <summary>Placeholder hash for test users that never authenticate via password.</summary>
    public const string DummyPasswordHash = "hash";
    /// <summary>
    /// A valid bcrypt hash of <see cref="DefaultPassword"/>, computed once at
    /// first access. Test users that only <em>store</em> a password hash (their
    /// credentials are never verified) reuse this instead of re-hashing at every
    /// case, which costs ~50-100ms per call at default cost 10.
    /// </summary>
    public static readonly string DefaultPasswordHash = BCrypt.Net.BCrypt.HashPassword(DefaultPassword);

    // ── Auth tokens ────────────────────────────────────────────────────
    public const string AdminSignupToken = "nars-admin-signup-v1";

    // ── Route constants ────────────────────────────────────────────────
    public const string LoginPath = "/login";
    public const string ApiFeaturesPath = "/api/features";
    public const string ApiLogsPath = "/api/logs";
    public const string ApiAuthSignInPath = "/api/signin";

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
    // Fixed value so tests are fully deterministic (each test uses its own
    // in-memory DB, so sharing the ID across tests cannot collide).
    public static readonly Guid UserId = new("11111111-1111-1111-1111-111111111111");

    // ── Location IDs ───────────────────────────────────────────────────
    // NOTE: suites deliberately use DIFFERENT ID namespaces and must not mix
    // them. The bulk of unit AND integration suites (controller auth flows,
    // features, field, stats, validation, spatial, user profile) seed via
    // SeedData.SeedBasicLocationsAsync and use CommuneId1/2. Admin-scoped
    // suites (draft features, commune scope, admin controllers/services,
    // locations) seed via SeedData.SeedAdminLocationsAsync and use the 100/101
    // namespace. Keep the two namespaces distinct on purpose so a test that
    // mixes them fails loudly instead of silently sharing rows.
    public const int CommuneId1 = 1;
    public const int CommuneId2 = 2;

    // IDs seeded by SeedData.SeedAdminLocationsAsync:
    //   wilaya 1/2, daira 10/11, commune 100/101.
    public const int WilayaId1 = 1;
    public const int WilayaId2 = 2;
    public const int DairaId10 = 10;
    public const int DairaId11 = 11;
    public const int CommuneId100 = 100;
    public const int CommuneId101 = 101;

    // ── "Not found" sentinels ───────────────────────────────────────────
    public const int NonExistentId = 999;

    // ── Date / time ────────────────────────────────────────────────────
    public static readonly DateTime FixedUtcNow = new(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);
    public static readonly DateTimeOffset FixedUtcNowOffset = new(2025, 6, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Parses json into a detached <see cref="JsonElement"/>. The intermediate
    /// JsonDocument is disposed immediately; Clone() keeps the element valid.
    /// </summary>
    public static JsonElement ToJsonElement(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>Serializes value to JSON and returns a detached JsonElement.</summary>
    public static JsonElement ToJsonElement<T>(T value) => ToJsonElement(JsonSerializer.Serialize(value));

    /// <summary>
    /// Canonical AuthorizedAdminSignupRequest for tests. Only the scenario-
    /// relevant fields need overriding; the rest are consistent valid defaults.
    /// </summary>
    public static AuthorizedAdminSignupRequest ValidAdminSignup(
        string? username = null,
        string? password = null,
        string? email = null,
        string? name = null,
        string? phone = null,
        int? communeId = null,
        string role = UserRoles.CommuneUser,
        string adminUsername = "admin")
        => new(
            AdminUsername: adminUsername,
            AdminPassword: DefaultPassword,
            Name: name ?? "Test User",
            Email: email ?? DefaultEmail,
            Phone: phone ?? AltPhone,
            Username: username ?? "testuser",
            Password: password ?? AltPassword,
            Role: role,
            CommuneId: communeId ?? CommuneId1,
            DairaId: null,
            WilayaId: null);

    // ── Helpers ────────────────────────────────────────────────────────
    public static AppDbContext CreateInMemoryDb(string prefix, SaveChangesInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"{prefix}_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }
        return new AppDbContext(builder.Options);
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
