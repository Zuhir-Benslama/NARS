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
            await db.Database.EnsureCreatedAsync();
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

        if (tables.Count == 0)
        {
            return;
        }

        var tableList = string.Join(", ", tables.Select(t => $"\"{t}\""));
#pragma warning disable EF1002 // Table names are trusted (from information_schema) and double-quoted
        await db.Database.ExecuteSqlRawAsync(
            $"TRUNCATE TABLE {tableList} RESTART IDENTITY CASCADE");
#pragma warning restore EF1002
    }

}
