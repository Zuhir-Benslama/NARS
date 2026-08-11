using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests;

public class DraftFeaturesServiceTests
{
    private static DraftFeaturesService CreateService(AppDbContext db, ISegmentationClient? segmentationClient = null) =>
        new(
            db,
            segmentationClient ?? Mock.Of<ISegmentationClient>(),
            new CommuneScopeService(db),
            Mock.Of<IDateTimeProvider>(x => x.UtcNow == FixedUtcNow));

    private static async Task SeedAsync(AppDbContext db)
    {
        await SeedData.SeedAdminLocationsAsync(db);
    }

    private static async Task<Guid> AddDraftAsync(AppDbContext db, int communeId)
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

    [Fact]
    public async Task ListDrafts_OutOfScopeCommune_ThrowsUnauthorized()
    {
        using var db = CreateInMemoryDb("DraftsListOutOfScope");
        await SeedAsync(db);
        var svc = CreateService(db);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            svc.ListDraftsAsync(UserRoles.FieldWorker, CommuneId100, null, null, CommuneId101, null, AiDraftFeature.StatusPending, default));
    }

    [Fact]
    public async Task ListDrafts_InScopeCommune_ReturnsDrafts()
    {
        using var db = CreateInMemoryDb("DraftsListInScope");
        await SeedAsync(db);
        var draftId = await AddDraftAsync(db, CommuneId100);
        var svc = CreateService(db);

        var drafts = await svc.ListDraftsAsync(UserRoles.FieldWorker, CommuneId100, null, null, CommuneId100, null, AiDraftFeature.StatusPending, default);

        var draft = Assert.Single(drafts);
        Assert.Equal(draftId, draft.Id);
    }

    [Fact]
    public async Task SegmentTile_OutOfScopeCommune_ThrowsUnauthorized()
    {
        using var db = CreateInMemoryDb("DraftsSegmentOutOfScope");
        await SeedAsync(db);
        var svc = CreateService(db);
        using var stream = new MemoryStream([1, 2, 3]);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            svc.SegmentTileAsync(UserRoles.CommuneUser, CommuneId100, null, null, CommuneId101,
                stream, "tile.png", "image/png", (1, 1, 2, 2), default));
    }

    [Fact]
    public async Task SegmentTile_UnknownCommune_ThrowsKeyNotFound()
    {
        using var db = CreateInMemoryDb("DraftsSegmentUnknownCommune");
        await SeedAsync(db);
        var svc = CreateService(db);
        using var stream = new MemoryStream([1, 2, 3]);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            svc.SegmentTileAsync(UserRoles.NationalAdmin, null, null, null, NonExistentId,
                stream, "tile.png", "image/png", (1, 1, 2, 2), default));
    }

    [Fact]
    public async Task SegmentTile_InScopeCommune_PersistsDrafts()
    {
        using var db = CreateInMemoryDb("DraftsSegmentInScope");
        await SeedAsync(db);
        var segmentation = new Mock<ISegmentationClient>();
        segmentation.Setup(s => s.SegmentTileAsync(It.IsAny<Stream>(), "tile.png", "image/png", It.IsAny<(double, double, double, double)>(), default))
            .ReturnsAsync(new SegmentationResult(
                Roads: [new SegmentedFeature("""{"type":"LineString"}""", 0.8, AiDraftFeature.TypeRoad)],
                Buildings: []));
        var svc = CreateService(db, segmentation.Object);
        using var stream = new MemoryStream([1, 2, 3]);

        var summary = await svc.SegmentTileAsync(UserRoles.NationalAdmin, null, null, null, CommuneId100,
            stream, "tile.png", "image/png", (1.0, 1.0, 2.0, 2.0), default);

        Assert.Equal(1, summary.RoadCount);
        Assert.Equal(0, summary.BuildingCount);
        Assert.Single(summary.DraftIds);
        var saved = await db.AiDraftFeatures.ToListAsync();
        Assert.Single(saved);
        Assert.Equal(CommuneId100, saved[0].CommuneId);
    }

    [Fact]
    public async Task AcceptDraft_OutOfScopeCommune_ReturnsForbidden()
    {
        using var db = CreateInMemoryDb("DraftsAcceptOutOfScope");
        await SeedAsync(db);
        var draftId = await AddDraftAsync(db, CommuneId101);
        var svc = CreateService(db);

        var result = await svc.AcceptDraftAsync(UserRoles.FieldWorker, CommuneId100, null, null, UserId, draftId, default);

        Assert.Equal(DraftReviewStatus.Forbidden, result.Status);
    }

    [Fact]
    public async Task AcceptDraft_InScopeCommune_Succeeds()
    {
        using var db = CreateInMemoryDb("DraftsAcceptInScope");
        await SeedAsync(db);
        var draftId = await AddDraftAsync(db, CommuneId100);
        var svc = CreateService(db);

        var result = await svc.AcceptDraftAsync(UserRoles.NationalAdmin, null, null, null, UserId, draftId, default);

        Assert.Equal(DraftReviewStatus.Success, result.Status);
        var draft = await db.AiDraftFeatures.FindAsync(draftId);
        Assert.Equal(AiDraftFeature.StatusAccepted, draft!.Status);
        Assert.Equal(UserId, draft.ReviewedBy);
    }

    [Fact]
    public async Task RejectDraft_InScopeCommune_Succeeds()
    {
        using var db = CreateInMemoryDb("DraftsRejectInScope");
        await SeedAsync(db);
        var draftId = await AddDraftAsync(db, CommuneId100);
        var svc = CreateService(db);

        var result = await svc.RejectDraftAsync(UserRoles.WilayaAdmin, null, null, WilayaId1, UserId, draftId, default);

        Assert.Equal(DraftReviewStatus.Success, result.Status);
        var draft = await db.AiDraftFeatures.FindAsync(draftId);
        Assert.Equal(AiDraftFeature.StatusRejected, draft!.Status);
    }

    [Fact]
    public async Task AcceptDraft_UnknownDraft_ReturnsNotFound()
    {
        using var db = CreateInMemoryDb("DraftsAcceptUnknown");
        await SeedAsync(db);
        var svc = CreateService(db);

        var result = await svc.AcceptDraftAsync(UserRoles.NationalAdmin, null, null, null, UserId, Guid.NewGuid(), default);

        Assert.Equal(DraftReviewStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task AcceptDraft_AlreadyReviewed_ReturnsAlreadyReviewed()
    {
        using var db = CreateInMemoryDb("DraftsAcceptTwice");
        await SeedAsync(db);
        var draftId = await AddDraftAsync(db, CommuneId100);
        var svc = CreateService(db);

        await svc.AcceptDraftAsync(UserRoles.NationalAdmin, null, null, null, UserId, draftId, default);
        var result = await svc.AcceptDraftAsync(UserRoles.NationalAdmin, null, null, null, UserId, draftId, default);

        Assert.Equal(DraftReviewStatus.AlreadyReviewed, result.Status);
    }
}
