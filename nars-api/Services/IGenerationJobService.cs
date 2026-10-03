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

    /// <summary>
    /// Run the districts phase for a job whose roads acceptance completed and
    /// which asked for districts (job.GenerateDistricts).
    /// </summary>
    Districts,
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
/// A claimed districts pass: the roads acceptance is done and the job was
/// created with GenerateDistricts, so the worker runs
/// IDistrictGenerationService.GenerateAsync over the job's commune with the
/// job creator's scope. Like the acceptance pass it is reclaimable when
/// <c>accept_heartbeat_at</c> goes stale.
/// </summary>
public sealed record GenerationDistrictsWorkItem(
    Guid JobId,
    string CallerRole,
    int? CallerCommuneId,
    int? CallerDairaId,
    int? CallerWilayaId,
    int CommuneId);

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
        Guid userId, int communeId, IReadOnlyList<GenerationGridDto> grids,
        bool generateDistricts = false, CancellationToken ct = default);

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
    /// acceptance pass when every chunk of an 'accepting' job is done, then a
    /// districts pass for an 'active' job sitting in the districts stage.
    /// Returns null when nothing is claimable. The signature's
    /// now/tolerances are injected by the worker so the eligibility rules stay
    /// testable.
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

    /// <summary>
    /// Records the acceptance result. A job created with GenerateDistricts does
    /// not finish here: it advances to the districts stage so the worker picks
    /// it up for one more pass.
    /// </summary>
    Task CompleteAcceptAsync(Guid jobId, JsonElement result, DateTimeOffset now, CancellationToken ct);

    Task FailAcceptAsync(Guid jobId, string error, DateTimeOffset now, CancellationToken ct);

    /// <summary>Finishes a job whose districts phase completed.</summary>
    Task CompleteDistrictsAsync(Guid jobId, JsonElement result, DateTimeOffset now, CancellationToken ct);

    Task FailDistrictsAsync(Guid jobId, string error, DateTimeOffset now, CancellationToken ct);
}
