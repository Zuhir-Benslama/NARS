using Microsoft.EntityFrameworkCore;
using NarsApi.Models;

namespace NarsApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // ── Feature tables ────────────────────────────────────────────────────────
    public DbSet<FeatureRegistry>  FeatureRegistry  { get; set; }
    public DbSet<Area>             Areas            { get; set; }
    public DbSet<District>         Districts        { get; set; }
    public DbSet<CityCenter>       CityCenters      { get; set; }
    public DbSet<Road>             Roads            { get; set; }
    public DbSet<HouseEntrance>    HouseEntrances   { get; set; }
    public DbSet<PublicBuilding>   PublicBuildings  { get; set; }
    public DbSet<PublicSpace>      PublicSpaces     { get; set; }
    public DbSet<NamingPanel>      NamingPanels     { get; set; }

    // ── Reference tables ──────────────────────────────────────────────────────
    public DbSet<User>             Users            { get; set; }
    public DbSet<Wilaya>           Wilayas          { get; set; }
    public DbSet<Daira>            Dairas           { get; set; }
    public DbSet<Commune>          Communes         { get; set; }
    public DbSet<CommuneBoundary>  CommuneBoundaries { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── users ──────────────────────────────────────────────────────────────
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email).IsUnique();
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username).IsUnique();

        // ── feature_registry ───────────────────────────────────────────────────
        // id comes from the shared feature_id_seq — tell EF not to auto-generate it.
        modelBuilder.Entity<FeatureRegistry>()
            .Property(r => r.Id)
            .ValueGeneratedNever();

        // ── feature tables: shared sequence ───────────────────────────────────
        // All feature PKs draw from feature_id_seq defined in the SQL schema.
        foreach (var featureType in new[]
        {
            typeof(Area), typeof(District), typeof(CityCenter),
            typeof(Road), typeof(HouseEntrance),
            typeof(PublicBuilding), typeof(PublicSpace), typeof(NamingPanel)
        })
        {
            modelBuilder.Entity(featureType)
                .Property("Id")
                .HasDefaultValueSql("nextval('feature_id_seq')");
        }

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
    }
}
