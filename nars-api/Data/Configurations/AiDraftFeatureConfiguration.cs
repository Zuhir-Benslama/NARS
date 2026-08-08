using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NarsApi.Models;

namespace NarsApi.Data.Configurations;

/// <summary>
/// EF mapping for ai_draft_features. The table is created by the SQL migration
/// (nars-infra/migrations/0001_create_ai_draft_features.sql), which is applied
/// outside EF migrations because the geometry column is JSONB with a
/// feature-type-aware CHECK constraint. This configuration lets EF query and
/// update the table; if the project ever auto-generates EF migrations from the
/// model, mark the corresponding migration a no-op so the SQL migration stays
/// authoritative.
/// </summary>
public sealed class AiDraftFeatureConfiguration : IEntityTypeConfiguration<AiDraftFeature>
{
    public void Configure(EntityTypeBuilder<AiDraftFeature> builder)
    {
        builder.ToTable("ai_draft_features");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.Id).HasColumnName("id");
        builder.Property(f => f.FeatureType).HasColumnName("feature_type").HasMaxLength(20).IsRequired();
        builder.Property(f => f.GeometryGeoJson).HasColumnName("geometry").HasColumnType("jsonb").IsRequired();
        builder.Property(f => f.Source).HasColumnName("source").HasMaxLength(20);
        builder.Property(f => f.Confidence).HasColumnName("confidence");
        builder.Property(f => f.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(f => f.CommuneId).HasColumnName("commune_id");
        builder.Property(f => f.ReviewedBy).HasColumnName("reviewed_by");
        builder.Property(f => f.ReviewedAt).HasColumnName("reviewed_at");
        builder.Property(f => f.CreatedAt).HasColumnName("created_at");
        builder.Property(f => f.SourceTileRef).HasColumnName("source_tile_ref").HasMaxLength(255);

        builder.HasIndex(f => new { f.FeatureType, f.Status, f.CommuneId });
    }
}
