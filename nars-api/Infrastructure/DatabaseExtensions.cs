using Microsoft.EntityFrameworkCore;
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
        services.AddSingleton<UpdatedAtInterceptor>();

        // The DbContext stays scoped for request-scoped services, while the
        // options are registered as a singleton so the singleton
        // IDbContextFactory (used by background/singleton services) can consume
        // them without a captive-scope violation.
        services.AddDbContext<AppDbContext>(
            (sp, options) => options.UseNpgsql(connectionString, o => o.UseNetTopologySuite())
                                    .AddInterceptors(sp.GetRequiredService<UpdatedAtInterceptor>()),
            contextLifetime: ServiceLifetime.Scoped,
            optionsLifetime: ServiceLifetime.Singleton);

        // DbContextFactory for parallel queries outside request scope
        services.AddDbContextFactory<AppDbContext>((sp, options) =>
            options.UseNpgsql(connectionString, o => o.UseNetTopologySuite())
                   .AddInterceptors(sp.GetRequiredService<UpdatedAtInterceptor>()));

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
