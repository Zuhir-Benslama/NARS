using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Moq;
using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using Xunit;

namespace NarsApi.Tests;

/// <summary>
/// Coverage for the remaining low-coverage bootstrapping/plumbing types:
/// the design-time <see cref="AppDbContextFactory"/>, the
/// <see cref="UpdatedAtInterceptor"/> save-time timestamp writer, and
/// <see cref="ErrorLogService"/>.
/// </summary>
public class InfrastructureServicesTests
{
    private static IDateTimeProvider CreateFixedTime() =>
        Mock.Of<IDateTimeProvider>(p => p.UtcNow == TestData.FixedUtcNow);

    private static DbContextOptions<AppDbContext> CreateInMemoryOptions(string prefix, SaveChangesInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"{prefix}_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }
        return builder.Options;
    }

    private static Road NewRoad(Guid userId, string label) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Data = "{}",
        Label = label,
        Layer = "main",
    };

    // ── AppDbContextFactory (design-time) ──────────────────────────────

    /// <summary>
    /// Design-time factory reads connection info from environment variables, so
    /// these tests must serialize with the Program-startup env-var tests.
    /// </summary>
    [Collection(ProgramStartupCollection.Name)]
    public class AppDbContextFactoryTests : IDisposable
    {
        private static readonly string[] EnvKeys =
            ["ConnectionStrings__DefaultConnection", "NARS_DB_HOST", "NARS_DB_NAME", "NARS_DB_USER", "NARS_DB_PASSWORD"];

        private readonly Dictionary<string, string?> _saved = EnvKeys.ToDictionary(k => k, Environment.GetEnvironmentVariable);

        public void Dispose()
        {
            foreach (var (key, value) in _saved)
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }

        [Fact]
        public void CreateDbContext_UsesConnectionStringsEnvVar()
        {
            const string connStr = "Host=db.internal;Database=custom;Username=svc;Password=pw;Port=5433";
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", connStr);
            foreach (var key in EnvKeys.Where(k => k != "ConnectionStrings__DefaultConnection"))
            {
                Environment.SetEnvironmentVariable(key, null);
            }

            var db = new AppDbContextFactory().CreateDbContext([]);
            using (db)
            {
                var actual = db.Database.GetConnectionString();
                Assert.Contains("Host=db.internal", actual, StringComparison.Ordinal);
                Assert.Contains("Database=custom", actual, StringComparison.Ordinal);
                Assert.Contains("Username=svc", actual, StringComparison.Ordinal);
                Assert.Contains("Port=5433", actual, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void CreateDbContext_FallsBackToNarsEnvVars()
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
            Environment.SetEnvironmentVariable("NARS_DB_HOST", "nars-db.internal");
            Environment.SetEnvironmentVariable("NARS_DB_NAME", "nars_prod");
            Environment.SetEnvironmentVariable("NARS_DB_USER", "nars_app");
            Environment.SetEnvironmentVariable("NARS_DB_PASSWORD", "s3cret");

            var db = new AppDbContextFactory().CreateDbContext([]);
            using (db)
            {
                var connStr = db.Database.GetConnectionString();
                Assert.Contains("Host=nars-db.internal", connStr, StringComparison.Ordinal);
                Assert.Contains("Database=nars_prod", connStr, StringComparison.Ordinal);
                Assert.Contains("Username=nars_app", connStr, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void CreateDbContext_UsesDefaultsWhenNoEnvConfigured()
        {
            foreach (var key in EnvKeys)
            {
                Environment.SetEnvironmentVariable(key, null);
            }

            var db = new AppDbContextFactory().CreateDbContext([]);
            using (db)
            {
                var connStr = db.Database.GetConnectionString();
                Assert.Contains("Host=localhost", connStr, StringComparison.Ordinal);
                Assert.Contains("Database=nars_db", connStr, StringComparison.Ordinal);
                Assert.Contains("Username=postgres", connStr, StringComparison.Ordinal);
            }
        }
    }

    // ── UpdatedAtInterceptor ───────────────────────────────────────────

    public class UpdatedAtInterceptorTests
    {
        [Fact]
        public async Task SavingChanges_SetsTimestampsOnAddedEntries()
        {
            var userId = Guid.NewGuid();
            var options = CreateInMemoryOptions("updated_at_interceptor", new UpdatedAtInterceptor(CreateFixedTime()));
            await using var db = new AppDbContext(options);

            var addedRoad = NewRoad(userId, "added");
            db.Roads.Add(addedRoad);
            db.Inspections.Add(new Inspection { Id = Guid.NewGuid(), FeatureId = Guid.NewGuid(), UserId = userId, Type = "pothole", Data = "{}", Status = "open" });
            db.Users.Add(new User { Id = userId, Name = "A", Email = "a@example.com", Phone = "0555000000", Username = "a", PasswordHash = "x", Role = "commune_user" });
            db.RefreshTokens.Add(new RefreshToken { Id = Guid.NewGuid(), UserId = userId, TokenHash = "h", ExpiresAt = TestData.FixedUtcNow.AddDays(1) });

            await db.SaveChangesAsync();

            Assert.Equal(TestData.FixedUtcNow, addedRoad.CreatedAt);
            Assert.Equal(TestData.FixedUtcNow, addedRoad.UpdatedAt);

            var inspection = await db.Inspections.SingleAsync();
            Assert.Equal(TestData.FixedUtcNow, inspection.CreatedAt);
            Assert.Equal(TestData.FixedUtcNow, inspection.UpdatedAt);

            var user = await db.Users.SingleAsync();
            Assert.Equal(TestData.FixedUtcNow, user.CreatedAt);

            var token = await db.RefreshTokens.SingleAsync();
            Assert.Equal(TestData.FixedUtcNow, token.CreatedAt);
        }

        [Fact]
        public async Task SavingChanges_SetsUpdatedAtOnModifiedFeature()
        {
            var userId = Guid.NewGuid();
            var options = CreateInMemoryOptions("updated_at_interceptor", new UpdatedAtInterceptor(CreateFixedTime()));
            await using var db = new AppDbContext(options);

            var road = NewRoad(userId, "modified");
            db.Roads.Add(road);
            await db.SaveChangesAsync();

            db.Entry(road).State = EntityState.Detached;
            road.UpdatedAt = null;
            db.Attach(road);
            db.Entry(road).State = EntityState.Modified;

            await db.SaveChangesAsync();

            Assert.Equal(TestData.FixedUtcNow, road.UpdatedAt);
        }

        [Fact]
        public async Task SavingChanges_PreservesExistingCreatedAtOnAdded()
        {
            var userId = Guid.NewGuid();
            var options = CreateInMemoryOptions("updated_at_interceptor", new UpdatedAtInterceptor(CreateFixedTime()));
            await using var db = new AppDbContext(options);

            var road = NewRoad(userId, "kept");
            road.CreatedAt = TestData.FixedUtcNow.AddDays(-3);
            db.Roads.Add(road);

            await db.SaveChangesAsync();

            Assert.Equal(TestData.FixedUtcNow.AddDays(-3), road.CreatedAt);
            Assert.Equal(TestData.FixedUtcNow, road.UpdatedAt);
        }
    }

    // ── ErrorLogService ────────────────────────────────────────────────

    public class ErrorLogServiceTests
    {
        private static ErrorLog NewEntry(string message) => new() { Id = Guid.NewGuid(), Message = message };

        private static ErrorLogService CreateService(AppDbContext db, int maxBatchSize) =>
            new(db, Options.Create(new LoggingOptions { MaxBatchSize = maxBatchSize }));

        [Fact]
        public async Task LogBatchAsync_EmptyList_ReturnsWithoutSaving()
        {
            var options = CreateInMemoryOptions("error_log_service");
            await using var db = new AppDbContext(options);
            var service = CreateService(db, maxBatchSize: 50);

            await service.LogBatchAsync([]);

            Assert.Equal(0, await db.ErrorLogs.CountAsync());
        }

        [Fact]
        public async Task LogBatchAsync_PersistsAllEntriesWithinLimit()
        {
            var options = CreateInMemoryOptions("error_log_service");
            await using var db = new AppDbContext(options);
            var service = CreateService(db, maxBatchSize: 100);

            await service.LogBatchAsync([NewEntry("e1"), NewEntry("e2")]);

            Assert.Equal(2, await db.ErrorLogs.CountAsync());
        }

        [Fact]
        public async Task LogBatchAsync_TruncatesToMaxBatchSize()
        {
            var options = CreateInMemoryOptions("error_log_service");
            await using var db = new AppDbContext(options);
            var service = CreateService(db, maxBatchSize: 2);

            await service.LogBatchAsync([NewEntry("e1"), NewEntry("e2"), NewEntry("e3")]);

            Assert.Equal(2, await db.ErrorLogs.CountAsync());
        }
    }
}
