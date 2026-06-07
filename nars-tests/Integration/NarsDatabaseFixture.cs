using Xunit;
using Testcontainers.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NarsApi.Data;

namespace NarsApi.Tests.Integration;

/// <summary>
/// Shared PostgreSQL container for integration tests.
/// Uses a class-level static container so all test classes share one instance.
/// PostGIS extension is enabled on first connection.
/// </summary>
public sealed class NarsDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgis/postgis:17-3.5-alpine")
        .WithDatabase("nars_test")
        .WithUsername("nars")
        .WithPassword("nars_password")
        .Build();

    private bool _initialized;

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
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
        await db.Database.EnsureCreatedAsync();
        _initialized = true;
    }

    public async Task DisposeAsync()
    {
        await _container.StopAsync();
        await _container.DisposeAsync();
    }

    /// <summary>
    /// Creates a fresh AppDbContext connected to the test container.
    /// </summary>
    public AppDbContext CreateDbContext()
    {
        if (!_initialized) throw new InvalidOperationException("Database not initialized");
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
        if (!_initialized) throw new InvalidOperationException("Database not initialized");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString, o => o.UseNetTopologySuite())
            .Options;
        return new PooledDbContextFactory<AppDbContext>(options);
    }

    /// <summary>
    /// Truncates all feature tables (but keeps reference data if seeded).
    /// </summary>
    public async Task CleanTablesAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.ExecuteSqlRawAsync(@"
            TRUNCATE TABLE
                naming_panels, public_spaces, public_buildings,
                house_entrances, roads, city_centers, districts, areas,
                feature_registry, refresh_tokens, users
            RESTART IDENTITY CASCADE;
        ");
    }
}
