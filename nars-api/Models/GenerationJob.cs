using System.Text.Json;

namespace NarsApi.Models;

/// <summary>
/// Maps to the generation_jobs table. One row per async "generate roads from
/// urban areas" run. Status constants mirror the SQL CHECK constraint values.
/// The chunk work items live in <see cref="GenerationJobChunk"/>; once every
/// chunk is processed the worker runs the roads-phase acceptance and stores
/// the GenerateRoadsResponse JSON in <see cref="Result"/>.
/// </summary>
public sealed class GenerationJob
{
    // Job lifecycle statuses.
    public const string StatusPending = "pending";     // created; chunks may not be uploaded yet
    public const string StatusActive = "active";       // at least one chunk is ready or in flight
    public const string StatusAccepting = "accepting"; // all chunks done; acceptance running (or reclaimable)
    public const string StatusDone = "done";           // acceptance finished; Result is set
    public const string StatusFailed = "failed";       // a chunk exhausted its attempt budget or acceptance threw
    public const string StatusCancelled = "cancelled"; // user cancelled; done chunks are kept, accept never runs

    // Active-phase stages.
    public const string StageSegment = "segment";
    public const string StageAccept = "accept";

    public Guid Id { get; internal set; }
    public int CommuneId { get; internal set; }
    public Guid CreatedBy { get; internal set; }
    public string Status { get; internal set; } = StatusPending;
    public string? Stage { get; internal set; }
    public int TotalChunks { get; internal set; }
    public int DoneChunks { get; internal set; }
    public double Progress { get; internal set; }
    public List<Guid> DraftIds { get; internal set; } = [];

    // Caller scope captured at creation so the worker can re-run segmentation
    // and the acceptance pass with the same authority the caller had.
    public string CallerRole { get; internal set; } = null!;
    public int? CallerCommuneId { get; internal set; }
    public int? CallerDairaId { get; internal set; }
    public int? CallerWilayaId { get; internal set; }

    public JsonElement? Result { get; internal set; }
    public string? Error { get; internal set; }
    public DateTimeOffset? AcceptHeartbeatAt { get; internal set; }
    public DateTimeOffset CreatedAt { get; internal set; }
    public DateTimeOffset UpdatedAt { get; internal set; }

    private GenerationJob() { } // EF Core

    public static GenerationJob Create(
        int communeId,
        Guid createdBy,
        string callerRole,
        int? callerCommuneId,
        int? callerDairaId,
        int? callerWilayaId,
        int totalChunks,
        DateTimeOffset createdAt)
    {
        return new GenerationJob
        {
            Id = Guid.CreateVersion7(),
            CommuneId = communeId,
            CreatedBy = createdBy,
            CallerRole = callerRole,
            CallerCommuneId = callerCommuneId,
            CallerDairaId = callerDairaId,
            CallerWilayaId = callerWilayaId,
            TotalChunks = totalChunks,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            Status = StatusPending,
            Stage = StageSegment,
        };
    }

    /// <summary>
    /// Appends segmented draft ids for each finished chunk. The property is a
    /// value-converted JSONB column, so the list MUST be replaced (not
    /// mutated) for EF change tracking to notice the update.
    /// </summary>
    public void AddDraftIds(IEnumerable<Guid> ids)
    {
        var merged = new List<Guid>(DraftIds.Count + ids.Count());
        merged.AddRange(DraftIds);
        merged.AddRange(ids);
        DraftIds = merged;
    }

    public void SetProgress(int doneChunks, DateTimeOffset now)
    {
        DoneChunks = doneChunks;
        Progress = TotalChunks == 0 ? 0.0 : (double)doneChunks / TotalChunks;
        UpdatedAt = now;
    }
}
