using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NarsApi.Data;
using Npgsql;

namespace NarsApi.Infrastructure;

/// <summary>
/// Extension methods for database configuration.
/// </summary>
public static class DatabaseExtensions
{
    /// <summary>
    /// Adds EF Core DbContext with Npgsql/PostGIS support.
    /// </summary>
    public static IServiceCollection AddNarsDatabase(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString, o => o.UseNetTopologySuite())
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

        // DbContextFactory for parallel queries outside request scope
        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseNpgsql(connectionString, o => o.UseNetTopologySuite())
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

        return services;
    }

    /// <summary>
    /// Adds a health check that verifies database connectivity.
    /// Uses Npgsql directly (AspNetCore.HealthChecks.NpgSql package).
    /// </summary>
    public static IHealthChecksBuilder AddNarsDatabaseHealthCheck(
        this IHealthChecksBuilder builder,
        string connectionString)
    {
        // HealthChecks.NpgSql provides AddNpgSql — it runs a simple SELECT 1
        // against the configured connection string to verify DB reachability.
        builder.AddNpgSql(connectionString, name: "database");
        return builder;
    }
}
