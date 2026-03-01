using Microsoft.EntityFrameworkCore;
using NarsApi.Models;

namespace NarsApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User>            Users            { get; set; }
    public DbSet<Feature>         Features         { get; set; }
    public DbSet<Wilaya>          Wilayas          { get; set; }
    public DbSet<Daira>           Dairas           { get; set; }
    public DbSet<Commune>         Communes         { get; set; }
    public DbSet<CommuneBoundary> CommuneBoundaries { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── users ─────────────────────────────────────────────
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email).IsUnique();
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username).IsUnique();

        // ── features ──────────────────────────────────────────

        // Composite index on (user_id, type) — used by every WHERE clause in
        // FeaturesController and ValidationController that filters by user + type.
        modelBuilder.Entity<Feature>()
            .HasIndex(f => new { f.UserId, f.Type })
            .HasDatabaseName("ix_features_user_id_type");

        // Composite index on (user_id, type, layer) — used by validation queries
        // that also filter on layer (e.g. central_urban, main_entrance, scattered).
        modelBuilder.Entity<Feature>()
            .HasIndex(f => new { f.UserId, f.Type, f.Layer })
            .HasDatabaseName("ix_features_user_id_type_layer");

        // ── communes_boundaries ───────────────────────────────
        // Spatial index on the geometry column for fast ST_DWithin /
        // ST_Intersects / ST_Covers queries in the validation endpoints.
        modelBuilder.Entity<CommuneBoundary>()
            .HasIndex(cb => cb.Geometry)
            .HasDatabaseName("ix_communes_boundaries_geometry")
            .HasMethod("GIST");   // PostGIS spatial index type
    }
}
