using Microsoft.EntityFrameworkCore;
using NarsApi.Data;

namespace NarsApi.Tests;

internal sealed class TestDbContextFactory(AppDbContext db) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => db;
    public ValueTask<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(db);
}
