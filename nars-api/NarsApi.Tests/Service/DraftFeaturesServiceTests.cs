using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using System.Globalization;
using System.Text.Json;
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
public class DraftFeaturesServiceTests(NarsDatabaseFixture fixture) : ServiceTestBase(fixture)
{
    private static DraftFeaturesService CreateService(IDbContextFactory<AppDbContext> factory) =>
        new(factory,
            Mock.Of<ISegmentationClient>(),
            new CommuneScopeService(factory),
            Mock.Of<IDateTimeProvider>(x => x.UtcNow == FixedUtcNow),
            Options.Create(new RoadRulesOptions()),
            Options.Create(new BuildingRulesOptions()));

    /// <summary>Own context per service: mirrors production (context per request).</summary>
    private DraftFeaturesService CreateIsolatedService()
    {
        return CreateService(Fixture.CreateDbContextFactory());
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
        await using var seedDb = Fixture.CreateDbContext();
        var (reviewerId, _) = await SeedReviewerAndCommuneAsync(seedDb, CommuneId100);
        var draftId = await SeedData.AddDraftAsync(seedDb, CommuneId100);

        var svc = CreateService(Fixture.CreateDbContextFactory());

        var result = await svc.AcceptDraftAsync(
            UserRoles.NationalAdmin, null, null, null, reviewerId, draftId, default);

        Assert.Equal(DraftReviewStatus.Success, result.Status);

        await using var verifyDb = Fixture.CreateDbContext();
        var draft = await verifyDb.AiDraftFeatures.AsNoTracking()
            .SingleAsync(f => f.Id == draftId);
        Assert.Equal(AiDraftFeature.StatusAccepted, draft.Status);
        Assert.Equal(reviewerId, draft.ReviewedBy);
        Assert.NotNull(draft.ReviewedAt);
    }

    [Fact]
    public async Task RejectDraft_RealConditionalUpdate_TransitionsToRejected()
    {
        await using var seedDb = Fixture.CreateDbContext();
        var (reviewerId, _) = await SeedReviewerAndCommuneAsync(seedDb, CommuneId100);
        var draftId = await SeedData.AddDraftAsync(seedDb, CommuneId100);

        var svc = CreateService(Fixture.CreateDbContextFactory());

        var result = await svc.RejectDraftAsync(
            UserRoles.WilayaAdmin, null, null, WilayaId1, reviewerId, draftId, default);

        Assert.Equal(DraftReviewStatus.Success, result.Status);

        await using var verifyDb = Fixture.CreateDbContext();
        var draft = await verifyDb.AiDraftFeatures.AsNoTracking()
            .SingleAsync(f => f.Id == draftId);
        Assert.Equal(AiDraftFeature.StatusRejected, draft.Status);
        Assert.Equal(reviewerId, draft.ReviewedBy);
    }

    [Fact]
    public async Task AcceptDraft_SecondReview_ReturnsAlreadyReviewedAndKeepsFirstDecision()
    {
        await using var seedDb = Fixture.CreateDbContext();
        var (firstReviewerId, _) = await SeedReviewerAndCommuneAsync(seedDb, CommuneId100);
        var secondReviewer = await SeedData.CreateUserAsync(seedDb, UserRoles.CommuneUser);
        var draftId = await SeedData.AddDraftAsync(seedDb, CommuneId100);

        var factory1 = Fixture.CreateDbContextFactory();
        var factory2 = Fixture.CreateDbContextFactory();

        var first = await CreateService(factory1).AcceptDraftAsync(
            UserRoles.NationalAdmin, null, null, null, firstReviewerId, draftId, default);
        var second = await CreateService(factory2).AcceptDraftAsync(
            UserRoles.NationalAdmin, null, null, null, secondReviewer.Id, draftId, default);

        Assert.Equal(DraftReviewStatus.Success, first.Status);
        Assert.Equal(DraftReviewStatus.AlreadyReviewed, second.Status);

        // The loser must not overwrite the winner's decision.
        await using var verifyDb = Fixture.CreateDbContext();
        var draft = await verifyDb.AiDraftFeatures.AsNoTracking()
            .SingleAsync(f => f.Id == draftId);
        Assert.Equal(AiDraftFeature.StatusAccepted, draft.Status);
        Assert.Equal(firstReviewerId, draft.ReviewedBy);
    }

