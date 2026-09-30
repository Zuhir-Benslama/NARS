namespace NarsApi.Models;

/// <summary>
/// Maps to the generation_job_chunks table. One row per grid tile of a
/// generation job. The client renders each tile and uploads it via the job
/// API; the worker pool claims ready rows (FOR UPDATE SKIP LOCKED), runs
/// segmentation against nars-segma and appends the resulting draft ids back
/// onto the parent job. Rasters are stored as BYTEA so jobs survive pod
/// restarts. Status constants mirror the SQL CHECK constraint values.
/// </summary>
public sealed class GenerationJobChunk
{
    public const string StatusAwaitingRaster = "awaiting_raster";
    public const string StatusReady = "ready";
    public const string StatusRunning = "running";
    public const string StatusDone = "done";
    public const string StatusFailed = "failed";
    public const string StatusCancelled = "cancelled";

    public Guid Id { get; internal set; }
    public Guid JobId { get; internal set; }
    public string ChunkKey { get; internal set; } = null!;
    public int Zoom { get; internal set; }
    public int X0 { get; internal set; }
    public int Y0 { get; internal set; }
    public int Width { get; internal set; }
    public int Height { get; internal set; }
    public double? MinLon { get; internal set; }
    public double? MinLat { get; internal set; }
    public double? MaxLon { get; internal set; }
    public double? MaxLat { get; internal set; }
    public byte[]? Raster { get; internal set; }
    public string? RasterFileName { get; internal set; }
    public string RasterContentType { get; internal set; } = "image/jpeg";
    public string Status { get; internal set; } = StatusAwaitingRaster;
    public int Attempts { get; internal set; }
    public string? Error { get; internal set; }
    public DateTimeOffset? HeartbeatAt { get; internal set; }
    public DateTimeOffset CreatedAt { get; internal set; }
    public DateTimeOffset UpdatedAt { get; internal set; }

    private GenerationJobChunk() { } // EF Core

    public static GenerationJobChunk Create(
        Guid jobId,
        string chunkKey,
        int zoom,
        (int X, int Y, int Width, int Height) grid,
        (double MinLon, double MinLat, double MaxLon, double MaxLat) bounds,
        DateTimeOffset createdAt)
    {
        return new GenerationJobChunk
        {
            Id = Guid.CreateVersion7(),
            JobId = jobId,
            ChunkKey = chunkKey,
            Zoom = zoom,
            X0 = grid.X,
            Y0 = grid.Y,
            Width = grid.Width,
            Height = grid.Height,
            MinLon = bounds.MinLon,
            MinLat = bounds.MinLat,
            MaxLon = bounds.MaxLon,
            MaxLat = bounds.MaxLat,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };
    }
}
