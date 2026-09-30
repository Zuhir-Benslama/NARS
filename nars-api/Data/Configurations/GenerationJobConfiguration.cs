using System.Linq;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NarsApi.Models;

namespace NarsApi.Data.Configurations;

/// <summary>
/// EF mapping for generation_jobs / generation_job_chunks. The tables are
/// created by the SQL migration (nars-infra/migrations/0002_create_generation_jobs.sql),
/// applied outside EF migrations (same pattern as AiDraftFeature): this
/// configuration lets EF read/update them, and the SQL DDL stays authoritative.
/// The tables are excluded from EF migrations so the generated migration is
/// empty (no DDL conflict with the SQL files) while the model snapshot still
/// tracks the entities, which is what keeps `dotnet ef database update` from
/// flagging them as pending model changes.
/// Only the jsonb user-data columns (draft_ids, result) need explicit mappings;
/// rasters are plain BYTEA and map directly to byte[].
/// </summary>
public sealed class GenerationJobConfiguration : IEntityTypeConfiguration<GenerationJob>
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private static readonly ValueComparer<List<Guid>> DraftIdComparer = new(
        (a, b) => (object?)a == (object?)b || (a != null && b != null && a.SequenceEqual(b)),
        list => list.Aggregate(0, (acc, id) => HashCode.Combine(acc, id.GetHashCode())),
        list => list.ToList());

    public void Configure(EntityTypeBuilder<GenerationJob> builder)
    {
        builder.ToTable("generation_jobs", t => t.ExcludeFromMigrations());

        builder.HasKey(j => j.Id);

        builder.Property(j => j.Id).HasColumnName("id");
        builder.Property(j => j.CommuneId).HasColumnName("commune_id");
        builder.Property(j => j.CreatedBy).HasColumnName("created_by");
        builder.Property(j => j.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(j => j.Stage).HasColumnName("stage").HasMaxLength(10);
        builder.Property(j => j.TotalChunks).HasColumnName("total_chunks");
        builder.Property(j => j.DoneChunks).HasColumnName("done_chunks");
        builder.Property(j => j.Progress).HasColumnName("progress");
        builder.Property(j => j.DraftIds)
            .HasColumnName("draft_ids")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, SerializerOptions),
                v => JsonSerializer.Deserialize<List<Guid>>(v, SerializerOptions) ?? new List<Guid>(),
                DraftIdComparer);
        builder.Property(j => j.CallerRole).HasColumnName("caller_role").HasMaxLength(20);
        builder.Property(j => j.CallerCommuneId).HasColumnName("caller_commune_id");
        builder.Property(j => j.CallerDairaId).HasColumnName("caller_daira_id");
        builder.Property(j => j.CallerWilayaId).HasColumnName("caller_wilaya_id");
        builder.Property(j => j.Result).HasColumnName("result").HasColumnType("jsonb");
        builder.Property(j => j.Error).HasColumnName("error");
        builder.Property(j => j.AcceptHeartbeatAt).HasColumnName("accept_heartbeat_at");
        builder.Property(j => j.CreatedAt).HasColumnName("created_at");
        builder.Property(j => j.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(j => new { j.CommuneId, j.CreatedAt })
            .HasDatabaseName("ix_generation_job_commune");
        builder.HasIndex(j => new { j.Status, j.CreatedAt })
            .HasDatabaseName("ix_generation_job_status");
    }
}

public sealed class GenerationJobChunkConfiguration : IEntityTypeConfiguration<GenerationJobChunk>
{
    public void Configure(EntityTypeBuilder<GenerationJobChunk> builder)
    {
        builder.ToTable("generation_job_chunks", t => t.ExcludeFromMigrations());

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.JobId).HasColumnName("job_id");
        builder.Property(c => c.ChunkKey).HasColumnName("chunk_key").HasMaxLength(40).IsRequired();
        builder.Property(c => c.Zoom).HasColumnName("zoom");
        builder.Property(c => c.X0).HasColumnName("x0");
        builder.Property(c => c.Y0).HasColumnName("y0");
        builder.Property(c => c.Width).HasColumnName("width");
        builder.Property(c => c.Height).HasColumnName("height");
        builder.Property(c => c.MinLon).HasColumnName("min_lon");
        builder.Property(c => c.MinLat).HasColumnName("min_lat");
        builder.Property(c => c.MaxLon).HasColumnName("max_lon");
        builder.Property(c => c.MaxLat).HasColumnName("max_lat");
        builder.Property(c => c.Raster).HasColumnName("raster").HasColumnType("bytea");
        builder.Property(c => c.RasterFileName).HasColumnName("raster_file_name").HasMaxLength(255);
        builder.Property(c => c.RasterContentType).HasColumnName("raster_content_type").HasMaxLength(30);
        builder.Property(c => c.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(c => c.Attempts).HasColumnName("attempts");
        builder.Property(c => c.Error).HasColumnName("error");
        builder.Property(c => c.HeartbeatAt).HasColumnName("heartbeat_at");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(c => c.JobId)
            .HasDatabaseName("ix_generation_job_chunks_job");
        builder.HasIndex(c => new { c.Status, c.CreatedAt })
            .HasDatabaseName("ix_generation_job_chunks_claim");

        builder.HasOne<GenerationJob>()
            .WithMany()
            .HasForeignKey(c => c.JobId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