    [Fact]
    public async Task AcceptDraft_ConcurrentReviewers_ExactlyOneWins()
    {
        await using var seedDb = Fixture.CreateDbContext();
        var (reviewer1Seed, _) = await SeedReviewerAndCommuneAsync(seedDb, CommuneId100);
        var reviewer2 = await SeedData.CreateUserAsync(seedDb, UserRoles.CommuneUser);
        var reviewer2Id = reviewer2.Id;
        var draftId = await SeedData.AddDraftAsync(seedDb, CommuneId100);

        var factory1 = Fixture.CreateDbContextFactory();
        var factory2 = Fixture.CreateDbContextFactory();
        var svc1 = CreateService(factory1);
        var svc2 = CreateService(factory2);

        var results = await Task.WhenAll(
            svc1.AcceptDraftAsync(UserRoles.DairaAdmin, null, DairaId10, null, reviewer1Seed, draftId, default),
            svc2.AcceptDraftAsync(UserRoles.DairaAdmin, null, DairaId10, null, reviewer2Id, draftId, default));

        Assert.Single(results, r => r.Status == DraftReviewStatus.Success);
        Assert.Single(results, r => r.Status == DraftReviewStatus.AlreadyReviewed);

        await using var verifyDb = Fixture.CreateDbContext();
        var draft = await verifyDb.AiDraftFeatures.AsNoTracking()
            .SingleAsync(f => f.Id == draftId);
        Assert.Equal(AiDraftFeature.StatusAccepted, draft.Status);
        Assert.NotNull(draft.ReviewedBy);
        Assert.Contains(draft.ReviewedBy!.Value, new[] { reviewer1Seed, reviewer2Id });
    }

    [Fact]
    public async Task ReviewDraft_UnknownDraft_ReturnsNotFound()
    {
        var svc = CreateService(Fixture.CreateDbContextFactory());

        var result = await svc.AcceptDraftAsync(
            UserRoles.NationalAdmin, null, null, null, UserId, Guid.NewGuid(), default);

        Assert.Equal(DraftReviewStatus.NotFound, result.Status);
    }

    private const string HorizontalRoadData = """{"type":"road","label":"","coordinates":[{"lat":2.9600,"lng":36.7200},{"lat":2.9600,"lng":36.7240}]}""";

    private static string Ring(double lngC, double latC, double half = 0.0002)
    {
        var culture = CultureInfo.InvariantCulture;
        string p(double lng, double lat) => $"[{lng.ToString("R", culture)},{lat.ToString("R", culture)}]";
        var ring = $"{p(lngC - half, latC - half)},{p(lngC + half, latC - half)},{p(lngC + half, latC + half)},{p(lngC - half, latC + half)},{p(lngC - half, latC - half)}";
        return $"{{\"type\":\"Polygon\",\"coordinates\":[[{ring}]]}}";
    }

    /// <summary>Seeds a commune-100 road owner with a road plus a nearby building draft.</summary>
    private static async Task<(Guid RoadOwnerId, Guid RoadId, Guid DraftId)> SeedBuildingAsync(
        AppDbContext db, int communeId, string ring, string? secondRing = null)
    {
        await SeedData.SeedAdminLocationsAsync(db);
        var owner = await SeedData.CreateUserAsync(db, UserRoles.CommuneUser, communeId: communeId);
        var roadId = await TestData.AddRoadAsync(db, owner.Id, HorizontalRoadData);
        var draft = AiDraftFeature.Create(
            featureType: AiDraftFeature.TypeBuilding,
            geometryGeoJson: ring,
            confidence: 0.9,
            communeId: communeId,
            sourceTileRef: "tile.png",
            createdAt: FixedUtcNowOffset);
        db.AiDraftFeatures.Add(draft);
        if (secondRing is not null)
        {
            db.AiDraftFeatures.Add(AiDraftFeature.Create(
                featureType: AiDraftFeature.TypeBuilding,
                geometryGeoJson: secondRing,
                confidence: 0.9,
                communeId: communeId,
                sourceTileRef: "tile.png",
                createdAt: FixedUtcNowOffset));
        }

        await db.SaveChangesAsync();
        return (owner.Id, roadId, draft.Id);
    }

