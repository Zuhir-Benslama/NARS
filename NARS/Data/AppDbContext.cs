using Microsoft.EntityFrameworkCore;
using NarsApi.Models;

namespace NarsApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // ── Feature tables ────────────────────────────────────────────────────────
    public DbSet<FeatureRegistry> FeatureRegistry { get; set; }
    public DbSet<Area> Areas { get; set; }
    public DbSet<District> Districts { get; set; }
    public DbSet<CityCenter> CityCenters { get; set; }
    public DbSet<Road> Roads { get; set; }
    public DbSet<HouseEntrance> HouseEntrances { get; set; }
    public DbSet<PublicBuilding> PublicBuildings { get; set; }
    public DbSet<PublicSpace> PublicSpaces { get; set; }
    public DbSet<NamingPanel> NamingPanels { get; set; }

    // ── Field worker inspections ──────────────────────────────────────────────
    public DbSet<Inspection> Inspections { get; set; }

    // ── Logs ──────────────────────────────────────────────────────────────────
    public DbSet<ErrorLog> ErrorLogs { get; set; }

    // ── Reference tables ──────────────────────────────────────────────────────
    public DbSet<User> Users { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }
    public DbSet<Wilaya> Wilayas { get; set; }
    public DbSet<Daira> Dairas { get; set; }
    public DbSet<Commune> Communes { get; set; }
    public DbSet<CommuneBoundary> CommuneBoundaries { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── users ──────────────────────────────────────────────────────────────
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email).IsUnique();
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username).IsUnique();
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Role)
            .HasDatabaseName("ix_users_role");
        modelBuilder.Entity<User>()
            .HasIndex(u => u.DairaId)
            .HasDatabaseName("ix_users_daira_id")
            .HasFilter("daira_id IS NOT NULL");
        modelBuilder.Entity<User>()
            .HasIndex(u => u.WilayaId)
            .HasDatabaseName("ix_users_wilaya_id")
            .HasFilter("wilaya_id IS NOT NULL");
        modelBuilder.Entity<User>()
            .HasIndex(u => new { u.CommuneId, u.Role })
            .HasDatabaseName("ix_users_commune_role")
            .HasFilter("commune_id IS NOT NULL");

        // ── feature_registry ───────────────────────────────────────────────────
        // UUID primary keys — no sequence needed, generated client-side.

        // ── areas ──────────────────────────────────────────────────────────────
        modelBuilder.Entity<Area>()
            .HasIndex(a => a.UserId)
            .HasDatabaseName("ix_areas_user_id");
        modelBuilder.Entity<Area>()
            .HasIndex(a => new { a.UserId, a.Layer })
            .HasDatabaseName("ix_areas_user_layer");

        // ── districts ──────────────────────────────────────────────────────────
        modelBuilder.Entity<District>()
            .HasIndex(d => d.UserId)
            .HasDatabaseName("ix_districts_user_id");

        // ── roads ──────────────────────────────────────────────────────────────
        modelBuilder.Entity<Road>()
            .HasIndex(r => r.UserId)
            .HasDatabaseName("ix_roads_user_id");
        modelBuilder.Entity<Road>()
            .HasIndex(r => new { r.UserId, r.Layer })
            .HasDatabaseName("ix_roads_user_layer");

        // ── house_entrances ────────────────────────────────────────────────────
        modelBuilder.Entity<HouseEntrance>()
            .HasIndex(e => e.UserId)
            .HasDatabaseName("ix_house_entrances_user_id");
        modelBuilder.Entity<HouseEntrance>()
            .HasIndex(e => new { e.UserId, e.Layer })
            .HasDatabaseName("ix_house_entrances_user_layer");
        modelBuilder.Entity<HouseEntrance>()
            .HasIndex(e => e.RoadId)
            .HasDatabaseName("ix_house_entrances_road_id")
            .HasFilter("road_id IS NOT NULL");

        // ── public_buildings / public_spaces / naming_panels / city_centers ───
        foreach (var (entityType, indexName) in new[]
        {
            (typeof(PublicBuilding), "ix_public_buildings_user_id"),
            (typeof(PublicSpace),    "ix_public_spaces_user_id"),
            (typeof(NamingPanel),    "ix_naming_panels_user_id"),
            (typeof(CityCenter),     "ix_city_centers_user_id"),
        })
        {
            modelBuilder.Entity(entityType)
                .HasIndex("UserId")
                .HasDatabaseName(indexName);
        }

        // ── communes_boundaries: spatial index ─────────────────────────────────
        modelBuilder.Entity<CommuneBoundary>()
            .HasIndex(cb => cb.Geometry)
            .HasDatabaseName("ix_communes_boundaries_geometry")
            .HasMethod("GIST");

        // ── refresh_tokens: index on token_hash for efficient refresh lookups ──
        modelBuilder.Entity<RefreshToken>()
            .HasIndex(rt => rt.TokenHash)
            .HasDatabaseName("ix_refresh_tokens_token_hash");

        // ── inspections: index on feature_id for history lookups ──────────────
        modelBuilder.Entity<Inspection>()
            .HasIndex(i => i.FeatureId)
            .HasDatabaseName("ix_inspections_feature_id");
        modelBuilder.Entity<Inspection>()
            .HasIndex(i => i.UserId)
            .HasDatabaseName("ix_inspections_user_id");
        modelBuilder.Entity<Inspection>()
            .HasIndex(i => new { i.FeatureId, i.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_inspections_feature_created");
    }
}
