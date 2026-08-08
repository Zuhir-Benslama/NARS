using Microsoft.EntityFrameworkCore;
using NarsApi.Data.Configurations;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
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

    // ── AI draft features (review queue) ────────────────────────────────────
    public DbSet<AiDraftFeature> AiDraftFeatures { get; set; }

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

        // ── Feature type indexes — driven by FeatureTypeDescriptor registry ──
        foreach (var descriptor in FeatureTypeRegistry.GetAllDescriptors())
        {
            foreach (var idx in descriptor.Indexes)
            {
                modelBuilder.Entity(descriptor.EntityType)
                    .HasIndex(idx.PropertyName)
                    .HasDatabaseName(idx.IndexName);
            }

            foreach (var idx in descriptor.CompositeIndexes)
            {
                var builder = modelBuilder.Entity(descriptor.EntityType)
                    .HasIndex(idx.PropertyNames)
                    .HasDatabaseName(idx.IndexName);

                if (idx.Filter is not null)
                {
                    builder.HasFilter(idx.Filter);
                }
            }
        }

        // ── communes_boundaries: spatial index ─────────────────────────────────
        modelBuilder.Entity<CommuneBoundary>()
            .HasIndex(cb => cb.Geometry)
            .HasDatabaseName("ix_communes_boundaries_geometry")
            .HasMethod("GIST");

        // ── refresh_tokens: index on token_hash for efficient refresh lookups ──
        modelBuilder.Entity<RefreshToken>()
            .HasIndex(rt => rt.TokenHash)
            .IsUnique()
            .HasDatabaseName("ix_refresh_tokens_token_hash");

        // ── error_logs: indexes for common query patterns ────────────────────
        modelBuilder.Entity<ErrorLog>()
            .HasIndex(el => el.UserId)
            .HasDatabaseName("ix_error_logs_user_id");
        modelBuilder.Entity<ErrorLog>()
            .HasIndex(el => el.Level)
            .HasDatabaseName("ix_error_logs_level");
        modelBuilder.Entity<ErrorLog>()
            .HasIndex(el => el.CreatedAt)
            .HasDatabaseName("ix_error_logs_created_at");

        // ── inspections: index on feature_id for history lookups ──────────────
        modelBuilder.Entity<Inspection>()
            .HasIndex(i => i.FeatureId)
            .HasDatabaseName("ix_inspections_feature_id");
        modelBuilder.Entity<Inspection>()
            .HasIndex(i => i.UserId)
            .HasDatabaseName("ix_inspections_user_id");
        // Composite index: FeatureId ASC (false = not descending), CreatedAt DESC (true = descending).
        // Optimizes "latest inspections per feature" queries.
        modelBuilder.Entity<Inspection>()
            .HasIndex(i => new { i.FeatureId, i.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_inspections_feature_created");

        // ── Relationships / foreign keys ─────────────────────────────────────
        // The schema previously declared no FKs, so referential integrity was
        // enforced only by hand-written delete code. These three relationships
        // are unambiguous (single parent table each) and cascade to match the
        // existing DeleteUserAsync flow (children removed before the user row).
        modelBuilder.Entity<RefreshToken>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(rt => rt.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Inspection>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(i => i.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ErrorLog>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(el => el.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── ai_draft_features (created by SQL migration, EF just queries/updates) ─
        modelBuilder.ApplyConfiguration(new AiDraftFeatureConfiguration());
    }
}
