using System.Text.Json;
using Microsoft.Extensions.Options;
using NarsApi.DTOs;
using NarsApi.Infrastructure;

namespace NarsApi.Services;

/// <summary>
/// Persistent worker pool (one loop per <see cref="RoadGenerationJobOptions.WorkerCount"/>)
/// draining the generation_job queue. Each iteration claims exactly one unit of
/// work (a chunk to segment, or a completed job's acceptance pass) inside a
/// fresh request scope, runs the SAME services the synchronous endpoints use
/// (<see cref="IDraftFeaturesService.SegmentTileAsync"/> /
/// <see cref="IRoadGenerationService.GenerateAsync"/>), and marks progress.
/// Crashed mid-chunk work is reclaimed via stale heartbeats by the claim query;
/// the queue is the source of truth, so this service is fully restart-safe.
/// </summary>
public sealed class GenerationJobWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<RoadGenerationJobOptions> options,
    ILogger<GenerationJobWorker> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workerCount = Math.Clamp(options.Value.WorkerCount, 1, 64);
        var tasks = new Task[workerCount];
        for (var i = 0; i < workerCount; i++)
        {
            tasks[i] = RunWorkerAsync(i, stoppingToken);
        }

        await Task.WhenAll(tasks);
    }

    private async Task RunWorkerAsync(int workerIndex, CancellationToken stoppingToken)
    {
        logger.LogInformation("Generation job worker {Worker} started.", workerIndex);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var jobs = scope.ServiceProvider.GetRequiredService<IGenerationJobService>();
                var drafts = scope.ServiceProvider.GetRequiredService<IDraftFeaturesService>();
                var roads = scope.ServiceProvider.GetRequiredService<IRoadGenerationService>();
                var time = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

                var work = await jobs.ClaimNextWorkAsync(
                    time.UtcNow, options.Value.StaleClaimAfter, options.Value.MaxAttempts, stoppingToken);

                if (work is null)
                {
                    await Task.Delay(options.Value.ClaimPollIntervalMs, stoppingToken);
                    continue;
                }

                switch (work)
                {
                    case GenerationChunkWorkItem chunk:
                        await ProcessChunkAsync(jobs, drafts, time, chunk, stoppingToken);
                        break;
                    case GenerationAcceptWorkItem accept:
                        await ProcessAcceptAsync(jobs, roads, time, accept, stoppingToken);
                        break;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // shutting down
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Generation job worker {Worker} failed a poll iteration.", workerIndex);
                await Task.Delay(options.Value.ClaimPollIntervalMs, stoppingToken);
            }
        }

        logger.LogInformation("Generation job worker {Worker} stopped.", workerIndex);
    }

    private async Task ProcessChunkAsync(
        IGenerationJobService jobs,
        IDraftFeaturesService drafts,
        IDateTimeProvider time,
        GenerationChunkWorkItem work,
        CancellationToken stoppingToken)
    {
        using var heartbeats = CreateHeartbeatLoop(
            stoppingToken,
            options.Value.HeartbeatIntervalMs,
            logger,
            "chunk {ChunkKey} of job {JobId}",
            work.JobId,
            (_) => jobs.HeartbeatChunkAsync(work.ChunkId, time.UtcNow, stoppingToken));

        try
        {
            var raster = await jobs.LoadChunkRasterAsync(work.ChunkId, stoppingToken);
            if (raster is null || raster.Length == 0)
            {
                await jobs.FailChunkAsync(
                    work.JobId, work.ChunkId, "Raster missing from the queue.", time.UtcNow, stoppingToken);
                return;
            }

            await using var stream = new MemoryStream(raster, writable: false);
            var summary = await drafts.SegmentTileAsync(
                work.CallerRole, work.CallerCommuneId, work.CallerDairaId, work.CallerWilayaId,
                work.CommuneId, NarsApi.Models.AiDraftFeature.TypeRoad, stream,
                work.RasterFileName, work.RasterContentType, work.Bounds, stoppingToken);

            await jobs.CompleteChunkAsync(
                work.JobId, work.ChunkId, summary.DraftIds, time.UtcNow, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "Chunk {ChunkKey} of job {JobId} failed: {Message}.", work.ChunkKey, work.JobId, ex.Message);
            try
            {
                await jobs.FailChunkAsync(
                    work.JobId, work.ChunkId, ex.Message, time.UtcNow, stoppingToken);
            }
            catch (Exception inner) when (inner is not OperationCanceledException)
            {
                logger.LogError(inner, "Failed to record chunk failure for {ChunkKey}.", work.ChunkKey);
            }
        }
    }

    private async Task ProcessAcceptAsync(
        IGenerationJobService jobs,
        IRoadGenerationService roads,
        IDateTimeProvider time,
        GenerationAcceptWorkItem work,
        CancellationToken stoppingToken)
    {
        using var heartbeats = CreateHeartbeatLoop(
            stoppingToken,
            options.Value.HeartbeatIntervalMs,
            logger,
            "acceptance of job {JobId}",
            work.JobId,
            (_) => jobs.HeartbeatAcceptAsync(work.JobId, time.UtcNow, stoppingToken));

        try
        {
            var summary = await roads.GenerateAsync(
                work.CallerRole, work.CallerCommuneId, work.CallerDairaId, work.CallerWilayaId,
                work.CreatedBy, work.CommuneId, work.DraftIds, stoppingToken);

            var response = new GenerateRoadsResponse(
                summary.Dropped,
                summary.Created
                    .Select(road => new GeneratedRoadDto(
                        road.DbId, road.Layer, road.Label, JsonSerializer.SerializeToElement(road.Data)))
                    .ToList(),
                GenerateRoadsDroppedDto.From(summary.Breakdown));

            await jobs.CompleteAcceptAsync(
                work.JobId, JsonSerializer.SerializeToElement(response, SerializerOptions), time.UtcNow, stoppingToken);

            logger.LogInformation(
                "Generation job {JobId} done: {Dropped} dropped, {Created} roads created.",
                work.JobId, summary.Dropped, summary.Created.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Acceptance pass for job {JobId} failed: {Message}", work.JobId, ex.Message);
            try
            {
                await jobs.FailAcceptAsync(work.JobId, ex.Message, time.UtcNow, stoppingToken);
            }
            catch (Exception inner) when (inner is not OperationCanceledException)
            {
                logger.LogError(inner, "Failed to record acceptance failure for job {JobId}.", work.JobId);
            }
        }
    }

    private static IDisposable CreateHeartbeatLoop(
        CancellationToken stoppingToken,
        int intervalMilliseconds,
        ILogger logger,
        string workDescription,
        object? workId,
        Action<CancellationToken> beat)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var loop = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    beat(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break; // shutting down or the beat token was cancelled
                }
                catch (Exception ex)
                {
                    // Heartbeat failures are not fatal: stale claims will
                    // eventually reclaim a completed-but-un-heartbeated item.
                    logger.LogWarning(ex, "Heartbeat failed for {Work} {WorkId}.",
                        workDescription, workId);
                }

                try
                {
                    await Task.Delay(intervalMilliseconds, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, CancellationToken.None);

        return new HeartbeatScope(cts, loop);
    }

    private sealed class HeartbeatScope(CancellationTokenSource cts, Task loop) : IDisposable
    {
        public void Dispose()
        {
            cts.Cancel();
            try
            {
                loop.GetAwaiter().GetResult();
            }
            finally
            {
                cts.Dispose();
            }
        }
    }
}
