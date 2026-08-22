using Xunit;
using Testcontainers.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NarsApi.Data;
using DotNet.Testcontainers.Configurations;

namespace NarsApi.Tests.Service;

/// <summary>
/// Shared PostgreSQL container for integration tests.
/// A collection fixture (ICollectionFixture) — all test classes in the
/// "PostgreSQL Integration" collection share this single container and run
/// serially. PostGIS is enabled explicitly in <see cref="InitializeAsync"/>.
/// </summary>
public sealed class NarsDatabaseFixture : IAsyncLifetime
{
    static NarsDatabaseFixture()
    {
        // Testcontainers' DockerDesktopEndpointAuthenticationProvider hardcodes
        // "/var/run/docker.sock" as the Resource Reaper's socket override (a
        // Docker Desktop VM assumption). On rootless Docker that path doesn't
        // exist, so the reaper's bind mount fails before any container starts.
        // When the standard socket is absent, clear the override so the reaper
        // derives the socket path from the (correctly resolved) daemon endpoint.
        if (!File.Exists("/var/run/docker.sock"))
        {
            TestcontainersSettings.DockerSocketOverride = null;
        }
    }

    // Digest-pinned for reproducible CI (matches the digest-pinning convention
    // used for the prod images in nars-infra/docker). Verify the digest with:
    //   docker buildx imagetools inspect postgis/postgis:17-3.5-alpine
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(
            "postgis/postgis:17-3.5-alpine@sha256:a7b31f03d1802e66d4d840ba31f20f9f3a8d3ffb2ba136596370a79356d6a327")
        .WithDatabase("nars_test")
        .WithUsername("nars")
        .WithPassword(Guid.NewGuid().ToString("N"))
        .Build();

    private bool _initialized;
    private IDbContextFactory<AppDbContext>? _sharedFactory;

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        try
        {
            await _container.StartAsync();

            // Enable PostGIS extension
            await using var conn = new Npgsql.NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE EXTENSION IF NOT EXISTS postgis;";
            await cmd.ExecuteNonQueryAsync();

            // Create the schema using EF migrations
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(ConnectionString, o => o.UseNetTopologySuite())
                .Options;

            await using var db = new AppDbContext(options);

            // Apply the real EF migrations from scratch. This doubles as a
            // migration smoke test: a broken/regressed migration fails here
            // instead of passing CI (EnsureCreatedAsync would have built the
            // schema straight from the model and skipped the migration SQL).
            await db.Database.MigrateAsync();

            // The EF chain deliberately does NOT own ai_draft_features — it is
            // created by nars-infra/migrations/0001_create_ai_draft_features.sql,
            // applied in production via `make db-migrate-nars` (see
            // AiDraftFeatureConfiguration). Re-apply that same idempotent script
            // so the test schema matches a migrated production cluster.
            var sqlPath = FindInfraSqlPath(
                "nars-infra", "migrations", "0001_create_ai_draft_features.sql");
            await using var draftCmd = conn.CreateCommand();
            draftCmd.CommandText = await File.ReadAllTextAsync(sqlPath);
            await draftCmd.ExecuteNonQueryAsync();

            _initialized = true;
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        try
        {
            (_sharedFactory as IDisposable)?.Dispose();
            await _container.StopAsync();
        }
        finally
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>
    /// Creates a fresh AppDbContext connected to the test container.
    /// </summary>
    public AppDbContext CreateDbContext()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("Database not initialized");
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString, o => o.UseNetTopologySuite())
            .Options;
        return new AppDbContext(options);
    }

    /// <summary>
    /// Creates an IDbContextFactory<AppDbContext> for the test container.
    /// </summary>
    public IDbContextFactory<AppDbContext> CreateDbContextFactory()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("Database not initialized");
        }

        if (_sharedFactory is not null)
        {
            return _sharedFactory;
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString, o => o.UseNetTopologySuite())
            .Options;
        _sharedFactory = new PooledDbContextFactory<AppDbContext>(options);
        return _sharedFactory;
    }

    /// <summary>
    /// Locates a file under the repository's nars-infra directory by walking up
    /// from the test output directory to the repo root.
    /// </summary>
    private static string FindInfraSqlPath(params string[] relativeParts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent!;
        }

        throw new InvalidOperationException(
            $"Could not locate {Path.Combine(relativeParts)} — walk up from {AppContext.BaseDirectory} found no repo root.");
    }

    /// <summary>
    /// Truncates all non-system tables in the public schema.
    /// Callers must reseed reference data and auth users afterwards.
    /// </summary>
    public async Task CleanTablesAsync()
    {
        await using var db = CreateDbContext();

        var tables = await db.Database.SqlQueryRaw<string>(
            "SELECT table_name FROM information_schema.tables " +
            "WHERE table_schema = 'public' AND table_type = 'BASE TABLE'"
        ).ToListAsync();

        // The migrations history table must survive: it records which EF
        // migrations the fixture already applied, and wiping it would make any
        // future MigrateAsync re-run the whole chain against existing tables.
        var userTables = tables.Where(t => t != "__EFMigrationsHistory").ToList();
        if (userTables.Count == 0)
        {
            return;
        }

        var tableList = string.Join(", ", userTables.Select(t => $"\"{t}\""));
#pragma warning disable EF1002 // Table names are trusted (from information_schema) and double-quoted
        await db.Database.ExecuteSqlRawAsync(
            $"TRUNCATE TABLE {tableList} RESTART IDENTITY CASCADE");
#pragma warning restore EF1002
    }

}
