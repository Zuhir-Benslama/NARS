using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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

namespace NarsApi.Tests;

public class DraftFeaturesUnitTests
{
    /// <summary>
    /// A testable subclass that replaces the PostgreSQL-specific conditional
    /// ExecuteUpdateAsync with an equivalent tracked update usable with the
    /// InMemory provider (which does not implement ExecuteUpdateAsync) and the
    /// building-accept transaction with a context-local equivalent. The
    /// production transition itself is exercised against PostgreSQL in
    /// Service/DraftFeaturesServiceTests.
    /// </summary>
    private sealed class TestableDraftFeaturesService(
        IDbContextFactory<AppDbContext> dbFactory,
        ISegmentationClient segmentationClient,
        IDateTimeProvider timeProvider,
        RoadRulesOptions? roadRules = null,
        BuildingRulesOptions? buildingRules = null) : DraftFeaturesService(dbFactory, segmentationClient, new CommuneScopeService(dbFactory), timeProvider, Options.Create(roadRules ?? new RoadRulesOptions()), Options.Create(buildingRules ?? new BuildingRulesOptions()))
    {
        protected override async Task<int> TryReviewDraftAsync(
            AppDbContext db, Guid draftId, string newStatus, Guid reviewedBy, DateTimeOffset reviewedAt, CancellationToken ct)
        {
            var draft = await db.AiDraftFeatures.FirstOrDefaultAsync(
                f => f.Id == draftId && f.Status == AiDraftFeature.StatusPending, ct);
            if (draft is null)
            {
                return 0;
            }

            var entry = db.Entry(draft);
            entry.Property(f => f.Status).CurrentValue = newStatus;
            entry.Property(f => f.ReviewedBy).CurrentValue = reviewedBy;
            entry.Property(f => f.ReviewedAt).CurrentValue = reviewedAt;
            await db.SaveChangesAsync(ct);
            return 1;
        }

        // InMemory cannot BeginTransactionAsync; run the shared acceptance core
        // directly on the tracked context.
        protected override Task<DraftReviewResult> AcceptBuildingDraftAsync(
            AppDbContext db, AiDraftFeature draft, Guid userId, CancellationToken ct)
            => AcceptBuildingDraftCoreAsync(db, draft, userId, ct);

        // EF query stands in for the raw FOR UPDATE lock / data read.
        protected override async Task<int?> NextEntranceNumberAsync(
            AppDbContext db, Guid roadOwnerId, Guid roadId, string side, CancellationToken ct)
        {
            var rows = await db.HouseEntrances
                .AsNoTracking()
                .Where(e => e.UserId == roadOwnerId && e.RoadId == roadId && e.Layer == FeatureTypes.HouseEntranceLayers.Main)
                .Select(e => e.Data)
                .ToListAsync(ct);

            var usedNumbers = new HashSet<int>();
            foreach (var data in rows)
            {
                if (TryReadEntranceNumber(data, side, out var num))
                {
                    usedNumbers.Add(num);
                }
            }

            var suggested = GeometryHelper.SuggestEntranceNumber(side, usedNumbers);
            return suggested < 0 ? null : suggested;
        }
    }

    private static DraftFeaturesService CreateService(
        AppDbContext db,
        ISegmentationClient? segmentationClient = null,
        IDbContextFactory<AppDbContext>? factory = null,
        RoadRulesOptions? roadRules = null,
        BuildingRulesOptions? buildingRules = null) =>
        new TestableDraftFeaturesService(
            factory ?? new TestDbContextFactory(db),
            segmentationClient ?? Mock.Of<ISegmentationClient>(),
            Mock.Of<IDateTimeProvider>(x => x.UtcNow == FixedUtcNow),
            roadRules,
            buildingRules);

    private static async Task SeedAsync(AppDbContext db)
    {
        await SeedData.SeedAdminLocationsAsync(db);
    }

