using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using Xunit;

namespace NarsApi.Tests.Service;

/// <summary>
/// Shared scaffolding for PostgreSQL integration test classes in the
/// "PostgreSQL Integration" collection. Owns the <see cref="NarsDatabaseFixture"/>
/// and a per-class <see cref="AppDbContext"/>, and guarantees the shared schema is
/// cleaned up after every class runs (tests in the collection run serially).
///
/// Derived classes override <see cref="SeedAsync"/> to populate their own
/// reference data and use <see cref="Db"/>/<see cref="Fixture"/> instead of
/// declaring their own context/fixture fields.
/// </summary>
[Collection(PostgreSqlCollection.CollectionName)]
[Trait("Category", "Service")]
public abstract class ServiceTestBase(NarsDatabaseFixture fixture) : IAsyncLifetime
{
    protected NarsDatabaseFixture Fixture { get; } = fixture;

    protected AppDbContext Db { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Db = Fixture.CreateDbContext();
        await SeedAsync();
    }

    public async Task DisposeAsync()
    {
        try { await Db.DisposeAsync(); }
        finally { await Fixture.CleanTablesAsync(); }
    }

    /// <summary>Populates reference data and fixtures for the test class. Runs with <see cref="Db"/> ready.</summary>
    protected virtual Task SeedAsync() => Task.CompletedTask;
}