    /// <summary>
    /// Runs the REAL acceptance path end to end: transaction + raw row-lock
    /// numbering + conditional update. The accepted building must show up as a
    /// numbered house entrance owned by the road's owner.
    /// </summary>
    [Fact]
    public async Task AcceptBuildingDraft_RealPath_CreatesNumberedEntrance()
    {
        await using var seedDb = Fixture.CreateDbContext();
        var (roadOwnerId, roadId, draftId) = await SeedBuildingAsync(seedDb, CommuneId100, Ring(36.7220, 2.9603));
        var reviewer = await SeedData.CreateUserAsync(seedDb, UserRoles.CommuneUser);

        var svc = CreateService(Fixture.CreateDbContextFactory());

        var result = await svc.AcceptDraftAsync(
            UserRoles.NationalAdmin, null, null, null, reviewer.Id, draftId, default);

        Assert.Equal(DraftReviewStatus.Success, result.Status);

        await using var verifyDb = Fixture.CreateDbContext();
        var entrance = await verifyDb.HouseEntrances.AsNoTracking()
            .SingleAsync(e => e.RoadId == roadId);
        Assert.Equal(roadOwnerId, entrance.UserId);
        Assert.Equal(FeatureTypes.HouseEntranceLayers.Main, entrance.Layer);
        var data = JsonSerializer.Deserialize<JsonElement>(entrance.Data);
        Assert.Equal("houseEntrances", data.GetProperty("type").GetString());
        Assert.Equal("left", data.GetProperty("side").GetString());
        Assert.Equal(1, data.GetProperty("entranceNumber").GetInt32());
        Assert.Equal(roadId.ToString(), data.GetProperty("roadDbId").GetString());

        var reg = await verifyDb.FeatureRegistry.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == entrance.Id);
        Assert.NotNull(reg);

        var draft = await verifyDb.AiDraftFeatures.AsNoTracking().SingleAsync(f => f.Id == draftId);
        Assert.Equal(AiDraftFeature.StatusAccepted, draft.Status);
        Assert.Equal(reviewer.Id, draft.ReviewedBy);
    }

    /// <summary>
    /// Two concurrent accepts of different buildings on the same road must each
    /// serialize on the road row lock and get disjoint next-free numbers —
    /// never the same entranceNumber twice.
    /// </summary>
    [Fact]
    public async Task AcceptBuildingDraft_ConcurrentBuildingsOnSameRoad_GetDisjointNumbers()
    {
        await using var seedDb = Fixture.CreateDbContext();
        var (_, roadId, _) = await SeedBuildingAsync(
            seedDb, CommuneId100, Ring(36.7220, 2.9603), secondRing: Ring(36.7225, 2.9603));
        var reviewer1 = await SeedData.CreateUserAsync(seedDb, UserRoles.CommuneUser);
        var reviewer2 = await SeedData.CreateUserAsync(seedDb, UserRoles.CommuneUser);
        var draftIds = await seedDb.AiDraftFeatures.AsNoTracking()
            .Where(f => f.FeatureType == AiDraftFeature.TypeBuilding)
            .Select(f => f.Id)
            .ToListAsync();
        Assert.Equal(2, draftIds.Count);

        var svc1 = CreateService(Fixture.CreateDbContextFactory());
        var svc2 = CreateService(Fixture.CreateDbContextFactory());

        var results = await Task.WhenAll(
            svc1.AcceptDraftAsync(UserRoles.NationalAdmin, null, null, null, reviewer1.Id, draftIds[0], default),
            svc2.AcceptDraftAsync(UserRoles.NationalAdmin, null, null, null, reviewer2.Id, draftIds[1], default));

        Assert.All(results, r => Assert.Equal(DraftReviewStatus.Success, r.Status));

        await using var verifyDb = Fixture.CreateDbContext();
        var entrances = await verifyDb.HouseEntrances.AsNoTracking()
            .Where(e => e.RoadId == roadId)
            .ToListAsync();
        Assert.Equal(2, entrances.Count);

        var numbers = entrances
            .Select(e => JsonSerializer.Deserialize<JsonElement>(e.Data).GetProperty("entranceNumber").GetInt32())
            .Order()
            .ToList();
        // Both buildings sit on the left of the road → both odd, disjoint.
        Assert.Equal([1, 3], numbers);
    }
}
