using Microsoft.EntityFrameworkCore;
using Moq;
using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests.Service;

/// <summary>
/// Integration coverage for the REAL draft-review transition. The InMemory
/// unit suite (DraftFeaturesTests) must substitute a tracked update for
/// ExecuteUpdateAsync; these tests run the production TryReviewDraftAsync
/// conditional UPDATE against PostgreSQL, including a concurrent double-review
/// race that must produce exactly one winner.
/// </summary>
[Collection(PostgreSqlCollection.CollectionName)]
[Trait("Category", "Service")]
public class DraftFeaturesServiceTests(NarsDatabaseFixture fixture) : IAsyncLifetime
{
    private readonly NarsDatabaseFixture _fixture = fixture;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _fixture.CleanTablesAsync();
    }

    private static DraftFeaturesService CreateService(AppDbContext db) =>
        new(db,
            Mock.Of<ISegmentationClient>(),
            new CommuneScopeService(db),
            Mock.Of<IDateTimeProvider>(x => x.UtcNow == FixedUtcNow));

    /// <summary>Own context per service: mirrors production (context per request).</summary>
    private async Task<DraftFeaturesService> CreateIsolatedServiceAsync()
    {
        var db = _fixture.CreateDbContext();
        try
        {
            return CreateService(db);
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }

    private static async Task<Guid> AddPendingDraftAsync(AppDbContext db, int communeId)
    {
        var draft = AiDraftFeature.Create(
            featureType: AiDraftFeature.TypeRoad,
            geometryGeoJson: """{"type":"LineString","coordinates":[[36.72,2.96],[36.73,2.97]]}""",
            confidence: 0.9,
            communeId: communeId,
            sourceTileRef: "tile.png",
            createdAt: FixedUtcNowOffset);
        db.AiDraftFeatures.Add(draft);
        await db.SaveChangesAsync();
        return draft.Id;
    }

    /// <summary>
    /// Seeds locations plus a real user row — drafts.reviews reference users(id)
    /// via ai_draft_features_reviewed_by_fk, so the reviewer must exist.
    /// </summary>
    private static async Task<(Guid ReviewerId, int CommuneId)> SeedReviewerAndCommuneAsync(
        AppDbContext db, int communeId)
    {
        await SeedData.SeedAdminLocationsAsync(db);
        var user = await SeedData.CreateUserAsync(db, UserRoles.CommuneUser);
        return (user.Id, communeId);
    }

    [Fact]
    public async Task AcceptDraft_RealConditionalUpdate_TransitionsAndStampsReviewer()
    {
        await using var seedDb = _fixture.CreateDbContext();
        var (reviewerId, _) = await SeedReviewerAndCommuneAsync(seedDb, CommuneId100);
        var draftId = await AddPendingDraftAsync(seedDb, CommuneId100);

        await using var svcDb = _fixture.CreateDbContext();
        var svc = CreateService(svcDb);

        var result = await svc.AcceptDraftAsync(
            UserRoles.NationalAdmin, null, null, null, reviewerId, draftId, default);

        Assert.Equal(DraftReviewStatus.Success, result.Status);

        await using var verifyDb = _fixture.CreateDbContext();
        var draft = await verifyDb.AiDraftFeatures.AsNoTracking()
            .SingleAsync(f => f.Id == draftId);
        Assert.Equal(AiDraftFeature.StatusAccepted, draft.Status);
        Assert.Equal(reviewerId, draft.ReviewedBy);
        Assert.NotNull(draft.ReviewedAt);
    }

    [Fact]
    public async Task RejectDraft_RealConditionalUpdate_TransitionsToRejected()
    {
        await using var seedDb = _fixture.CreateDbContext();
        var (reviewerId, _) = await SeedReviewerAndCommuneAsync(seedDb, CommuneId100);
        var draftId = await AddPendingDraftAsync(seedDb, CommuneId100);

        await using var svcDb = _fixture.CreateDbContext();
        var svc = CreateService(svcDb);

        var result = await svc.RejectDraftAsync(
            UserRoles.WilayaAdmin, null, null, WilayaId1, reviewerId, draftId, default);

        Assert.Equal(DraftReviewStatus.Success, result.Status);

        await using var verifyDb = _fixture.CreateDbContext();
        var draft = await verifyDb.AiDraftFeatures.AsNoTracking()
            .SingleAsync(f => f.Id == draftId);
        Assert.Equal(AiDraftFeature.StatusRejected, draft.Status);
        Assert.Equal(reviewerId, draft.ReviewedBy);
    }

    [Fact]
    public async Task AcceptDraft_SecondReview_ReturnsAlreadyReviewedAndKeepsFirstDecision()
    {
        await using var seedDb = _fixture.CreateDbContext();
        var (firstReviewerId, _) = await SeedReviewerAndCommuneAsync(seedDb, CommuneId100);
        var secondReviewer = await SeedData.CreateUserAsync(seedDb, UserRoles.CommuneUser);
        var draftId = await AddPendingDraftAsync(seedDb, CommuneId100);

        await using var firstDb = _fixture.CreateDbContext();
        await using var secondDb = _fixture.CreateDbContext();

        var first = await CreateService(firstDb).AcceptDraftAsync(
            UserRoles.NationalAdmin, null, null, null, firstReviewerId, draftId, default);
        var second = await CreateService(secondDb).AcceptDraftAsync(
            UserRoles.NationalAdmin, null, null, null, secondReviewer.Id, draftId, default);

        Assert.Equal(DraftReviewStatus.Success, first.Status);
        Assert.Equal(DraftReviewStatus.AlreadyReviewed, second.Status);

        // The loser must not overwrite the winner's decision.
        await using var verifyDb = _fixture.CreateDbContext();
        var draft = await verifyDb.AiDraftFeatures.AsNoTracking()
            .SingleAsync(f => f.Id == draftId);
        Assert.Equal(AiDraftFeature.StatusAccepted, draft.Status);
        Assert.Equal(firstReviewerId, draft.ReviewedBy);
    }

    [Fact]
    public async Task AcceptDraft_ConcurrentReviewers_ExactlyOneWins()
    {
        await using var seedDb = _fixture.CreateDbContext();
        var (reviewer1Seed, _) = await SeedReviewerAndCommuneAsync(seedDb, CommuneId100);
        var reviewer2 = await SeedData.CreateUserAsync(seedDb, UserRoles.CommuneUser);
        var reviewer2Id = reviewer2.Id;
        var draftId = await AddPendingDraftAsync(seedDb, CommuneId100);

        // Two independent service instances with their own contexts, racing the
        // same pending draft through the real conditional UPDATE.
        await using var db1 = _fixture.CreateDbContext();
        await using var db2 = _fixture.CreateDbContext();
        var svc1 = CreateService(db1);
        var svc2 = CreateService(db2);

        var results = await Task.WhenAll(
            svc1.AcceptDraftAsync(UserRoles.DairaAdmin, null, DairaId10, null, reviewer1Seed, draftId, default),
            svc2.AcceptDraftAsync(UserRoles.DairaAdmin, null, DairaId10, null, reviewer2Id, draftId, default));

        Assert.Single(results, r => r.Status == DraftReviewStatus.Success);
        Assert.Single(results, r => r.Status == DraftReviewStatus.AlreadyReviewed);

        await using var verifyDb = _fixture.CreateDbContext();
        var draft = await verifyDb.AiDraftFeatures.AsNoTracking()
            .SingleAsync(f => f.Id == draftId);
        Assert.Equal(AiDraftFeature.StatusAccepted, draft.Status);
        Assert.NotNull(draft.ReviewedBy);
        Assert.Contains(draft.ReviewedBy!.Value, new[] { reviewer1Seed, reviewer2Id });
    }

    [Fact]
    public async Task ReviewDraft_UnknownDraft_ReturnsNotFound()
    {
        await using var svcDb = _fixture.CreateDbContext();
        var svc = CreateService(svcDb);

        var result = await svc.AcceptDraftAsync(
            UserRoles.NationalAdmin, null, null, null, UserId, Guid.NewGuid(), default);

        Assert.Equal(DraftReviewStatus.NotFound, result.Status);
    }
}
