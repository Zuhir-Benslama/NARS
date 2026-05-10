using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NarsApi.Data;

namespace NarsApi.Infrastructure;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connStr = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrEmpty(connStr))
        {
            var host = Environment.GetEnvironmentVariable("NARS_DB_HOST") ?? "localhost";
            var db = Environment.GetEnvironmentVariable("NARS_DB_NAME") ?? "nars_db";
            var user = Environment.GetEnvironmentVariable("NARS_DB_USER") ?? "postgres";
            var pass = Environment.GetEnvironmentVariable("NARS_DB_PASSWORD") ?? "postgres";
            connStr = $"Host={host};Database={db};Username={user};Password={pass}";
        }

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(connStr, o => o.UseNetTopologySuite());

        return new AppDbContext(optionsBuilder.Options);
    }
}
