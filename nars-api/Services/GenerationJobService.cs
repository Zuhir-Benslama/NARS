using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

/// <summary>
/// Queue management for async road-generation runs. Creation/upload/read/cancel
/// are ordinary EF CRUD behind a commune-scope check. The claim surface
/// (<see cref="ClaimNextWorkAsync"/>) uses a single Postgres transaction with a
/// FOR UPDATE SKIP LOCKED SELECT so concurrent workers never double-claim;
/// claims are also where chunks transition to 'running' and heartbeat is
/// stamped, which makes crashed workers' chunks reclaimable after StaleClaimAfter.
/// </summary>
public sealed class GenerationJobService(
    IDbContextFactory<AppDbContext> dbFactory,
    ICommuneScopeService communeScope,
    IDateTimeProvider timeProvider) : IGenerationJobService
{
    // Validated against the same CHECK constraints the SQL migration defines.
    private const int MaxGridSize = 4096;
    private const int MaxChunkDimension = 4096;

    // ── User-facing lifecycle ─────────────────────────────────────────────────

    public async Task<GenerationJobView> CreateAsync(
        string callerRole, int? callerCommuneId, int? callerDairaId, int? callerWilayaId,
        Guid userId, int communeId, IReadOnlyList<GenerationGridDto> grids, CancellationToken ct)
    {
        if (grids.Count is 0 or > MaxGridSize)
        {
            throw new ArgumentException($"A job must contain between 1 and {MaxGridSize} chunks.", nameof(grids));
        }

        var seenKeys = new HashSet<string>(grids.Count, StringComparer.Ordinal);
        foreach (var grid in grids)
        {
            if (string.IsNullOrWhiteSpace(grid.ChunkKey) || grid.ChunkKey.Length > 40)
            {
                throw new ArgumentException("Each chunkKey must be between 1 and 40 characters.", nameof(grids));
            }

            if (!seenKeys.Add(grid.ChunkKey))
            {
                throw new ArgumentException($"Duplicate chunkKey '{grid.ChunkKey}'.", nameof(grids));
            }

            if (grid.Zoom is < 1 or > 30
                || grid.Width is <= 0 or > MaxChunkDimension
                || grid.Height is <= 0 or > MaxChunkDimension
                || grid.X0 < 0 || grid.Y0 < 0)
            {
                throw new ArgumentException($"Chunk '{grid.ChunkKey}' has an invalid grid.", nameof(grids));
            }

            if (grid.MinLon >= grid.MaxLon || grid.MinLat >= grid.MaxLat
                || grid.MinLon is < -180 or > 180 || grid.MaxLon is < -180 or > 180
                || grid.MinLat is < -90 or > 90 || grid.MaxLat is < -90 or > 90)
            {
                throw new ArgumentException($"Chunk '{grid.ChunkKey}' has invalid bounds.", nameof(grids));
            }
        }

        if (!await communeScope.CanAccessCommuneAsync(
                callerRole, callerCommuneId, callerDairaId, callerWilayaId, communeId, ct))
        {
            throw new UnauthorizedAccessException("You do not have access to this commune.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        _ = await db.Communes.FindAsync([communeId], ct)
            ?? throw new KeyNotFoundException($"Commune {communeId} not found");

        var now = timeProvider.UtcNow;
        var job = GenerationJob.Create(
            communeId, userId, callerRole, callerCommuneId, callerDairaId, callerWilayaId,
            grids.Count, now);

        db.GenerationJobs.Add(job);

        foreach (var grid in grids)
        {
            db.GenerationJobChunks.Add(GenerationJobChunk.Create(
                job.Id, grid.ChunkKey, grid.Zoom,
                (grid.X0, grid.Y0, grid.Width, grid.Height),
                (grid.MinLon, grid.MinLat, grid.MaxLon, grid.MaxLat),
                now));
        }

        await db.SaveChangesAsync(ct);

        return await BuildViewAsync(db, job.Id, ct);
    }

    public async Task<GenerationJobView> GetViewAsync(
        Guid jobId, string callerRole, int? callerCommuneId, int? callerDairaId,
        int? callerWilayaId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var job = await AssertCanViewAsync(db, jobId, callerRole, callerCommuneId, callerDairaId, callerWilayaId, ct);

        return await BuildViewAsync(db, job.Id, ct);
    }

    public async Task<GenerationJobView> UploadChunkRasterAsync(
        Guid jobId, Guid chunkId, string callerRole, int? callerCommuneId, int? callerDairaId,
        int? callerWilayaId, byte[] raster, string fileName, string contentType, CancellationToken ct)
    {
        if (raster.Length == 0)
        {
            throw new ArgumentException("Uploaded raster is empty.", nameof(raster));
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var job = await AssertCanViewAsync(db, jobId, callerRole, callerCommuneId, callerDairaId, callerWilayaId, ct);
        if (job.Status is GenerationJob.StatusCancelled or GenerationJob.StatusDone
            or GenerationJob.StatusFailed or GenerationJob.StatusAccepting)
        {
            // Accepting means all chunks are already processed — an upload
            // arriving after the acceptance claim is stale client behaviour.
            throw new InvalidOperationException($"Job {jobId} is {job.Status} and accepts no more rasters.");
        }

        var chunk = await db.GenerationJobChunks
            .SingleOrDefaultAsync(c => c.Id == chunkId && c.JobId == jobId, ct);
        if (chunk is null)
        {
            throw new KeyNotFoundException($"Chunk {chunkId} not found in job {jobId}.");
        }

        if (chunk.Status != GenerationJobChunk.StatusAwaitingRaster)
        {
            // Idempotency guard: the first upload wins, matching the queue's
            // at-most-once raster contract. A client retry must create a new job.
            throw new InvalidOperationException(
                $"Chunk {chunk.ChunkKey} is already {chunk.Status}; rasters cannot be replaced.");
        }

        var now = timeProvider.UtcNow;
        chunk.Raster = raster;
        chunk.RasterFileName = fileName;
        chunk.RasterContentType = contentType;
        chunk.Status = GenerationJobChunk.StatusReady;
        chunk.UpdatedAt = now;

        if (job.Status == GenerationJob.StatusPending)
        {
            job.Status = GenerationJob.StatusActive;
            job.Stage = GenerationJob.StageSegment;
            job.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);

        return await BuildViewAsync(db, jobId, ct);
    }

    public async Task<GenerationJobView> CancelAsync(
        Guid jobId, string callerRole, int? callerCommuneId, int? callerDairaId,
        int? callerWilayaId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var job = await AssertCanViewAsync(db, jobId, callerRole, callerCommuneId, callerDairaId, callerWilayaId, ct);
        if (job.Status is GenerationJob.StatusDone or GenerationJob.StatusFailed or GenerationJob.StatusCancelled)
        {
            return await BuildViewAsync(db, jobId, ct); // already terminal — nothing to cancel
        }

        var now = timeProvider.UtcNow;
        job.Status = GenerationJob.StatusCancelled;
        job.UpdatedAt = now;

        var stillOpen = await db.GenerationJobChunks
            .Where(c => c.JobId == jobId
                && c.Status != GenerationJobChunk.StatusDone
                && c.Status != GenerationJobChunk.StatusCancelled)
            .ToListAsync(ct);
        foreach (var chunk in stillOpen)
        {
            chunk.Status = GenerationJobChunk.StatusCancelled;
            chunk.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);

        return await BuildViewAsync(db, jobId, ct);
    }

    // ── Claim surface (worker pool) ───────────────────────────────────────────

    public async Task<object?> ClaimNextWorkAsync(
        DateTimeOffset now, TimeSpan staleClaimAfter, int maxAttempts, CancellationToken ct)
    {
        if (maxAttempts < 1)
        {
            return null;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        if (!db.Database.IsNpgsql())
        {
            // In-memory providers cannot run the FOR UPDATE SKIP LOCKED scan.
            // Non-Npgsql test hosts exercise lifecycle/CRUD; the real worker
            // only ever runs against Postgres (nars_db).
            return null;
        }

        // Chunk claims take priority: keep emptying the segment queues before
        // picking up acceptance passes (a job's own acceptance waits for its
        // last chunk, and different jobs proceed independently on the pool).
        var chunkId = await ClaimChunkIdAsync(db, now, staleClaimAfter, maxAttempts, ct);
        if (chunkId is not null)
        {
            return await CompleteChunkClaimAsync(db, chunkId.Value, now, maxAttempts, ct);
        }

        var jobId = await ClaimAcceptJobIdAsync(db, now, staleClaimAfter, ct);
        if (jobId is not null)
        {
            return await CompleteAcceptClaimAsync(db, jobId.Value, now, ct);
        }

        return null;
    }

    private static async Task<Guid?> ClaimChunkIdAsync(
        AppDbContext db, DateTimeOffset now, TimeSpan staleClaimAfter, int maxAttempts, CancellationToken ct)
    {
        var stale = now - staleClaimAfter;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var connection = db.Database.GetDbConnection();
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx.GetDbTransaction();
        cmd.CommandText = """
            SELECT c.id
            FROM generation_job_chunks AS c
            JOIN generation_jobs AS j ON j.id = c.job_id
            WHERE j.status IN ('pending', 'active')
              AND c.attempts < @max_attempts
              AND (
                    (c.status = 'ready')
                    OR (c.status = 'running' AND c.heartbeat_at < @stale)
                  )
            ORDER BY c.created_at
            FOR UPDATE SKIP LOCKED
            LIMIT 1
            """;
        SqlFragments.AddParam(cmd, "@max_attempts", maxAttempts);
        SqlFragments.AddParam(cmd, "@stale", stale);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            await tx.RollbackAsync(ct);
            return null;
        }

        var result = reader.GetGuid(0);
        await reader.DisposeAsync();
        await tx.CommitAsync(ct);
        return result;
    }

    private static async Task<Guid?> ClaimAcceptJobIdAsync(
        AppDbContext db, DateTimeOffset now, TimeSpan staleClaimAfter, CancellationToken ct)
    {
        var stale = now - staleClaimAfter;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var connection = db.Database.GetDbConnection();
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx.GetDbTransaction();
        cmd.CommandText = """
            SELECT j.id
            FROM generation_jobs AS j
            WHERE j.status = 'accepting'
              AND (j.accept_heartbeat_at IS NULL OR j.accept_heartbeat_at < @stale)
            ORDER BY j.updated_at
            FOR UPDATE SKIP LOCKED
            LIMIT 1
            """;
        SqlFragments.AddParam(cmd, "@stale", stale);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            await tx.RollbackAsync(ct);
            return null;
        }

        var result = reader.GetGuid(0);
        await reader.DisposeAsync();
        await tx.CommitAsync(ct);
        return result;
    }

    private static async Task<GenerationChunkWorkItem?> CompleteChunkClaimAsync(
        AppDbContext db, Guid chunkId, DateTimeOffset now, int maxAttempts, CancellationToken ct)
    {
        // The chunk row was locked by the claim SELECT; applying the running
        // transition and the exhaustion check in the same context keeps the
        // attempts budget exactly-once per claimed unit of work.
        var chunk = await db.GenerationJobChunks.SingleAsync(c => c.Id == chunkId, ct);

        var job = await db.GenerationJobs.SingleAsync(j => j.Id == chunk.JobId, ct);
        chunk.Attempts += 1;
        if (chunk.Attempts >= maxAttempts)
        {
            // Exhausted: fail the whole job; no work item is returned. Drafts
            // already segmented stay pending in the review queue.
            chunk.Status = GenerationJobChunk.StatusFailed;
            chunk.Error = $"Exhausted {maxAttempts} attempt{(maxAttempts == 1 ? string.Empty : "s")}.";
            job.Status = GenerationJob.StatusFailed;
            job.Error = chunk.Error;
            job.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            return null;
        }

        chunk.Status = GenerationJobChunk.StatusRunning;
        chunk.HeartbeatAt = now;
        chunk.UpdatedAt = now;

        if (job.Status == GenerationJob.StatusPending)
        {
            job.Status = GenerationJob.StatusActive;
            job.Stage = GenerationJob.StageSegment;
            job.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);

        return PromiseFor(chunk, job);
    }

    private static GenerationChunkWorkItem PromiseFor(GenerationJobChunk chunk, GenerationJob job)
    {
        // Work item carries enough to re-run the segmentation without another
        // round-trip: caller scope, bounds and the raster's identity. Bounds are
        // guaranteed present (create validates them).
        return new GenerationChunkWorkItem(
            job.Id, chunk.Id, chunk.ChunkKey, job.CallerRole, job.CallerCommuneId,
            job.CallerDairaId, job.CallerWilayaId, job.CommuneId,
            (chunk.MinLon!.Value, chunk.MinLat!.Value, chunk.MaxLon!.Value, chunk.MaxLat!.Value),
            chunk.RasterFileName ?? $"{chunk.ChunkKey}.jpg",
            chunk.RasterContentType);
    }

    private static async Task<GenerationAcceptWorkItem> CompleteAcceptClaimAsync(
        AppDbContext db, Guid jobId, DateTimeOffset now, CancellationToken ct)
    {
        var job = await db.GenerationJobs.SingleAsync(j => j.Id == jobId, ct);

        job.AcceptHeartbeatAt = now;
        job.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        return new GenerationAcceptWorkItem(
            job.Id, job.CreatedBy, job.CallerRole, job.CallerCommuneId,
            job.CallerDairaId, job.CallerWilayaId, job.CommuneId, job.DraftIds.ToList());
    }

    public async Task<byte[]?> LoadChunkRasterAsync(Guid chunkId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.GenerationJobChunks
            .Where(c => c.Id == chunkId)
            .Select(c => c.Raster)
            .SingleOrDefaultAsync(ct);
    }

    public async Task HeartbeatChunkAsync(Guid chunkId, DateTimeOffset now, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var chunk = await db.GenerationJobChunks.FindAsync([chunkId], ct);
        if (chunk is not null && chunk.Status == GenerationJobChunk.StatusRunning)
        {
            chunk.HeartbeatAt = now;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task HeartbeatAcceptAsync(Guid jobId, DateTimeOffset now, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var job = await db.GenerationJobs.FindAsync([jobId], ct);
        if (job is not null && job.Status == GenerationJob.StatusAccepting)
        {
            job.AcceptHeartbeatAt = now;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<bool> CompleteChunkAsync(
        Guid jobId, Guid chunkId, IReadOnlyList<Guid> draftIds, DateTimeOffset now, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var chunk = await db.GenerationJobChunks
            .SingleOrDefaultAsync(c => c.Id == chunkId && c.JobId == jobId, ct);
        if (chunk is null)
        {
            throw new KeyNotFoundException($"Chunk {chunkId} not found in job {jobId}.");
        }

        chunk.Status = GenerationJobChunk.StatusDone;
        chunk.HeartbeatAt = null;
        chunk.Error = null;
        chunk.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        // Recompute progress from actual counts so concurrent completions never
        // lose an update (each worker recomputes after its own commit).
        var job = await db.GenerationJobs.SingleAsync(j => j.Id == jobId, ct);
        job.SetProgress(await db.GenerationJobChunks.CountAsync(c => c.JobId == jobId && c.Status == GenerationJobChunk.StatusDone, ct), now);
        job.AddDraftIds(draftIds);

        if (job.DoneChunks == job.TotalChunks && job.Status != GenerationJob.StatusCancelled)
        {
            job.Status = GenerationJob.StatusAccepting;
            job.Stage = GenerationJob.StageAccept;
            job.AcceptHeartbeatAt = null; // re-claimable by any worker
            job.Error = null;
        }

        await db.SaveChangesAsync(ct);

        // True when this chunk completed the last work item and the job should
        // hand off to the acceptance pass shortly (claimable by any worker).
        return job.DoneChunks == job.TotalChunks && job.Status == GenerationJob.StatusAccepting;
    }

    public async Task FailChunkAsync(Guid jobId, Guid chunkId, string error, DateTimeOffset now, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var chunk = await db.GenerationJobChunks
            .SingleOrDefaultAsync(c => c.Id == chunkId && c.JobId == jobId, ct);
        if (chunk is null)
        {
            return;
        }

        chunk.Status = GenerationJobChunk.StatusFailed;
        chunk.HeartbeatAt = null;
        chunk.Error = error;
        chunk.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        await UpdateTerminalJobFailureAsync(db, jobId, now, error, ct);
    }

    private static async Task UpdateTerminalJobFailureAsync(
        AppDbContext db, Guid jobId, DateTimeOffset now, string error, CancellationToken ct)
    {
        // A job is failed when every chunk stopped being claimable and at least
        // one failed. Done-only jobs already transitioned to accepting.
        var remaining = await db.GenerationJobChunks.CountAsync(
            c => c.JobId == jobId
                && c.Status != GenerationJobChunk.StatusDone
                && c.Status != GenerationJobChunk.StatusFailed
                && c.Status != GenerationJobChunk.StatusCancelled, ct);
        if (remaining > 0)
        {
            return;
        }

        var failed = await db.GenerationJobChunks.AnyAsync(
            c => c.JobId == jobId && c.Status == GenerationJobChunk.StatusFailed, ct);
        if (!failed)
        {
            return;
        }

        var job = await db.GenerationJobs.SingleOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null || job.Status is GenerationJob.StatusCancelled or GenerationJob.StatusDone)
        {
            return;
        }

        job.Status = GenerationJob.StatusFailed;
        job.Error = error;
        job.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
    }

    public async Task CompleteAcceptAsync(Guid jobId, JsonElement result, DateTimeOffset now, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var job = await db.GenerationJobs.FindAsync([jobId], ct);
        if (job is null || job.Status != GenerationJob.StatusAccepting)
        {
            return;
        }

        job.Status = GenerationJob.StatusDone;
        job.Stage = null;
        job.Result = result;
        job.Error = null;
        job.AcceptHeartbeatAt = null;
        job.Progress = 1.0;
        job.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
    }

    public async Task FailAcceptAsync(Guid jobId, string error, DateTimeOffset now, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var job = await db.GenerationJobs.FindAsync([jobId], ct);
        if (job is null || job.Status != GenerationJob.StatusAccepting)
        {
            return;
        }

        job.Status = GenerationJob.StatusFailed;
        job.Error = error;
        job.AcceptHeartbeatAt = null;
        job.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
    }

    // ── View construction / scope ─────────────────────────────────────────────

    private async Task<GenerationJob> AssertCanViewAsync(
        AppDbContext db, Guid jobId, string callerRole, int? callerCommuneId,
        int? callerDairaId, int? callerWilayaId, CancellationToken ct)
    {
        var job = await db.GenerationJobs.FindAsync([jobId], ct)
            ?? throw new KeyNotFoundException($"Generation job {jobId} not found");

        if (!await communeScope.CanAccessCommuneAsync(
                callerRole, callerCommuneId, callerDairaId, callerWilayaId, job.CommuneId, ct))
        {
            throw new UnauthorizedAccessException("You do not have access to this commune.");
        }

        return job;
    }

    private static async Task<GenerationJobView> BuildViewAsync(
        AppDbContext db, Guid jobId, CancellationToken ct)
    {
        var job = await db.GenerationJobs.AsNoTracking().SingleAsync(j => j.Id == jobId, ct);

        var chunks = await db.GenerationJobChunks.AsNoTracking()
            .Where(c => c.JobId == jobId)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new GenerationChunkView(
                c.Id, c.ChunkKey, c.Zoom, c.X0, c.Y0, c.Width, c.Height,
                c.MinLon, c.MinLat, c.MaxLon, c.MaxLat, c.Status, c.Attempts, c.Error,
                c.CreatedAt, c.UpdatedAt))
            .ToListAsync(ct);

        return new GenerationJobView(
            job.Id, job.CommuneId, job.Status, job.Stage, job.TotalChunks, job.DoneChunks,
            job.Progress, job.DraftIds.ToList(), job.Error, job.CreatedAt, job.UpdatedAt,
            chunks, job.Result);
    }
}
