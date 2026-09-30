using System.Text.Json;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using Xunit;

namespace NarsApi.Tests;

public class GenerationJobServiceTests
{
    private static readonly DateTimeOffset FixedUtcNow = DateTimeOffset.UtcNow;

    private sealed class FixedTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow => FixedUtcNow.UtcDateTime;
    }

    // The factory opens a FRESH context per call against the same in-memory db,
    // mirroring how the service runs against Postgres (scoped contexts).
    private static async Task<GenerationJobService> CreateServiceAsync()
    {
        var (db, factory) = TestData.CreateInMemoryDbPair("generationjob");
        await SeedData.SeedAdminLocationsAsync(db);
        return new GenerationJobService(factory, new CommuneScopeService(factory), new FixedTimeProvider());
    }

    private static GenerationGridDto Grid(string key)
        => new(key, 18, 4200, 3400, 512, 512, 2.94, 36.70, 2.95, 36.71);

    private static readonly Guid OwnerId = Guid.CreateVersion7();

    [Fact]
    public async Task CreateAsync_CreatesJobWithChunks()
    {
        var service = await CreateServiceAsync();

        var view = await service.CreateAsync(
            UserRoles.NationalAdmin, null, null, null, OwnerId, TestData.CommuneId100,
            [Grid("0,0"), Grid("0,1")], CancellationToken.None);

        Assert.Equal(TestData.CommuneId100, view.CommuneId);
        Assert.Equal(GenerationJob.StatusPending, view.Status);
        Assert.Equal(GenerationJob.StageSegment, view.Stage);
        Assert.Equal(2, view.TotalChunks);
        Assert.Equal(0, view.DoneChunks);
        Assert.Empty(view.DraftIds);
        Assert.Equal(2, view.Chunks.Count);
        Assert.All(view.Chunks, c => Assert.Equal(GenerationJobChunk.StatusAwaitingRaster, c.Status));
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateChunkKeys()
    {
        var service = await CreateServiceAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(
            UserRoles.NationalAdmin, null, null, null, OwnerId, TestData.CommuneId100,
            [Grid("0,0"), Grid("0,0")], CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_UnknownCommune_ThrowsNotFound()
    {
        var service = await CreateServiceAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CreateAsync(
            UserRoles.NationalAdmin, null, null, null, OwnerId, TestData.NonExistentId,
            [Grid("0,0")], CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_CallerWithoutCommuneScope_ThrowsForbidden()
    {
        var service = await CreateServiceAsync();

        // Field worker scoped to commune 101 cannot start a generation run for 100.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateAsync(
            UserRoles.FieldWorker, TestData.CommuneId101, null, null, OwnerId, TestData.CommuneId100,
            [Grid("0,0")], CancellationToken.None));
    }

    [Fact]
    public async Task UploadChunkRaster_ActivatesJobAndMarksChunkReady()
    {
        var service = await CreateServiceAsync();

        var created = await service.CreateAsync(
            UserRoles.NationalAdmin, null, null, null, OwnerId, TestData.CommuneId100, [Grid("0,0")], CancellationToken.None);
        var chunk = created.Chunks.Single();

        var view = await service.UploadChunkRasterAsync(
            created.Id, chunk.Id, UserRoles.NationalAdmin, null, null, null,
            [1, 2, 3], "0_0.jpg", "image/jpeg", CancellationToken.None);

        Assert.Equal(GenerationJob.StatusActive, view.Status);
        Assert.Equal(GenerationJobChunk.StatusReady, view.Chunks.Single().Status);
    }

    [Fact]
    public async Task UploadChunkRaster_SecondUploadForSameChunk_Rejected()
    {
        var service = await CreateServiceAsync();

        var created = await service.CreateAsync(
            UserRoles.NationalAdmin, null, null, null, OwnerId, TestData.CommuneId100, [Grid("0,0")], CancellationToken.None);
        var chunk = created.Chunks.Single();

        await service.UploadChunkRasterAsync(
            created.Id, chunk.Id, UserRoles.NationalAdmin, null, null, null,
            [1, 2, 3], "0_0.jpg", "image/jpeg", CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UploadChunkRasterAsync(
            created.Id, chunk.Id, UserRoles.NationalAdmin, null, null, null,
            [4, 5], "0_0.jpg", "image/jpeg", CancellationToken.None));
    }

    [Fact]
    public async Task UploadChunkRaster_UnknownChunk_ThrowsNotFound()
    {
        var service = await CreateServiceAsync();

        var created = await service.CreateAsync(
            UserRoles.NationalAdmin, null, null, null, OwnerId, TestData.CommuneId100, [Grid("0,0")], CancellationToken.None);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.UploadChunkRasterAsync(
            created.Id, Guid.CreateVersion7(), UserRoles.NationalAdmin, null, null, null,
            [1], "x.jpg", "image/jpeg", CancellationToken.None));
    }

    [Fact]
    public async Task CompleteChunkAsync_AccumulatesDraftsAndProgressesToAccepting()
    {
        var service = await CreateServiceAsync();

        var created = await service.CreateAsync(
            UserRoles.NationalAdmin, null, null, null, OwnerId, TestData.CommuneId100,
            [Grid("0,0"), Grid("0,1")], CancellationToken.None);
        var chunkA = created.Chunks[0];
        var chunkB = created.Chunks[1];

        var notLast = await service.CompleteChunkAsync(
            created.Id, chunkA.Id, [Guid.CreateVersion7(), Guid.CreateVersion7()], FixedUtcNow, CancellationToken.None);
        var last = await service.CompleteChunkAsync(
            created.Id, chunkB.Id, [Guid.CreateVersion7()], FixedUtcNow, CancellationToken.None);

        Assert.False(notLast);
        Assert.True(last);

        var view = await service.GetViewAsync(
            created.Id, UserRoles.NationalAdmin, null, null, null, CancellationToken.None);
        Assert.Equal(GenerationJob.StatusAccepting, view.Status);
        Assert.Equal(GenerationJob.StageAccept, view.Stage);
        Assert.Equal(1.0, view.Progress);
        Assert.Equal(2, view.DoneChunks);
        Assert.Equal(3, view.DraftIds.Count);
        Assert.All(view.Chunks, c => Assert.Equal(GenerationJobChunk.StatusDone, c.Status));
    }

    [Fact]
    public async Task CompleteAcceptAsync_SetsDoneWithResult()
    {
        var service = await CreateServiceAsync();

        var created = await service.CreateAsync(
            UserRoles.NationalAdmin, null, null, null, OwnerId, TestData.CommuneId100, [Grid("0,0")], CancellationToken.None);
        var chunk = created.Chunks.Single();
        await service.CompleteChunkAsync(created.Id, chunk.Id, [Guid.CreateVersion7()], FixedUtcNow, CancellationToken.None);

        var result = JsonSerializer.SerializeToElement("""{"dropped":0,"created":[],"breakdown":{}}""", new JsonSerializerOptions());
        await service.CompleteAcceptAsync(created.Id, result, FixedUtcNow, CancellationToken.None);

        var view = await service.GetViewAsync(
            created.Id, UserRoles.NationalAdmin, null, null, null, CancellationToken.None);
        Assert.Equal(GenerationJob.StatusDone, view.Status);
        Assert.True(view.Result.HasValue);
    }

    [Fact]
    public async Task CancelAsync_CancelsOpenChunksAndKeepsDoneOnes()
    {
        var service = await CreateServiceAsync();

        var created = await service.CreateAsync(
            UserRoles.NationalAdmin, null, null, null, OwnerId, TestData.CommuneId100,
            [Grid("0,0"), Grid("0,1")], CancellationToken.None);
        await service.UploadChunkRasterAsync(
            created.Id, created.Chunks[0].Id, UserRoles.NationalAdmin, null, null, null,
            [1], "a.jpg", "image/jpeg", CancellationToken.None);
        await service.CompleteChunkAsync(created.Id, created.Chunks[0].Id, [Guid.CreateVersion7()], FixedUtcNow, CancellationToken.None);

        var view = await service.CancelAsync(
            created.Id, UserRoles.NationalAdmin, null, null, null, CancellationToken.None);

        Assert.Equal(GenerationJob.StatusCancelled, view.Status);
        Assert.Equal(GenerationJobChunk.StatusDone, view.Chunks[0].Status);
        Assert.Equal(GenerationJobChunk.StatusCancelled, view.Chunks[1].Status);
    }

    [Fact]
    public async Task GetViewAsync_CallerWithoutCommuneScope_ThrowsForbidden()
    {
        var service = await CreateServiceAsync();

        var created = await service.CreateAsync(
            UserRoles.NationalAdmin, null, null, null, OwnerId, TestData.CommuneId100, [Grid("0,0")], CancellationToken.None);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetViewAsync(
            created.Id, UserRoles.FieldWorker, TestData.CommuneId101, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task GetViewAsync_UnknownJob_ThrowsNotFound()
    {
        var service = await CreateServiceAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetViewAsync(
            Guid.CreateVersion7(), UserRoles.NationalAdmin, null, null, null, CancellationToken.None));
    }
}
