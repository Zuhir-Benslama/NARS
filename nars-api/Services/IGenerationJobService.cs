using System.Text.Json;
using NarsApi.DTOs;

namespace NarsApi.Services;

/// <summary>Work claimed by a worker of the generation queue.</summary>
public enum GenerationWorkKind
{
    /// <summary>Segment one uploaded raster chunk (nars-segma + draft insert).</summary>
    Chunk,

    /// <summary>Run the roads-phase acceptance for a fully-segmented job.</summary>
    Accept,
}

/// <summary>
/// A claimed chunk: the worker loads the raster and re-runs the exact same
/// segmentation path the synchronous endpoint uses
/// (<see cref="IDraftFeaturesService.SegmentTileAsync"/>), passing the stored
/// caller scope so authority follows the job creator.
/// </summary>
public sealed record GenerationChunkWorkItem(
    Guid JobId,
    Guid ChunkId,
    string ChunkKey,
    string CallerRole,
    int? CallerCommuneId,
    int? CallerDairaId,
    int? CallerWilayaId,
    int CommuneId,
    (double MinLon, double MinLat, double MaxLon, double MaxLat) Bounds,
    string RasterFileName,
    string RasterContentType);

/// <summary>
/// A claimed acceptance pass: every chunk is done, so the worker runs
/// IRoadGenerationService.GenerateAsync over the accumulated draft ids with the
/// job creator's scope. Reclaimable when <c>accept_heartbeat_at</c> goes stale
/// (crashed worker), so acceptance is ownerless after the last chunk commits.
/// </summary>
public sealed record GenerationAcceptWorkItem(
    Guid JobId,
    Guid CreatedBy,
    string CallerRole,
    int? CallerCommuneId,
    int? CallerDairaId,
    int? CallerWilayaId,
    int CommuneId,
    IReadOnlyList<Guid> DraftIds);

/// <summary>
/// Orchestrates the async road-generation queue: job lifecycle, per-chunk
/// raster uploads, and the FOR UPDATE SKIP LOCKED claim surface used by the
/// worker pool. All user-facing methods enforce commune scope against the
/// stored caller identity before touching a job.
/// </summary>
public interface IGenerationJobService
{
    Task<GenerationJobView> CreateAsync(
        string callerRole, int? callerCommuneId, int? callerDairaId, int? callerWilayaId,
        Guid userId, int communeId, IReadOnlyList<GenerationGridDto> grids, CancellationToken ct);

    Task<GenerationJobView> GetViewAsync(
        Guid jobId, string callerRole, int? callerCommuneId, int? callerDairaId,
        int? callerWilayaId, CancellationToken ct);

    Task<GenerationJobView> UploadChunkRasterAsync(
        Guid jobId, Guid chunkId, string callerRole, int? callerCommuneId, int? callerDairaId,
        int? callerWilayaId, byte[] raster, string fileName, string contentType, CancellationToken ct);

    Task<GenerationJobView> CancelAsync(
        Guid jobId, string callerRole, int? callerCommuneId, int? callerDairaId,
        int? callerWilayaId, CancellationToken ct);

    /// <summary>
    /// Claims one unit of work: first a ready or stale-running chunk of a
    /// segment-phase job (bounded by <paramref name="maxAttempts"/>), then an
    /// acceptance pass when every chunk of an 'accepting' job is done. Returns
    /// null when nothing is claimable. The signature's now/tolerances are
    /// injected by the worker so the eligibility rules stay testable.
    /// </summary>
    Task<object?> ClaimNextWorkAsync(
        DateTimeOffset now, TimeSpan staleClaimAfter, int maxAttempts, CancellationToken ct);

    Task<byte[]?> LoadChunkRasterAsync(Guid chunkId, CancellationToken ct);

    Task HeartbeatChunkAsync(Guid chunkId, DateTimeOffset now, CancellationToken ct);

    Task HeartbeatAcceptAsync(Guid jobId, DateTimeOffset now, CancellationToken ct);

    /// <summary>Fails a chunk (worker-side segma error) and evaluates job failure.</summary>
    Task FailChunkAsync(Guid jobId, Guid chunkId, string error, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Records the claimed chunk as done, appends the segmentation draft ids to
    /// the job and advances its progress. Returns true when this was the last
    /// chunk (the job has transitioned to 'accepting', ready for an acceptance
    /// claim).
    /// </summary>
    Task<bool> CompleteChunkAsync(
        Guid jobId, Guid chunkId, IReadOnlyList<Guid> draftIds, DateTimeOffset now, CancellationToken ct);

    Task CompleteAcceptAsync(Guid jobId, JsonElement result, DateTimeOffset now, CancellationToken ct);

    Task FailAcceptAsync(Guid jobId, string error, DateTimeOffset now, CancellationToken ct);
}