    [Fact]
    public async Task ListDrafts_OutOfScopeCommune_ThrowsUnauthorized()
    {
        var (db, factory) = CreateInMemoryDbPair("DraftsListOutOfScope");
        await using (db)
        {
            await SeedAsync(db);
            var svc = CreateService(db, factory: factory);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                svc.ListDraftsAsync(UserRoles.FieldWorker, CommuneId100, null, null, CommuneId101, null, AiDraftFeature.StatusPending, default));
        }
    }

    [Fact]
    public async Task ListDrafts_InScopeCommune_ReturnsDrafts()
    {
        var (db, factory) = CreateInMemoryDbPair("DraftsListInScope");
        await using (db)
        {
            await SeedAsync(db);
            var draftId = await SeedData.AddDraftAsync(db, CommuneId100);
            var svc = CreateService(db, factory: factory);

            var drafts = await svc.ListDraftsAsync(UserRoles.FieldWorker, CommuneId100, null, null, CommuneId100, null, AiDraftFeature.StatusPending, default);

            var draft = Assert.Single(drafts.Items);
            Assert.Equal(draftId, draft.Id);
        }
    }

    [Fact]
    public async Task SegmentTile_OutOfScopeCommune_ThrowsUnauthorized()
    {
        var (db, factory) = CreateInMemoryDbPair("DraftsSegmentOutOfScope");
        await using (db)
        {
            await SeedAsync(db);
            var svc = CreateService(db, factory: factory);
            using var stream = new MemoryStream([1, 2, 3]);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                svc.SegmentTileAsync(UserRoles.CommuneUser, CommuneId100, null, null, CommuneId101,
                    AiDraftFeature.TypeBuilding, stream, "tile.png", "image/png", (1, 1, 2, 2), default));
        }
    }

    [Fact]
    public async Task SegmentTile_UnknownCommune_ThrowsKeyNotFound()
    {
        var (db, factory) = CreateInMemoryDbPair("DraftsSegmentUnknownCommune");
        await using (db)
        {
            await SeedAsync(db);
            var svc = CreateService(db, factory: factory);
            using var stream = new MemoryStream([1, 2, 3]);

            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                svc.SegmentTileAsync(UserRoles.NationalAdmin, null, null, null, NonExistentId,
                    AiDraftFeature.TypeBuilding, stream, "tile.png", "image/png", (1, 1, 2, 2), default));
        }
    }

    [Fact]
    public async Task SegmentTile_Buildings_InScopeCommune_PersistsDrafts()
    {
        var (db, factory) = CreateInMemoryDbPair("DraftsSegmentInScope");
        await using (db)
        {
            await SeedAsync(db);
            var segmentation = new Mock<ISegmentationClient>();
            segmentation.Setup(s => s.SegmentTileAsync(
                    It.IsAny<string>(), It.IsAny<Stream>(), "tile.png", "image/png",
                    It.IsAny<(double, double, double, double)>(), default))
                .ReturnsAsync(new SegmentationResult
                {
                    Buildings = [new SegmentedFeature("""{"type":"Polygon"}""", 0.8, AiDraftFeature.TypeBuilding)],
                });
            var svc = CreateService(db, segmentation.Object, factory);
            using var stream = new MemoryStream([1, 2, 3]);

            var summary = await svc.SegmentTileAsync(UserRoles.NationalAdmin, null, null, null, CommuneId100,
                AiDraftFeature.TypeBuilding, stream, "tile.png", "image/png", (1.0, 1.0, 2.0, 2.0), default);

            Assert.Equal(1, summary.BuildingCount);
            Assert.Equal(0, summary.RoadCount);
            Assert.Single(summary.DraftIds);
            var saved = await db.AiDraftFeatures.ToListAsync();
            Assert.Single(saved);
            Assert.Equal(CommuneId100, saved[0].CommuneId);
            Assert.Equal(AiDraftFeature.TypeBuilding, saved[0].FeatureType);
        }
    }

    [Fact]
    public async Task SegmentTile_Roads_InScopeCommune_PersistsDraftsAsRoads()
    {
        var (db, factory) = CreateInMemoryDbPair("DraftsSegmentRoads");
        await using (db)
        {
            await SeedAsync(db);
            var segmentation = new Mock<ISegmentationClient>();
            segmentation.Setup(s => s.SegmentTileAsync(
                    It.IsAny<string>(), It.IsAny<Stream>(), "tile.png", "image/png",
                    It.IsAny<(double, double, double, double)>(), default))
                .ReturnsAsync(new SegmentationResult
                {
                    Roads = [new SegmentedFeature("""{"type":"LineString"}""", 0.9, AiDraftFeature.TypeRoad)],
                });
            var svc = CreateService(db, segmentation.Object, factory);
            using var stream = new MemoryStream([1, 2, 3]);

            var summary = await svc.SegmentTileAsync(UserRoles.NationalAdmin, null, null, null, CommuneId100,
                AiDraftFeature.TypeRoad, stream, "tile.png", "image/png", (1.0, 1.0, 2.0, 2.0), default);

            Assert.Equal(0, summary.BuildingCount);
            Assert.Equal(1, summary.RoadCount);
            Assert.Single(summary.DraftIds);
            var saved = await db.AiDraftFeatures.ToListAsync();
            Assert.Single(saved);
            Assert.Equal(CommuneId100, saved[0].CommuneId);
            Assert.Equal(AiDraftFeature.TypeRoad, saved[0].FeatureType);
        }
    }

    [Fact]
    public async Task AcceptDraft_OutOfScopeCommune_ReturnsForbidden()
    {
        var (db, factory) = CreateInMemoryDbPair("DraftsAcceptOutOfScope");
        await using (db)
        {
            await SeedAsync(db);
            var draftId = await SeedData.AddDraftAsync(db, CommuneId101);
            var svc = CreateService(db, factory: factory);

            var result = await svc.AcceptDraftAsync(UserRoles.FieldWorker, CommuneId100, null, null, UserId, draftId, default);

            Assert.Equal(DraftReviewStatus.Forbidden, result.Status);
        }
    }

    [Fact]
    public async Task AcceptDraft_InScopeCommune_Succeeds()
    {
        var (db, factory) = CreateInMemoryDbPair("DraftsAcceptInScope");
        await using (db)
        {
            await SeedAsync(db);
            var draftId = await SeedData.AddDraftAsync(db, CommuneId100);
            var svc = CreateService(db, factory: factory);

            var result = await svc.AcceptDraftAsync(UserRoles.NationalAdmin, null, null, null, UserId, draftId, default);

            Assert.Equal(DraftReviewStatus.Success, result.Status);
            db.ChangeTracker.Clear();
            var draft = await db.AiDraftFeatures.FindAsync(draftId);
            Assert.Equal(AiDraftFeature.StatusAccepted, draft!.Status);
            Assert.Equal(UserId, draft.ReviewedBy);
        }
    }

    [Fact]
    public async Task RejectDraft_InScopeCommune_Succeeds()
    {
        var (db, factory) = CreateInMemoryDbPair("DraftsRejectInScope");
        await using (db)
        {
            await SeedAsync(db);
            var draftId = await SeedData.AddDraftAsync(db, CommuneId100);
            var svc = CreateService(db, factory: factory);

            var result = await svc.RejectDraftAsync(UserRoles.WilayaAdmin, null, null, WilayaId1, UserId, draftId, default);

            Assert.Equal(DraftReviewStatus.Success, result.Status);
            db.ChangeTracker.Clear();
            var draft = await db.AiDraftFeatures.FindAsync(draftId);
            Assert.Equal(AiDraftFeature.StatusRejected, draft!.Status);
        }
    }

    [Fact]
    public async Task AcceptDraft_UnknownDraft_ReturnsNotFound()
    {
        var (db, factory) = CreateInMemoryDbPair("DraftsAcceptUnknown");
        await using (db)
        {
            await SeedAsync(db);
            var svc = CreateService(db, factory: factory);

            var result = await svc.AcceptDraftAsync(UserRoles.NationalAdmin, null, null, null, UserId, Guid.NewGuid(), default);

            Assert.Equal(DraftReviewStatus.NotFound, result.Status);
        }
    }

    [Fact]
    public async Task AcceptDraft_AlreadyReviewed_ReturnsAlreadyReviewed()
    {
        var (db, factory) = CreateInMemoryDbPair("DraftsAcceptTwice");
        await using (db)
        {
            await SeedAsync(db);
            var draftId = await SeedData.AddDraftAsync(db, CommuneId100);
            var svc = CreateService(db, factory: factory);

            await svc.AcceptDraftAsync(UserRoles.NationalAdmin, null, null, null, UserId, draftId, default);
            var result = await svc.AcceptDraftAsync(UserRoles.NationalAdmin, null, null, null, UserId, draftId, default);

            Assert.Equal(DraftReviewStatus.AlreadyReviewed, result.Status);
        }
    }

    [Fact]
    public async Task AcceptRoadDraft_MaterializesRoadRow()
    {
        var (db, factory) = CreateInMemoryDbPair("DraftsAcceptMaterializes");
        await using (db)
        {
            await SeedAsync(db);
            var draftId = await SeedData.AddDraftAsync(db, CommuneId100);
            var svc = CreateService(db, factory: factory);

            var result = await svc.AcceptDraftAsync(UserRoles.NationalAdmin, null, null, null, UserId, draftId, default);

            Assert.Equal(DraftReviewStatus.Success, result.Status);
            db.ChangeTracker.Clear();
            var draft = await db.AiDraftFeatures.FindAsync(draftId);
            Assert.Equal(AiDraftFeature.StatusAccepted, draft!.Status);

            var road = Assert.Single(db.Roads);
            Assert.Equal(UserId, road.UserId);
            Assert.Equal(FeatureTypes.RoadLayers.Street, road.Layer);
            var data = JsonSerializer.Deserialize<JsonElement>(road.Data);
            var first = data.GetProperty("coordinates")[0];
            Assert.Equal("road", data.GetProperty("type").GetString());
            Assert.True(first.TryGetProperty("lat", out _));
            Assert.True(first.TryGetProperty("lng", out _));
            Assert.Equal(36.72, first.GetProperty("lng").GetDouble());
            Assert.Equal(2.96, first.GetProperty("lat").GetDouble());
        }
    }

    [Fact]
    public async Task AcceptRoadDraft_TooShort_ReturnsRulesNotMet()
    {
        var (db, factory) = CreateInMemoryDbPair("DraftsAcceptTooShort");
        await using (db)
        {
            await SeedAsync(db);
            var draft = AiDraftFeature.Create(
                featureType: AiDraftFeature.TypeRoad,
                geometryGeoJson: """{"type":"LineString","coordinates":[[36.7200,2.9600],[36.7201,2.9601]]}""",
                confidence: 0.9,
                communeId: CommuneId100,
                sourceTileRef: "tile.png",
                createdAt: FixedUtcNowOffset);
            db.AiDraftFeatures.Add(draft);
            await db.SaveChangesAsync();
            var svc = CreateService(db, factory: factory, roadRules: new RoadRulesOptions { MinRoadLengthM = 5000 });

            var result = await svc.AcceptDraftAsync(UserRoles.NationalAdmin, null, null, null, UserId, draft.Id, default);

            Assert.Equal(DraftReviewStatus.RulesNotMet, result.Status);
            Assert.Empty(db.Roads);
            db.ChangeTracker.Clear();
            var reloaded = await db.AiDraftFeatures.FindAsync(draft.Id);
            Assert.Equal(AiDraftFeature.StatusPending, reloaded!.Status);
        }
    }

    [Fact]
    public async Task AcceptRoadDraft_LowConfidence_ReturnsRulesNotMet()
    {
        var (db, factory) = CreateInMemoryDbPair("DraftsAcceptLowConfidence");
        await using (db)
        {
            await SeedAsync(db);
            var draft = AiDraftFeature.Create(
                featureType: AiDraftFeature.TypeRoad,
                geometryGeoJson: """{"type":"LineString","coordinates":[[36.70,2.95],[36.72,2.97]]}""",
                confidence: 0.3,
                communeId: CommuneId100,
                sourceTileRef: "tile.png",
                createdAt: FixedUtcNowOffset);
            db.AiDraftFeatures.Add(draft);
            await db.SaveChangesAsync();
            var svc = CreateService(db, factory: factory, roadRules: new RoadRulesOptions { MinConfidence = 0.5 });

            var result = await svc.AcceptDraftAsync(UserRoles.NationalAdmin, null, null, null, UserId, draft.Id, default);

            Assert.Equal(DraftReviewStatus.RulesNotMet, result.Status);
            Assert.Empty(db.Roads);
        }
    }

    // ── Building accept (Phase 2: entrance materialization) ────────────────
    // Road runs along a line of constant latitude (legacy data lat/lng field
    // naming); geometry is GeoJSON [lng, lat], like DraftGeometry parses it.

    private const string HorizontalRoadData = """{"type":"road","label":"","coordinates":[{"lat":2.9600,"lng":36.7200},{"lat":2.9600,"lng":36.7240}]}""";

    private static string Ring(double lngC, double latC, double half = 0.0002)
    {
        var culture = CultureInfo.InvariantCulture;
        string p(double lng, double lat) => $"[{lng.ToString("R", culture)},{lat.ToString("R", culture)}]";
        var ring = $"{p(lngC - half, latC - half)},{p(lngC + half, latC - half)},{p(lngC + half, latC + half)},{p(lngC - half, latC + half)},{p(lngC - half, latC - half)}";
        return $"{{\"type\":\"Polygon\",\"coordinates\":[[{ring}]]}}";
    }

    private static async Task<AiDraftFeature> AddBuildingDraftAsync(
        AppDbContext db, int communeId, string geometry, double confidence = 0.9)
    {
        var draft = AiDraftFeature.Create(
            featureType: AiDraftFeature.TypeBuilding,
            geometryGeoJson: geometry,
            confidence: confidence,
            communeId: communeId,
            sourceTileRef: "tile.png",
            createdAt: FixedUtcNowOffset);
        db.AiDraftFeatures.Add(draft);
        await db.SaveChangesAsync();
        return draft;
    }

    [Fact]
    public async Task AcceptBuildingDraft_WithNearbyRoad_CreatesNumberedEntrance()
    {
        var (db, factory) = CreateInMemoryDbPair("DraftsAcceptBuilding");
        await using (db)
        {
            await SeedAsync(db);
            // Road at lat 2.96 lng 36.72→36.724; building sits just above the
            // road (centroid lat 2.9603) so the side decision is robust.
            var owner = await SeedData.CreateUserAsync(db, UserRoles.CommuneUser, communeId: CommuneId100);
            var roadId = await TestData.AddRoadAsync(db, owner.Id, HorizontalRoadData);
            var draft = await AddBuildingDraftAsync(db, CommuneId100, Ring(36.7220, 2.9603));
            var svc = CreateService(db, factory: factory);

            var result = await svc.AcceptDraftAsync(UserRoles.NationalAdmin, null, null, null, UserId, draft.Id, default);

            Assert.Equal(DraftReviewStatus.Success, result.Status);
            var entrance = Assert.Single(db.HouseEntrances.AsNoTracking());
            Assert.Equal(roadId, entrance.RoadId);
            Assert.Equal(owner.Id, entrance.UserId);
            Assert.Equal(FeatureTypes.HouseEntranceLayers.Main, entrance.Layer);
            Assert.Equal("1", entrance.Label);

            var data = JsonSerializer.Deserialize<JsonElement>(entrance.Data);
            Assert.Equal("houseEntrances", data.GetProperty("type").GetString());
            Assert.Equal("main_entrance", data.GetProperty("entranceTypeKey").GetString());
            Assert.Equal("left", data.GetProperty("side").GetString());
            Assert.Equal(1, data.GetProperty("entranceNumber").GetInt32());
            Assert.Equal(roadId.ToString(), data.GetProperty("roadDbId").GetString());
            // Entrance = nearest ring vertex to the road: the bottom edge of a
            // ring spanning lat 2.9601..2.9605, i.e. lat ≈ 2.9601.
            Assert.Equal(2.9601, data.GetProperty("lat").GetDouble(), precision: 3);
            Assert.True(Math.Abs(data.GetProperty("lng").GetDouble() - 36.7220) <= 0.0003);
            Assert.Equal(1, data.GetProperty("coordinates").GetArrayLength());
            Assert.True(data.GetProperty("coordinates")[0].TryGetProperty("lat", out _));
            Assert.True(data.TryGetProperty("buildFootprint", out _), "accepted building footprint must be preserved");

            Assert.NotNull(await db.FeatureRegistry.FirstOrDefaultAsync(r => r.Id == entrance.Id));
            db.ChangeTracker.Clear();
            Assert.Equal(AiDraftFeature.StatusAccepted, (await db.AiDraftFeatures.FindAsync(draft.Id))!.Status);
        }
    }

    [Fact]
    public async Task AcceptBuildingDraft_SecondOnSameSide_UsesNextFreeNumber()
    {
        var (db, factory) = CreateInMemoryDbPair("DraftsAcceptBuildingSecond");
        await using (db)
        {
            await SeedAsync(db);
            var owner = await SeedData.CreateUserAsync(db, UserRoles.CommuneUser, communeId: CommuneId100);
            var roadId = await TestData.AddRoadAsync(db, owner.Id, HorizontalRoadData);
            var svc = CreateService(db, factory: factory);

            var first = await AddBuildingDraftAsync(db, CommuneId100, Ring(36.7220, 2.9603));
            var second = await AddBuildingDraftAsync(db, CommuneId100, Ring(36.7225, 2.9603));

            var result = await svc.AcceptDraftAsync(UserRoles.NationalAdmin, null, null, null, UserId, first.Id, default);
            Assert.Equal(DraftReviewStatus.Success, result.Status);
            result = await svc.AcceptDraftAsync(UserRoles.NationalAdmin, null, null, null, UserId, second.Id, default);
            Assert.Equal(DraftReviewStatus.Success, result.Status);

            var entrances = db.HouseEntrances.AsNoTracking().OrderBy(e => e.Id).ToList();
            Assert.Equal(2, entrances.Count);
            var numbers = entrances.Select(e => JsonSerializer.Deserialize<JsonElement>(e.Data).GetProperty("entranceNumber").GetInt32()).Order().ToList();
            Assert.Equal([1, 3], numbers);
        }
    }

    [Fact]
    public async Task AcceptBuildingDraft_OnRightSide_UsesEvenNumber()
    {
        var (db, factory) = CreateInMemoryDbPair("DraftsAcceptBuildingRightSide");
        await using (db)
        {
            await SeedAsync(db);
            var owner = await SeedData.CreateUserAsync(db, UserRoles.CommuneUser, communeId: CommuneId100);
            _ = await TestData.AddRoadAsync(db, owner.Id, HorizontalRoadData);
            // Building below the road (lower lat) → cross product < 0 → right.
            var draft = await AddBuildingDraftAsync(db, CommuneId100, Ring(36.7220, 2.9597));
            var svc = CreateService(db, factory: factory);

            var result = await svc.AcceptDraftAsync(UserRoles.NationalAdmin, null, null, null, UserId, draft.Id, default);

            Assert.Equal(DraftReviewStatus.Success, result.Status);
            var entrance = Assert.Single(db.HouseEntrances.AsNoTracking());
            var data = JsonSerializer.Deserialize<JsonElement>(entrance.Data);
            Assert.Equal("right", data.GetProperty("side").GetString());
            Assert.Equal(2, data.GetProperty("entranceNumber").GetInt32());
        }
    }

    [Fact]
    public async Task AcceptBuildingDraft_LowConfidence_ReturnsRulesNotMet()
    {
        var (db, factory) = CreateInMemoryDbPair("DraftsAcceptBuildingLowConfidence");
        await using (db)
        {
            await SeedAsync(db);
            var owner = await SeedData.CreateUserAsync(db, UserRoles.CommuneUser, communeId: CommuneId100);
            _ = await TestData.AddRoadAsync(db, owner.Id, HorizontalRoadData);
            var draft = await AddBuildingDraftAsync(db, CommuneId100, Ring(36.7220, 2.9600), confidence: 0.3);
            var svc = CreateService(db, factory: factory, buildingRules: new BuildingRulesOptions { MinConfidence = 0.5 });

            var result = await svc.AcceptDraftAsync(UserRoles.NationalAdmin, null, null, null, UserId, draft.Id, default);

            Assert.Equal(DraftReviewStatus.RulesNotMet, result.Status);
            Assert.Empty(db.HouseEntrances);
            db.ChangeTracker.Clear();
            Assert.Equal(AiDraftFeature.StatusPending, (await db.AiDraftFeatures.FindAsync(draft.Id))!.Status);
        }
    }

    [Fact]
    public async Task AcceptBuildingDraft_NoRoadInCommune_ReturnsRulesNotMet()
    {
        var (db, factory) = CreateInMemoryDbPair("DraftsAcceptBuildingNoRoad");
        await using (db)
        {
            await SeedAsync(db);
            var draft = await AddBuildingDraftAsync(db, CommuneId100, Ring(36.7220, 2.9600));
            var svc = CreateService(db, factory: factory);

            var result = await svc.AcceptDraftAsync(UserRoles.NationalAdmin, null, null, null, UserId, draft.Id, default);

            Assert.Equal(DraftReviewStatus.RulesNotMet, result.Status);
            Assert.Empty(db.HouseEntrances);
            db.ChangeTracker.Clear();
            Assert.Equal(AiDraftFeature.StatusPending, (await db.AiDraftFeatures.FindAsync(draft.Id))!.Status);
        }
    }

    [Fact]
    public async Task AcceptBuildingDraft_RoadTooFar_ReturnsRulesNotMet()
    {
        var (db, factory) = CreateInMemoryDbPair("DraftsAcceptBuildingTooFar");
        await using (db)
        {
            await SeedAsync(db);
            var owner = await SeedData.CreateUserAsync(db, UserRoles.CommuneUser, communeId: CommuneId100);
            _ = await TestData.AddRoadAsync(db, owner.Id, HorizontalRoadData);
            // ~3 km from the road → beyond the 100 m reference cap.
            var draft = await AddBuildingDraftAsync(db, CommuneId100, Ring(36.7500, 2.9900));
            var svc = CreateService(db, factory: factory);

            var result = await svc.AcceptDraftAsync(UserRoles.NationalAdmin, null, null, null, UserId, draft.Id, default);

            Assert.Equal(DraftReviewStatus.RulesNotMet, result.Status);
            Assert.Empty(db.HouseEntrances);
        }
    }

    [Fact]
    public async Task AcceptBuildingDraft_NotPolygon_ReturnsInvalidGeometry()
    {
        var (db, factory) = CreateInMemoryDbPair("DraftsAcceptBuildingWrongKind");
        await using (db)
        {
            await SeedAsync(db);
            var owner = await SeedData.CreateUserAsync(db, UserRoles.CommuneUser, communeId: CommuneId100);
            _ = await TestData.AddRoadAsync(db, owner.Id, HorizontalRoadData);
            var draft = await AddBuildingDraftAsync(db, CommuneId100,
                """{"type":"LineString","coordinates":[[36.72,2.96],[36.73,2.97]]}""");
            var svc = CreateService(db, factory: factory);

            var result = await svc.AcceptDraftAsync(UserRoles.NationalAdmin, null, null, null, UserId, draft.Id, default);

            Assert.Equal(DraftReviewStatus.InvalidGeometry, result.Status);
            Assert.Empty(db.HouseEntrances);
        }
    }

    [Fact]
    public async Task UpdateDraft_ChangesGeometry()
    {
        var (db, factory) = CreateInMemoryDbPair("DraftsUpdate");
        await using (db)
        {
            await SeedAsync(db);
            var draftId = await SeedData.AddDraftAsync(db, CommuneId100);
            var svc = CreateService(db, factory: factory);
            const string updatedGeoJson = """{"type":"LineString","coordinates":[[36.71,2.95],[36.74,2.98]]}""";

            var result = await svc.UpdateDraftAsync(UserRoles.NationalAdmin, null, null, null, UserId, draftId, updatedGeoJson, default);

            Assert.Equal(DraftReviewStatus.Success, result.Status);
            db.ChangeTracker.Clear();
            var draft = await db.AiDraftFeatures.FindAsync(draftId);
            Assert.Equal(updatedGeoJson, draft!.GeometryGeoJson);
        }
    }

    [Fact]
    public async Task UpdateDraft_WrongGeometryKind_ReturnsInvalidGeometry()
    {
        var (db, factory) = CreateInMemoryDbPair("DraftsUpdateWrongKind");
        await using (db)
        {
            await SeedAsync(db);
            var draftId = await SeedData.AddDraftAsync(db, CommuneId100);
            var svc = CreateService(db, factory: factory);

            var result = await svc.UpdateDraftAsync(UserRoles.NationalAdmin, null, null, null, UserId, draftId,
                """{"type":"Polygon","coordinates":[[[36.7,2.9],[36.8,2.9],[36.8,3.0],[36.7,2.9]]]}""", default);

            Assert.Equal(DraftReviewStatus.InvalidGeometry, result.Status);
            db.ChangeTracker.Clear();
            var draft = await db.AiDraftFeatures.FindAsync(draftId);
            Assert.Equal("""{"type":"LineString","coordinates":[[36.72,2.96],[36.73,2.97]]}""", draft!.GeometryGeoJson);
        }
    }

    [Fact]
    public async Task UpdateDraft_NonPending_ReturnsAlreadyReviewed()
    {
        var (db, factory) = CreateInMemoryDbPair("DraftsUpdateReviewed");
        await using (db)
        {
            await SeedAsync(db);
            var draftId = await SeedData.AddDraftAsync(db, CommuneId100);
            var svc = CreateService(db, factory: factory);
            await svc.AcceptDraftAsync(UserRoles.NationalAdmin, null, null, null, UserId, draftId, default);

            var result = await svc.UpdateDraftAsync(UserRoles.NationalAdmin, null, null, null, UserId, draftId,
                """{"type":"LineString","coordinates":[[36.71,2.95],[36.74,2.98]]}""", default);

            Assert.Equal(DraftReviewStatus.AlreadyReviewed, result.Status);
        }
    }

    [Fact]
    public async Task UpdateDraft_OutOfScope_ReturnsForbidden()
    {
        var (db, factory) = CreateInMemoryDbPair("DraftsUpdateOutOfScope");
        await using (db)
        {
            await SeedAsync(db);
            var draftId = await SeedData.AddDraftAsync(db, CommuneId101);
            var svc = CreateService(db, factory: factory);

            var result = await svc.UpdateDraftAsync(UserRoles.FieldWorker, CommuneId100, null, null, UserId, draftId,
                """{"type":"LineString","coordinates":[[36.71,2.95],[36.74,2.98]]}""", default);

            Assert.Equal(DraftReviewStatus.Forbidden, result.Status);
        }
    }

    [Fact]
    public async Task DeleteDraft_RemovesPending()
    {
        var (db, factory) = CreateInMemoryDbPair("DraftsDelete");
        await using (db)
        {
            await SeedAsync(db);
            var draftId = await SeedData.AddDraftAsync(db, CommuneId100);
            var svc = CreateService(db, factory: factory);

            var result = await svc.DeleteDraftAsync(UserRoles.NationalAdmin, null, null, null, UserId, draftId, default);

            Assert.Equal(DraftReviewStatus.Success, result.Status);
            Assert.Empty(db.AiDraftFeatures);
        }
    }

    [Fact]
    public async Task DeleteDraft_NonPending_ReturnsAlreadyReviewed()
    {
        var (db, factory) = CreateInMemoryDbPair("DraftsDeleteReviewed");
        await using (db)
        {
            await SeedAsync(db);
            var draftId = await SeedData.AddDraftAsync(db, CommuneId100);
            var svc = CreateService(db, factory: factory);
            await svc.AcceptDraftAsync(UserRoles.NationalAdmin, null, null, null, UserId, draftId, default);

            var result = await svc.DeleteDraftAsync(UserRoles.NationalAdmin, null, null, null, UserId, draftId, default);

            Assert.Equal(DraftReviewStatus.AlreadyReviewed, result.Status);
        }
    }
}
