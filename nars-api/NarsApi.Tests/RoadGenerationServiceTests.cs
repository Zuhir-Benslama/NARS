using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using static NarsApi.Tests.SeedData;
using Xunit;

namespace NarsApi.Tests;

/// <summary>
/// InMemory unit tests for <see cref="RoadGenerationService"/>. The PostgreSQL-
/// only conditional ExecuteUpdateAsync is replaced by an equivalent tracked
/// update (same convention as DraftFeaturesUnitTests).
/// </summary>
public class RoadGenerationUnitTests
{
    private sealed class TestableRoadGenerationService(
        IDbContextFactory<AppDbContext> dbFactory,
        ICommuneScopeService communeScope,
        IDateTimeProvider timeProvider,
        ValidationOptions validation,
        RoadRulesOptions roadRules) : RoadGenerationService(
            dbFactory, communeScope, timeProvider, Options.Create(validation), Options.Create(roadRules))
    {
        protected override async Task<int> TransitionDraftsToAcceptedAsync(
            AppDbContext db, IReadOnlyList<Guid> draftIds, Guid userId, DateTime reviewedAt, CancellationToken ct)
        {
            var affected = 0;
            foreach (var draftId in draftIds)
            {
                var draft = await db.AiDraftFeatures.FirstOrDefaultAsync(
                    f => f.Id == draftId && f.Status == AiDraftFeature.StatusPending, ct);
                if (draft is null)
                {
                    continue;
                }

                var entry = db.Entry(draft);
                entry.Property(f => f.Status).CurrentValue = AiDraftFeature.StatusAccepted;
                entry.Property(f => f.ReviewedBy).CurrentValue = userId;
                entry.Property(f => f.ReviewedAt).CurrentValue = reviewedAt;
                affected++;
            }

            await db.SaveChangesAsync(ct);
            return affected;
        }
    }

    private const string UserRole = UserRoles.CommuneUser;

    private static RoadGenerationService CreateService(
        AppDbContext db,
        IDbContextFactory<AppDbContext>? factory = null,
        ICommuneScopeService? communeScope = null,
        ValidationOptions? validation = null,
        RoadRulesOptions? roadRules = null) =>
        new TestableRoadGenerationService(
            factory ?? new TestDbContextFactory(db),
            communeScope ?? AllowCommuneScope(),
            Mock.Of<IDateTimeProvider>(x => x.UtcNow == FixedUtcNow),
            validation ?? new ValidationOptions { RoadTurnAngleDegrees = 90.0, RoadConnectivityMeters = 20.0 },
            roadRules ?? new RoadRulesOptions());

    private static ICommuneScopeService AllowCommuneScope() => CommuneScopeMock(allow: true);

    private static ICommuneScopeService CommuneScopeMock(bool allow)
    {
        var mock = new Mock<ICommuneScopeService>();
        mock.Setup(s => s.CanAccessCommuneAsync(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(allow);
        return mock.Object;
    }

    /// <summary>Area data JSONB (open ring; implicitly closed by the geometry rules).</summary>
    private static string AreaData(double minLon, double minLat, double maxLon, double maxLat)
        => $$"""
            {"type":"areas","label":"","areaTypeKey":"central_urban","coordinates":[
              {"lat":{{minLat}},"lng":{{minLon}}},
              {"lat":{{minLat}},"lng":{{maxLon}}},
              {"lat":{{maxLat}},"lng":{{maxLon}}},
              {"lat":{{maxLat}},"lng":{{minLon}}}]}
            """;

    /// <summary>Road data JSONB used to seed the commune's existing road network.</summary>
    private static string RoadData(double lon1, double lat1, double lon2, double lat2)
        => $$"""
            {"type":"road","label":"","roadTypeKey":"street","coordinates":[
              {"lat":{{lat1}},"lng":{{lon1}}},
              {"lat":{{lat2}},"lng":{{lon2}}}]}
            """;

    private static string LineGeoJson(params (double Lon, double Lat)[] points)
        => """{"type":"LineString","coordinates":["""
        + string.Join(",", points.Select(p => $"[{p.Lon},{p.Lat}]"))
        + "]}";

    private static async Task<(Guid UserId, Guid DraftId)> SeedCommuneAsync(
        AppDbContext db, string geometryGeoJson, double confidence = 0.9)
    {
        var user = await CreateUserAsync(db, UserRole, communeId: CommuneId100);
        var draft = AiDraftFeature.Create(
            featureType: AiDraftFeature.TypeRoad,
            geometryGeoJson: geometryGeoJson,
            confidence: confidence,
            communeId: CommuneId100,
            sourceTileRef: "tile.png",
            createdAt: FixedUtcNowOffset);
        db.AiDraftFeatures.Add(draft);
        await db.SaveChangesAsync();
        return (user.Id, draft.Id);
    }

    private static async Task<Guid> AddUrbanAreaAsync(AppDbContext db, Guid userId)
    {
        var area = new Area
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Layer = FeatureTypes.AreaLayers.CentralUrban,
            Label = "Urban",
            Data = AreaData(2.950, 36.715, 2.960, 36.725),
        };
        db.Areas.Add(area);
        await db.SaveChangesAsync();
        return area.Id;
    }

    private static async Task SeedRoadAsync(AppDbContext db, Guid userId)
    {
        await SeedRoadAsync(db, userId, 2.9510, 36.7165, 2.9540, 36.7165);
    }

    private static async Task SeedRoadAsync(
        AppDbContext db, Guid userId, double lon1, double lat1, double lon2, double lat2)
    {
        db.Roads.Add(new Road
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Layer = FeatureTypes.RoadLayers.Street,
            Label = "Network",
            Data = RoadData(lon1, lat1, lon2, lat2),
            UpdatedAt = FixedUtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<string> DraftStatusAsync(
        IDbContextFactory<AppDbContext> factory, Guid draftId)
    {
        await using var fresh = await factory.CreateDbContextAsync();
        return (await fresh.AiDraftFeatures.FirstAsync(f => f.Id == draftId)).Status;
    }

    // ── Tests ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Generate_NoPendingDrafts_ReturnsEmpty()
    {
        var (db, factory) = CreateInMemoryDbPair("RoadGenNoDrafts");
        await using (db)
        {
            await SeedAdminLocationsAsync(db);
            var user = await CreateUserAsync(db, UserRole, communeId: CommuneId100);
            var svc = CreateService(db, factory);

            var summary = await svc.GenerateAsync(
                UserRole, CommuneId100, null, null, user.Id, CommuneId100, [Guid.NewGuid()], default);

            Assert.Empty(summary.Created);
            Assert.Equal(0, summary.Dropped);
        }
    }

    [Fact]
    public async Task Generate_OutsideUrbanArea_DropsAndLeavesDraftPending()
    {
        var (db, factory) = CreateInMemoryDbPair("RoadGenOutsideArea");
        await using (db)
        {
            await SeedAdminLocationsAsync(db);
            // Draft at lat 36.700 — ~1.7 km south of the urban area (≥36.715).
            var (userId, draftId) = await SeedCommuneAsync(
                db, LineGeoJson((2.9510, 36.7000), (2.9540, 36.7000)));
            await AddUrbanAreaAsync(db, userId);
            var svc = CreateService(db, factory);

            var summary = await svc.GenerateAsync(
                UserRole, CommuneId100, null, null, userId, CommuneId100, [draftId], default);

            Assert.Empty(summary.Created);
            Assert.Equal(1, summary.Dropped);
            Assert.Equal(AiDraftFeature.StatusPending, await DraftStatusAsync(factory, draftId));
        }
    }

    [Fact]
    public async Task Generate_TurnAngleExceeded_DropsAndLeavesDraftPending()
    {
        var (db, factory) = CreateInMemoryDbPair("RoadGenTurnAngle");
        await using (db)
        {
            await SeedAdminLocationsAsync(db);
            // Three vertices making an ~108° turn (> 90°), all inside the area.
            var (userId, draftId) = await SeedCommuneAsync(
                db, LineGeoJson((2.9510, 36.7160), (2.9520, 36.7170), (2.9500, 36.7180)));
            await AddUrbanAreaAsync(db, userId);
            var svc = CreateService(db, factory);

            var summary = await svc.GenerateAsync(
                UserRole, CommuneId100, null, null, userId, CommuneId100, [draftId], default);

            Assert.Empty(summary.Created);
            Assert.Equal(1, summary.Dropped);
            Assert.Equal(AiDraftFeature.StatusPending, await DraftStatusAsync(factory, draftId));
        }
    }

    [Fact]
    public async Task Generate_TurnAngleUpTo135Degrees_Accepted()
    {
        var (db, factory) = CreateInMemoryDbPair("RoadGenTurnAngle135");
        await using (db)
        {
            await SeedAdminLocationsAsync(db);
            // Three vertices making an ~126° turn: sharper than the old 90°
            // limit but under the relaxed 135° one — a gentle deflection like a
            // small intersection kink must survive the rules.
            var (userId, draftId) = await SeedCommuneAsync(
                db, LineGeoJson((2.9510, 36.7160), (2.9525, 36.7175), (2.9530, 36.7160)));
            await AddUrbanAreaAsync(db, userId);
            var svc = CreateService(db, factory,
                validation: new ValidationOptions { RoadTurnAngleDegrees = 135.0 });

            var summary = await svc.GenerateAsync(
                UserRole, CommuneId100, null, null, userId, CommuneId100, [draftId], default);

            var road = Assert.Single(summary.Created);
            Assert.Equal(0, summary.Dropped);
            Assert.Equal(3, road.Data["coordinates"]!.AsArray().Count);
            Assert.Equal(AiDraftFeature.StatusAccepted, await DraftStatusAsync(factory, draftId));
        }
    }

    [Fact]
    public async Task Generate_FirstRoad_InUrbanArea_NoNetworkExemption_Accepted()
    {
        var (db, factory) = CreateInMemoryDbPair("RoadGenFirstRoad");
        await using (db)
        {
            await SeedAdminLocationsAsync(db);
            var (userId, draftId) = await SeedCommuneAsync(
                db, LineGeoJson((2.9510, 36.7160), (2.9540, 36.7160), (2.9560, 36.7160)));
            await AddUrbanAreaAsync(db, userId);
            var svc = CreateService(db, factory);

            // No roads exist in the commune: connectivity is exempt, straight
            // east-west centerline, all vertices inside the area.
            var summary = await svc.GenerateAsync(
                UserRole, CommuneId100, null, null, userId, CommuneId100, [draftId], default);

            var road = Assert.Single(summary.Created);
            Assert.Equal(FeatureTypes.RoadLayers.Street, road.Layer);
            var coords = road.Data["coordinates"]!.AsArray();
            Assert.Equal(3, coords.Count);
            Assert.Equal(2.9560, coords[2]!["lng"]!.GetValue<double>(), 5);
            await using var fresh = await factory.CreateDbContextAsync();
            var draft = await fresh.AiDraftFeatures.FirstAsync(f => f.Id == draftId);
            Assert.Equal(AiDraftFeature.StatusAccepted, draft.Status);
        }
    }

    [Fact]
    public async Task Generate_SnapsEndpointsOntoExistingNetwork()
    {
        var (db, factory) = CreateInMemoryDbPair("RoadGenSnap");
        await using (db)
        {
            await SeedAdminLocationsAsync(db);
            // Two vertical network roads 90 m apart. The draft runs between
            // them: each endpoint sits ~18 m off its road (inside the 20 m snap
            // tolerance) and is pulled onto it, bridging the gap as a genuine
            // crossing segment — no corridor is duplicated, so the road survives.
            var (userId, draftId) = await SeedCommuneAsync(
                db, LineGeoJson((2.9512, 36.7166), (2.9518, 36.7166)));
            await AddUrbanAreaAsync(db, userId);
            await SeedRoadAsync(db, userId, 2.9510, 36.7164, 2.9510, 36.7168);
            await SeedRoadAsync(db, userId, 2.9520, 36.7164, 2.9520, 36.7168);
            var svc = CreateService(db, factory);

            var summary = await svc.GenerateAsync(
                UserRole, CommuneId100, null, null, userId, CommuneId100, [draftId], default);

            var road = Assert.Single(summary.Created);
            Assert.Equal(0, summary.Dropped);
            var coords = road.Data["coordinates"]!.AsArray();
            Assert.Equal(2, coords.Count);
            // Both endpoints pulled onto the network.
            Assert.Equal(2.9510, coords[0]!["lng"]!.GetValue<double>(), 5);
            Assert.Equal(2.9520, coords[1]!["lng"]!.GetValue<double>(), 5);
            Assert.Equal(36.7166, coords[0]!["lat"]!.GetValue<double>(), 6);
            Assert.Equal(36.7166, coords[1]!["lat"]!.GetValue<double>(), 6);
        }
    }

    [Fact]
    public async Task Generate_ParallelToExistingNetwork_IsDroppedAsTooClose()
    {
        var (db, factory) = CreateInMemoryDbPair("RoadGenParallelDup");
        await using (db)
        {
            await SeedAdminLocationsAsync(db);
            // A draft running parallel ~11 m north of a mapped road: both
            // endpoints sit within the 20 m snap tolerance and are pulled onto
            // the road, collapsing the road onto a duplicate corridor. The
            // min-separation rule must prune it (this is the duplicate-road
            // defect the toggle fixes), not accept an overlapping twin.
            var (userId, draftId) = await SeedCommuneAsync(
                db, LineGeoJson((2.9512, 36.7166), (2.9538, 36.7166)));
            await AddUrbanAreaAsync(db, userId);
            await SeedRoadAsync(db, userId); // east-west network at lat 36.7165
            var svc = CreateService(db, factory);

            var summary = await svc.GenerateAsync(
                UserRole, CommuneId100, null, null, userId, CommuneId100, [draftId], default);

            Assert.Empty(summary.Created);
            Assert.Equal(1, summary.Dropped);
            Assert.Equal(1, summary.Breakdown.TooClose);
            Assert.Equal(AiDraftFeature.StatusPending, await DraftStatusAsync(factory, draftId));
        }
    }

    [Fact]
    public async Task Generate_SnapCollapsingToZeroLength_IsRejectedAsTooShort()
    {
        var (db, factory) = CreateInMemoryDbPair("RoadGenSnapCollapse");
        await using (db)
        {
            await SeedAdminLocationsAsync(db);
            // A ~22 m draft straddling the seeded east-west network at its
            // midpoint: both endpoints sit ~11 m off the line (within the 20 m
            // snap tolerance) and project onto the SAME network point. The
            // pre-snap length rule passes (22 m >= 10 m) but the snap would
            // collapse the whole road onto that node — the post-snap length
            // re-check must reject it.
            var (userId, draftId) = await SeedCommuneAsync(
                db, LineGeoJson((2.9525, 36.7164), (2.9525, 36.7166)));
            await AddUrbanAreaAsync(db, userId);
            await SeedRoadAsync(db, userId);
            var svc = CreateService(db, factory);

            // The draft is cut at the crossing, so two ~11 m pieces are
            // evaluated; each collapses onto the same network node. Dropped
            // counts the single draft, TooShort the two collapsed pieces.
            var summary = await svc.GenerateAsync(
                UserRole, CommuneId100, null, null, userId, CommuneId100, [draftId], default);

            Assert.Empty(summary.Created);
            Assert.Equal(1, summary.Dropped);
            Assert.Equal(2, summary.Breakdown.TooShort);
            Assert.Equal(AiDraftFeature.StatusPending, await DraftStatusAsync(factory, draftId));
        }
    }

    [Fact]
    public async Task Generate_UnconnectableEndpoint_SeedsNetworkInsteadOfDropping()
    {
        var (db, factory) = CreateInMemoryDbPair("RoadGenUnconnected");
        await using (db)
        {
            await SeedAdminLocationsAsync(db);
            // Inside the urban area but ~500 m from the seeded network. The
            // paper rule used to drop such a road as Disconnected; connectivity
            // is a merge (not a rejection), so a road that cannot reach the
            // network seeds it instead — otherwise generation from a sparse
            // commune could never get started.
            var (userId, draftId) = await SeedCommuneAsync(
                db, LineGeoJson((2.9510, 36.7210), (2.9520, 36.7210)));
            await AddUrbanAreaAsync(db, userId);
            await SeedRoadAsync(db, userId);
            var svc = CreateService(db, factory);

            var summary = await svc.GenerateAsync(
                UserRole, CommuneId100, null, null, userId, CommuneId100, [draftId], default);

            var road = Assert.Single(summary.Created);
            Assert.Equal(0, summary.Dropped);
            // No endpoint was within the snap tolerance, so the seed kept its
            // original coordinates rather than being pulled off-course.
            Assert.Equal(36.7210, road.Data["coordinates"]![0]!["lat"]!.GetValue<double>(), 6);
            Assert.Equal(AiDraftFeature.StatusAccepted, await DraftStatusAsync(factory, draftId));
        }
    }

    [Fact]
    public async Task Generate_RemoteRoadOnly_SeedsRoadsExemptFromConnectivity()
    {
        var (db, factory) = CreateInMemoryDbPair("RoadGenRemoteNetwork");
        await using (db)
        {
            await SeedAdminLocationsAsync(db);
            var (userId, draftId) = await SeedCommuneAsync(
                db, LineGeoJson((2.9510, 36.7160), (2.9540, 36.7160), (2.9560, 36.7160)));
            await AddUrbanAreaAsync(db, userId);
            // A mapped road kilometres away from this corridor (the live
            // failure mode: a legacy/demo road sitting in a different
            // district). It must not gate generation here.
            await SeedRoadAsync(db, userId, 2.9600, 36.7600, 2.9630, 36.7600);
            var svc = CreateService(db, factory);

            var summary = await svc.GenerateAsync(
                UserRole, CommuneId100, null, null, userId, CommuneId100, [draftId], default);

            var road = Assert.Single(summary.Created);
            Assert.Equal(0, summary.Dropped);
            // No snapping happened: the lone local corridor kept its own coords.
            Assert.Equal(36.7160, road.Data["coordinates"]![0]!["lat"]!.GetValue<double>(), 6);
            Assert.Equal(AiDraftFeature.StatusAccepted, await DraftStatusAsync(factory, draftId));
        }
    }

    [Fact]
    public async Task Generate_LocalAndRemoteRoads_ParallelDuplicateIsDroppedAsTooClose()
    {
        var (db, factory) = CreateInMemoryDbPair("RoadGenLocalAndRemote");
        await using (db)
        {
            await SeedAdminLocationsAsync(db);
            var (userId, draftId) = await SeedCommuneAsync(
                db, LineGeoJson((2.9512, 36.7166), (2.9538, 36.7166)));
            await AddUrbanAreaAsync(db, userId);
            await SeedRoadAsync(db, userId); // local east-west network at lat 36.7165
            await SeedRoadAsync(db, userId, 2.9600, 36.7600, 2.9630, 36.7600); // remote
            var svc = CreateService(db, factory);

            // The duplicate detection was evaluated against the LOCAL road: if
            // the snap had latched onto the far-away road instead, the result
            // would not be a near-coinciding corridor and would be kept. Being
            // pruned as TooClose proves the local road won, exactly as its
            // older sibling asserted.
            var summary = await svc.GenerateAsync(
                UserRole, CommuneId100, null, null, userId, CommuneId100, [draftId], default);

            Assert.Empty(summary.Created);
            Assert.Equal(1, summary.Dropped);
            Assert.Equal(1, summary.Breakdown.TooClose);
        }
    }

    [Fact]
    public async Task Generate_MixedAcceptance_CountsCreatedAndDropped()
    {
        var (db, factory) = CreateInMemoryDbPair("RoadGenMixed");
        await using (db)
        {
            await SeedAdminLocationsAsync(db);
            var (userId, okId) = await SeedCommuneAsync(
                db, LineGeoJson((2.9510, 36.7160), (2.9560, 36.7160)));
            var (_, outId) = await SeedCommuneAsync(
                db, LineGeoJson((2.9510, 36.7000), (2.9540, 36.7000)));
            var (_, turnId) = await SeedCommuneAsync(
                db, LineGeoJson((2.9510, 36.7160), (2.9520, 36.7170), (2.9500, 36.7180)));
            var (_, lowConfId) = await SeedCommuneAsync(
                db, LineGeoJson((2.9510, 36.7160), (2.9560, 36.7160)), confidence: 0.1);
            await AddUrbanAreaAsync(db, userId);
            var svc = CreateService(db, factory, roadRules: new RoadRulesOptions { MinConfidence = 0.5, MinRoadLengthM = 1.0 });

            var summary = await svc.GenerateAsync(
                UserRole, CommuneId100, null, null, userId, CommuneId100,
                [okId, outId, turnId, lowConfId], default);

            var road = Assert.Single(summary.Created);
            Assert.NotEqual(Guid.Empty, road.DbId);
            Assert.Equal(2, road.Data["coordinates"]!.AsArray().Count);
            Assert.Equal(3, summary.Dropped);
            Assert.Equal(1, summary.Breakdown.OutsideUrbanArea);
            Assert.Equal(1, summary.Breakdown.ExcessiveTurnAngle);
            Assert.Equal(1, summary.Breakdown.LowConfidence);
            Assert.Equal(0, summary.Breakdown.TooShort);
            Assert.Equal(0, summary.Breakdown.InvalidGeometry);
            Assert.Equal(AiDraftFeature.StatusAccepted, await DraftStatusAsync(factory, okId));
            Assert.Equal(AiDraftFeature.StatusPending, await DraftStatusAsync(factory, outId));
            Assert.Equal(AiDraftFeature.StatusPending, await DraftStatusAsync(factory, turnId));
            Assert.Equal(AiDraftFeature.StatusPending, await DraftStatusAsync(factory, lowConfId));
        }
    }

    [Fact]
    public async Task Generate_ShortDraftWithNoNetwork_SeedsTheNetwork()
    {
        var (db, factory) = CreateInMemoryDbPair("RoadGenShortIsolated");
        await using (db)
        {
            await SeedAdminLocationsAsync(db);
            // A ~4.5 m draft inside the area with no road anywhere near it —
            // short AND trivially "isolated". With no network to be a spur of,
            // it is the bootstrap: it seeds the commune's road network instead
            // of being pruned.
            var (userId, draftId) = await SeedCommuneAsync(
                db, LineGeoJson((2.9520, 36.7160), (2.95205, 36.7160)));
            await AddUrbanAreaAsync(db, userId);
            var svc = CreateService(db, factory);

            var summary = await svc.GenerateAsync(
                UserRole, CommuneId100, null, null, userId, CommuneId100, [draftId], default);

            var road = Assert.Single(summary.Created);
            Assert.Equal(0, summary.Dropped);
            Assert.Equal(36.7160, road.Data["coordinates"]![0]!["lat"]!.GetValue<double>(), 6);
            Assert.Equal(AiDraftFeature.StatusAccepted, await DraftStatusAsync(factory, draftId));
        }
    }

    [Fact]
    public async Task Generate_DropsShortDraftNearNetwork()
    {
        var (db, factory) = CreateInMemoryDbPair("RoadGenShortNear");
        await using (db)
        {
            await SeedAdminLocationsAsync(db);
            // A ~4.5 m stub sitting ~11 m north of the seeded network road. It
            // is short AND a network already exists, so even though it is within
            // the isolation distance it is noise, not topology: it gets dropped
            // (the weld pass re-connects genuinely dangling fragments that are
            // long enough to be roads — 4.5 m is not).
            var (userId, draftId) = await SeedCommuneAsync(
                db, LineGeoJson((2.9520, 36.7166), (2.95205, 36.7166)));
            await AddUrbanAreaAsync(db, userId);
            await SeedRoadAsync(db, userId); // east-west network at lat 36.7165
            var svc = CreateService(db, factory);

            var summary = await svc.GenerateAsync(
                UserRole, CommuneId100, null, null, userId, CommuneId100, [draftId], default);

            Assert.Empty(summary.Created);
            Assert.Equal(1, summary.Dropped);
            Assert.Equal(1, summary.Breakdown.TooShort);
            Assert.Equal(AiDraftFeature.StatusPending, await DraftStatusAsync(factory, draftId));
        }
    }

    [Fact]
    public async Task Generate_SnappedFreeEndPiece_FusesThroughCrossingIntoOneRoad()
    {
        var (db, factory) = CreateInMemoryDbPair("RoadGenSplitCrossing");
        await using (db)
        {
            await SeedAdminLocationsAsync(db);
            // The east-west draft crosses a perpendicular network road at its
            // midpoint and is split into two pieces sharing that node. The west
            // piece's free end snaps onto a SHORT parallel network stub (only a
            // few metres of it are alongside, so min-separation does not fire).
            // End-to-end the two pieces are a straight continuation, so the
            // merge fuses them back through the crossing node into one road:
            // free ends kept, junction not duplicated.
            var (userId, draftId) = await SeedCommuneAsync(
                db, LineGeoJson((2.9512, 36.7166), (2.9538, 36.7166)));
            await AddUrbanAreaAsync(db, userId);
            await SeedRoadAsync(db, userId, 2.9509, 36.7165, 2.9511, 36.7165); // short stub
            await SeedRoadAsync(db, userId, 2.9525, 36.7160, 2.9525, 36.7170); // crossing
            var svc = CreateService(db, factory);

            var summary = await svc.GenerateAsync(
                UserRole, CommuneId100, null, null, userId, CommuneId100, [draftId], default);

            Assert.Equal(1, summary.Merged);
            var road = Assert.Single(summary.Created);
            Assert.Equal(0, summary.Dropped);
            var coords = road.Data["coordinates"]!.AsArray();
            Assert.Equal(3, coords.Count);
            // West free end snapped onto the stub; middle is the crossing node.
            Assert.Equal(36.7165, coords[0]!["lat"]!.GetValue<double>(), 6);
            Assert.Equal(2.9511, coords[0]!["lng"]!.GetValue<double>(), 5);
            Assert.Equal(36.7166, coords[1]!["lat"]!.GetValue<double>(), 6);
            Assert.Equal(2.9525, coords[1]!["lng"]!.GetValue<double>(), 5);
            Assert.Equal(36.7166, coords[2]!["lat"]!.GetValue<double>(), 6);
            Assert.Equal(2.9538, coords[2]!["lng"]!.GetValue<double>(), 5);
            Assert.Equal(AiDraftFeature.StatusAccepted, await DraftStatusAsync(factory, draftId));
        }
    }

    [Fact]
    public async Task Generate_LongParallelNetworkSnap_IsDroppedAsTooClose()
    {
        var (db, factory) = CreateInMemoryDbPair("RoadGenSplitCrossing");
        await using (db)
        {
            await SeedAdminLocationsAsync(db);
            // The draft runs parallel ~11 m north of a long mapped road and is
            // cut at a crossing road. Each piece's free end snaps onto the
            // parallel road, turning both pieces into duplicate corridors — the
            // exact defect this toggle removes. (The old behaviour "split into
            // two snapped roads" created those duplicates; crossing-split +
            // merge composability itself is covered by
            // Generate_SplitPiecesWithUnsnappableFreeEnds_AreSeedsThenMerged.)
            var (userId, draftId) = await SeedCommuneAsync(
                db, LineGeoJson((2.9512, 36.7166), (2.9538, 36.7166)));
            await AddUrbanAreaAsync(db, userId);
            await SeedRoadAsync(db, userId, 2.9510, 36.7165, 2.9540, 36.7165); // long parallel
            await SeedRoadAsync(db, userId, 2.9525, 36.7160, 2.9525, 36.7170); // crossing
            var svc = CreateService(db, factory);

            var summary = await svc.GenerateAsync(
                UserRole, CommuneId100, null, null, userId, CommuneId100, [draftId], default);

            Assert.Empty(summary.Created);
            Assert.Equal(1, summary.Dropped);
            Assert.Equal(2, summary.Breakdown.TooClose);
            Assert.Equal(AiDraftFeature.StatusPending, await DraftStatusAsync(factory, draftId));
        }
    }

    [Fact]
    public async Task Generate_SplitPiecesWithUnsnappableFreeEnds_AreSeedsThenMerged()
    {
        var (db, factory) = CreateInMemoryDbPair("RoadGenSplitUnconnected");
        await using (db)
        {
            await SeedAdminLocationsAsync(db);
            // The draft crosses the network road but its free ends are ~115 m
            // from it: splitting produces two pieces whose free endpoints are
            // too far to snap. They are not dropped as dangling spurs — each
            // piece extends the commune's graph from its shared crossing node,
            // and since they are collinear they are fused back into one road by
            // the merge pass.
            var (userId, draftId) = await SeedCommuneAsync(
                db, LineGeoJson((2.9512, 36.7166), (2.9538, 36.7166)));
            await AddUrbanAreaAsync(db, userId);
            await SeedRoadAsync(db, userId, 2.9525, 36.7160, 2.9525, 36.7170); // crossing only
            var svc = CreateService(db, factory);

            var summary = await svc.GenerateAsync(
                UserRole, CommuneId100, null, null, userId, CommuneId100, [draftId], default);

            Assert.Equal(1, summary.Merged);
            var road = Assert.Single(summary.Created);
            Assert.Equal(0, summary.Dropped);
            var coords = road.Data["coordinates"]!.AsArray();
            // Both pieces share the crossing node, so the merged road keeps the
            // free ends and the shared junction once (3 vertices, no dup).
            Assert.Equal(3, coords.Count);
            Assert.Equal(36.7166, coords[0]!["lat"]!.GetValue<double>(), 6);
            Assert.Equal(2.9512, coords[0]!["lng"]!.GetValue<double>(), 5);
            Assert.Equal(36.7166, coords[1]!["lat"]!.GetValue<double>(), 6);
            Assert.Equal(2.9525, coords[1]!["lng"]!.GetValue<double>(), 5);
            Assert.Equal(36.7166, coords[2]!["lat"]!.GetValue<double>(), 6);
            Assert.Equal(2.9538, coords[2]!["lng"]!.GetValue<double>(), 5);
            Assert.Equal(AiDraftFeature.StatusAccepted, await DraftStatusAsync(factory, draftId));
        }
    }

    [Fact]
    public async Task Generate_OutOfScopeCommune_ThrowsUnauthorized()
    {
        var (db, factory) = CreateInMemoryDbPair("RoadGenOutOfScope");
        await using (db)
        {
            await SeedAdminLocationsAsync(db);
            var user = await CreateUserAsync(db, UserRole, communeId: CommuneId100);
            var svc = CreateService(db, factory, communeScope: CommuneScopeMock(allow: false));

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                svc.GenerateAsync(UserRole, CommuneId100, null, null, user.Id, CommuneId101, [Guid.NewGuid()], default));
        }
    }
}

/// <summary>
/// Unit tests for the pure planar geometry used by road generation: urban
/// containment and endpoint snapping.
/// </summary>
public class RoadGenerationGeometryTests
{
    private static readonly IReadOnlyList<(double Lat, double Lng)> Square =
    [
        (36.715, 2.950),
        (36.715, 2.960),
        (36.725, 2.960),
        (36.725, 2.950),
    ];

    [Fact]
    public void DistanceToPolygonM_Inside_IsZero()
    {
        var inside = (36.7200, 2.9550);
        Assert.Equal(0.0, RoadGenerationGeometry.DistanceToPolygonM(inside.Item1, inside.Item2, Square), 4);
    }

    [Fact]
    public void DistanceToPolygonM_Outside_MatchesMetresPerDegreeLat()
    {
        // ~0.01° south of the bottom edge ≈ 1113 m.
        var outside = (36.705, 2.9550);
        var expectedM = 0.01 * 111_320.0;
        var actualM = RoadGenerationGeometry.DistanceToPolygonM(outside.Item1, outside.Item2, Square);
        Assert.InRange(actualM, expectedM - 2.0, expectedM + 2.0);
    }

    [Fact]
    public void DistanceToPolygonM_OnEdge_IsZero()
    {
        var onEdge = (36.715, 2.9550);
        Assert.Equal(0.0, RoadGenerationGeometry.DistanceToPolygonM(onEdge.Item1, onEdge.Item2, Square), 4);
    }

    [Fact]
    public void TrySnapToNetwork_SnapsOntoNearestSegment()
    {
        IReadOnlyList<(double Lat, double Lng)> road = [(36.7165, 2.9510), (36.7165, 2.9540)];
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> network = [road];

        var ok = RoadGenerationGeometry.TrySnapToNetwork(
            36.7166, 2.9520, network, 20.0, out var snapped);

        Assert.True(ok);
        Assert.Equal(36.7165, snapped.Lat, 6);
        Assert.Equal(2.9520, snapped.Lng, 6);
    }

    [Fact]
    public void TrySnapToNetwork_BeyondMaxDistance_IsFalse()
    {
        IReadOnlyList<(double Lat, double Lng)> road = [(36.7165, 2.9510), (36.7165, 2.9540)];
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> network = [road];

        // ~500 m away: beyond any connectivity window.
        Assert.False(RoadGenerationGeometry.TrySnapToNetwork(
            36.7210, 2.9520, network, 20.0, out _));
    }

    [Fact]
    public void TrySnapToNetwork_PicksNearestAcrossRoads()
    {
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> network =
        [
            [(36.7165, 2.9510), (36.7165, 2.9540)],
            [(36.7175, 2.9510), (36.7175, 2.9540)],
        ];
        // Closer to the 36.7165 road than the 36.7175 road.
        var ok = RoadGenerationGeometry.TrySnapToNetwork(
            36.7167, 2.9520, network, 40.0, out var snapped);

        Assert.True(ok);
        Assert.Equal(36.7165, snapped.Lat, 6);
    }

    [Fact]
    public void IsNearLine_WithinRadius_IsTrue()
    {
        IReadOnlyList<(double Lat, double Lng)> line = [(36.7165, 2.9510), (36.7165, 2.9540)];
        // ~11 m north of the east-west line.
        Assert.True(RoadGenerationGeometry.IsNearLine(36.7166, 2.9520, line, 30.0));
    }

    [Fact]
    public void IsNearLine_BeyondRadius_IsFalse()
    {
        IReadOnlyList<(double Lat, double Lng)> line = [(36.7165, 2.9510), (36.7165, 2.9540)];
        // ~500 m north — outside any local-network search radius.
        Assert.False(RoadGenerationGeometry.IsNearLine(36.7210, 2.9520, line, 100.0));
    }

    [Fact]
    public void IsNearLine_EmptyOrSingleVertex_IsFalse()
    {
        Assert.False(RoadGenerationGeometry.IsNearLine(36.7166, 2.9520, [], 3000.0));
        Assert.False(RoadGenerationGeometry.IsNearLine(
            36.7166, 2.9520, [(36.7165, 2.9510)], 3000.0));
    }

    [Fact]
    public void DistanceBetweenLinesM_CrossingSegments_IsZero()
    {
        IReadOnlyList<(double Lat, double Lng)> horizontal = [(36.7165, 2.9510), (36.7165, 2.9540)];
        IReadOnlyList<(double Lat, double Lng)> vertical = [(36.7160, 2.9525), (36.7170, 2.9525)];

        Assert.Equal(0.0, RoadGenerationGeometry.DistanceBetweenLinesM(horizontal, vertical), 4);
    }

    [Fact]
    public void DistanceBetweenLinesM_ParallelSeparated_MatchesMetresPerDegree()
    {
        // 0.0001° of latitude apart ≈ 11.13 m.
        IReadOnlyList<(double Lat, double Lng)> a = [(36.7165, 2.9510), (36.7165, 2.9540)];
        IReadOnlyList<(double Lat, double Lng)> b = [(36.7166, 2.9510), (36.7166, 2.9540)];

        var distance = RoadGenerationGeometry.DistanceBetweenLinesM(a, b);

        Assert.InRange(distance, 11.0, 11.5);
    }

    [Fact]
    public void DistanceBetweenLinesM_TouchingEndpoint_IsZero()
    {
        // b hangs off the end of a but shares the endpoint (36.7165, 2.9540).
        IReadOnlyList<(double Lat, double Lng)> a = [(36.7165, 2.9510), (36.7165, 2.9540)];
        IReadOnlyList<(double Lat, double Lng)> b = [(36.7165, 2.9540), (36.7167, 2.9540)];

        Assert.Equal(0.0, RoadGenerationGeometry.DistanceBetweenLinesM(a, b), 4);
    }

    [Fact]
    public void SplitLineAtCrossings_NoNetwork_ReturnsOriginalLine()
    {
        IReadOnlyList<(double Lat, double Lng)> line = [(36.7165, 2.9510), (36.7165, 2.9540)];

        var pieces = RoadGenerationGeometry.SplitLineAtCrossings(line, []);

        var piece = Assert.Single(pieces);
        Assert.Equal(2, piece.Count);
        Assert.Equal(36.7165, piece[0].Lat, 6);
        Assert.Equal(2.9510, piece[0].Lng, 6);
        Assert.Equal(2.9540, piece[1].Lng, 6);
    }

    [Fact]
    public void SplitLineAtCrossings_SingleCrossing_SplitsIntoTwo()
    {
        // East-west line crossed in its interior by a north-south road.
        IReadOnlyList<(double Lat, double Lng)> line = [(36.7165, 2.9510), (36.7165, 2.9540)];
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> network =
        [
            [(36.7160, 2.9525), (36.7170, 2.9525)],
        ];

        var pieces = RoadGenerationGeometry.SplitLineAtCrossings(line, network);

        Assert.Equal(2, pieces.Count);
        Assert.Equal(2, pieces[0].Count);
        Assert.Equal(2, pieces[1].Count);
        // Both pieces share the crossing node; the original order is preserved.
        Assert.Equal(2.9525, pieces[0][^1].Lng, 5);
        Assert.Equal(36.7165, pieces[0][^1].Lat, 5);
        Assert.Equal(2.9525, pieces[1][0].Lng, 5);
        Assert.Equal(2.9510, pieces[0][0].Lng, 5);
        Assert.Equal(2.9540, pieces[1][^1].Lng, 5);
    }

    [Fact]
    public void SplitLineAtCrossings_MultipleCrossings_SplitsIntoThree()
    {
        IReadOnlyList<(double Lat, double Lng)> line = [(36.7165, 2.9510), (36.7165, 2.9560)];
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> network =
        [
            [(36.7160, 2.9525), (36.7170, 2.9525)],
            [(36.7160, 2.9545), (36.7170, 2.9545)],
        ];

        var pieces = RoadGenerationGeometry.SplitLineAtCrossings(line, network);

        Assert.Equal(3, pieces.Count);
        Assert.Equal(2.9525, pieces[0][^1].Lng, 5);
        Assert.Equal(2.9545, pieces[1][^1].Lng, 5);
        Assert.Equal(2.9545, pieces[2][0].Lng, 5);
        Assert.Equal(2.9560, pieces[2][^1].Lng, 5);
    }

    [Fact]
    public void SplitLineAtCrossings_EndpointTouch_DoesNotSplit()
    {
        // The line merely ends on the crossing road (a T-junction): snapping
        // merges it, there is nothing to split.
        IReadOnlyList<(double Lat, double Lng)> line = [(36.7165, 2.9510), (36.7165, 2.9525)];
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> network =
        [
            [(36.7160, 2.9525), (36.7170, 2.9525)],
        ];

        var pieces = RoadGenerationGeometry.SplitLineAtCrossings(line, network);

        var piece = Assert.Single(pieces);
        Assert.Equal(2, piece.Count);
        Assert.Equal(36.7165, piece[1].Lat, 5);
    }

    [Fact]
    public void SplitLineAtCrossings_ParallelRoad_DoesNotSplit()
    {
        // Collinear/parallel roads share no interior crossing.
        IReadOnlyList<(double Lat, double Lng)> line = [(36.7165, 2.9510), (36.7165, 2.9540)];
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> network =
        [
            [(36.7166, 2.9510), (36.7166, 2.9540)],
        ];

        var pieces = RoadGenerationGeometry.SplitLineAtCrossings(line, network);

        var piece = Assert.Single(pieces);
        Assert.Equal(2, piece.Count);
    }

    [Fact]
    public void TryExtendToNetwork_StraightAhead_HitsSegmentAtCrossing()
    {
        // Road runs west→east to (36.7166, 2.9525); a network road crosses its
        // extension dead ahead ~111 m further east. The ray must stop exactly
        // there (a T-junction), not continue past.
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> network =
        [
            [(36.7160, 2.9535), (36.7170, 2.9535)],
        ];

        var ok = RoadGenerationGeometry.TryExtendToNetwork(
            36.7166, 2.9525, 36.7166, 2.9520, network, 500.0, out var hit);

        Assert.True(ok);
        Assert.Equal(36.7166, hit.Lat, 5);
        Assert.Equal(2.9535, hit.Lng, 5);
    }

    [Fact]
    public void TryExtendToNetwork_OffBearing_DoesNotHit()
    {
        // The network road is perpendicular to the ray direction (due north of
        // the endpoint rather than ahead of it), so marching the bearing never
        // crosses it.
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> network =
        [
            [(36.7175, 2.9520), (36.7175, 2.9540)],
        ];

        Assert.False(RoadGenerationGeometry.TryExtendToNetwork(
            36.7166, 2.9525, 36.7166, 2.9520, network, 500.0, out _));
    }

    [Fact]
    public void TryExtendToNetwork_BeyondRadius_IsFalse()
    {
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> network =
        [
            [(36.7160, 2.9540), (36.7170, 2.9540)],
        ];

        // ~140 m ahead, beyond the 50 m weld radius.
        Assert.False(RoadGenerationGeometry.TryExtendToNetwork(
            36.7166, 2.9525, 36.7166, 2.9520, network, 50.0, out _));
    }

    [Fact]
    public void ProperlyCrossesAnyNetworkSegment_InteriorCrossing_IsTrue()
    {
        IReadOnlyList<(double Lat, double Lng)> line = [(36.7165, 2.9510), (36.7165, 2.9540)];
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> network =
        [
            [(36.7160, 2.9525), (36.7170, 2.9525)],
        ];

        Assert.True(RoadGenerationGeometry.ProperlyCrossesAnyNetworkSegment(line, network));
    }

    [Fact]
    public void ProperlyCrossesAnyNetworkSegment_EndpointTouch_IsFalse()
    {
        // The line merely ends on the network road (T-junction): not a pierce.
        IReadOnlyList<(double Lat, double Lng)> line = [(36.7165, 2.9510), (36.7165, 2.9525)];
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> network =
        [
            [(36.7160, 2.9525), (36.7170, 2.9525)],
        ];

        Assert.False(RoadGenerationGeometry.ProperlyCrossesAnyNetworkSegment(line, network));
    }

    [Fact]
    public void ProperlyCrossesAnyNetworkSegment_ParallelRoad_IsFalse()
    {
        IReadOnlyList<(double Lat, double Lng)> line = [(36.7165, 2.9510), (36.7165, 2.9540)];
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> network =
        [
            [(36.7166, 2.9510), (36.7166, 2.9540)],
        ];

        Assert.False(RoadGenerationGeometry.ProperlyCrossesAnyNetworkSegment(line, network));
    }

    [Fact]
    public void MergeCollinearRoads_TouchingCollinear_JoinsIntoOne()
    {
        // Two east-west roads sharing the exact endpoint 2.9525, straight
        // through: one physical road split at a node, must fuse back.
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> candidates =
        [
            [(36.7166, 2.9520), (36.7166, 2.9525)],
            [(36.7166, 2.9525), (36.7166, 2.9538)],
        ];

        var merged = RoadNetworkTopology.MergeCollinearRoads(candidates);

        var component = Assert.Single(merged);
        Assert.Equal(3, component.Vertices.Count);
        Assert.Equal([0, 1], component.MemberIndices);
        Assert.Equal(2.9520, component.Vertices[0].Lng, 5);
        Assert.Equal(2.9525, component.Vertices[1].Lng, 5);
        Assert.Equal(2.9538, component.Vertices[2].Lng, 5);
    }

    [Fact]
    public void MergeCollinearRoads_AngledAtJunction_StaysApart()
    {
        // The second road turns sharply at the shared endpoint: a real
        // junction, not a split road — must not fuse.
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> candidates =
        [
            [(36.7166, 2.9520), (36.7166, 2.9525)],
            [(36.7166, 2.9525), (36.7170, 2.9525)],
        ];

        var merged = RoadNetworkTopology.MergeCollinearRoads(candidates);

        Assert.Equal(2, merged.Count);
    }

    [Fact]
    public void MergeCollinearRoads_NearButGapped_StaysApart()
    {
        // Same line, but a 50 m gap between the endpoints: not connected.
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> candidates =
        [
            [(36.7166, 2.9520), (36.7166, 2.9525)],
            [(36.7166, 2.9530), (36.7166, 2.9538)],
        ];

        var merged = RoadNetworkTopology.MergeCollinearRoads(candidates);

        Assert.Equal(2, merged.Count);
    }

    [Fact]
    public void MergeCollinearRoads_DuplicateCorridorWithBend_DoesNotFold()
    {
        // The commune's roads 11/51: identical start P, near-identical end Q
        // (~60 µm apart), 51 detouring ~3.8 m off the P→Q chord at its mid
        // vertex. The old chain assembly spliced both in and folded the merged
        // polyline back on itself ([P,Q,mid,P]) — the 90° look-alike the user
        // reported. The fold guards must leave both detections as clean,
        // non-folded components.
        (double Lat, double Lng) p = (36.01600057935144, 7.434689104557037);
        (double Lat, double Lng) q = (36.01568384198998, 7.433299778776534);
        (double Lat, double Lng) mid = (36.015780, 7.433710);
        (double Lat, double Lng) qPrime = (36.01568382, 7.43329972);
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> candidates =
        [
            [p, q],
            [p, mid, qPrime],
        ];

        var merged = RoadNetworkTopology.MergeCollinearRoads(candidates);

        Assert.Equal(2, merged.Count);
        Assert.All(merged, c => Assert.Single(c.MemberIndices));
        Assert.All(merged, c => Assert.Equal(0, SelfOverlapCount(c.Vertices)));
    }

    [Fact]
    public void MergeCollinearRoads_SameHeadingAroundBearingWrap_JoinsIntoOne()
    {
        // Real roads 225/362: one street drawn as two collinear detections
        // sharing endpoint B and both heading ~260°. The old bearing-wrap bug
        // read the ~5° corridor separation as ~175° (355° wrapped) and never
        // fused them; the undirected line difference must.
        (double Lat, double Lng) a = (36.010823962730356, 7.418161332607269);
        (double Lat, double Lng) b = (36.0104985258095, 7.416729032993317);
        (double Lat, double Lng) c = (36.01047249085583, 7.416600286960602);
        (double Lat, double Lng) d = (36.01039004683588, 7.415988743305206);
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> candidates =
        [
            [a, b],
            [b, c, d],
        ];

        var merged = RoadNetworkTopology.MergeCollinearRoads(candidates);

        var component = Assert.Single(merged);
        Assert.Equal(4, component.Vertices.Count);
        Assert.Equal([0, 1], component.MemberIndices);
        Assert.Equal(0, SelfOverlapCount(component.Vertices));
    }

    [Fact]
    public void MergeCollinearRoads_StubOnLongBody_IndexOrderInvariant()
    {
        // A 100 m stub lying on a long road's interior. The old touch check
        // only ran a's endpoints against b, so the outcome depended on which
        // road came first in index order (a fold when the stub was first).
        // Both orders must now resolve identically: the pavement stays intact,
        // no folded polyline is produced.
        IReadOnlyList<(double Lat, double Lng)> longRoad = [(36.7155, 2.9500), (36.7155, 2.9540)];
        IReadOnlyList<(double Lat, double Lng)> stub = [(36.7155, 2.9515), (36.7155, 2.9525)];

        var forward = RoadNetworkTopology.MergeCollinearRoads([longRoad, stub]);
        var backward = RoadNetworkTopology.MergeCollinearRoads([stub, longRoad]);

        Assert.Equal(forward.Count, backward.Count);
        Assert.All(forward, c => Assert.Single(c.MemberIndices));
        Assert.All(backward, c => Assert.Single(c.MemberIndices));
        Assert.All(forward.Concat(backward), c => Assert.Equal(0, SelfOverlapCount(c.Vertices)));
    }

    private static int SelfOverlapCount(IReadOnlyList<(double Lat, double Lng)> vertices)
    {
        var count = 0;
        for (var i = 0; i < vertices.Count; i++)
        {
            for (var j = i + 2; j < vertices.Count; j++)
            {
                if (SquaredMetres(vertices[i], vertices[j]) <= 1.0)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static double SquaredMetres((double Lat, double Lng) a, (double Lat, double Lng) b)
    {
        var cosLat = Math.Cos(a.Lat * Math.PI / 180.0);
        var dLat = (b.Lat - a.Lat) * 111_320.0;
        var dLng = (b.Lng - a.Lng) * cosLat * 111_320.0;
        return dLat * dLat + dLng * dLng;
    }

    [Fact]
    public void WeldEndpoints_StraightExtension_ConnectsEndpoint()
    {
        // Endpoint at (36.7166, 2.9520) heads due east; a network road crosses
        // that bearing ~35 m ahead. The evaluate snap radius (20 m) left the
        // road detached, so only the weld pass reconnects it as a T-junction.
        var candidates = new List<List<(double Lat, double Lng)>>
        {
            new() { (36.7166, 2.9515), (36.7166, 2.9520) },
        };
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> network =
        [
            [(36.7160, 2.9524), (36.7170, 2.9524)],
        ];
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> areaRings =
        [
            [(36.700, 2.940), (36.700, 2.970), (36.740, 2.970), (36.740, 2.940)],
        ];
        var rules = new RoadRulesOptions { RoadWeldRadiusM = 50.0, MinRoadLengthM = 10.0 };

        var welded = RoadNetworkTopology.WeldEndpoints(
            candidates, network, areaRings, new ValidationOptions { RoadTurnAngleDegrees = 135.0 }, rules);

        Assert.Equal(1, welded);
        Assert.Equal(3, candidates[0].Count);
        // The new vertex shares the network road's latitude.
        Assert.Equal(36.7166, candidates[0][2].Lat, 5);
        Assert.Equal(2.9524, candidates[0][2].Lng, 5);
    }

    [Fact]
    public void WeldEndpoints_BeyondRadius_DoesNotWeld()
    {
        var candidates = new List<List<(double Lat, double Lng)>>
        {
            new() { (36.7166, 2.9515), (36.7166, 2.9520) },
        };
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> network =
        [
            [(36.7160, 2.9550), (36.7170, 2.9550)],
        ];
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> areaRings =
        [
            [(36.700, 2.940), (36.700, 2.970), (36.740, 2.970), (36.740, 2.940)],
        ];

        var welded = RoadNetworkTopology.WeldEndpoints(
            candidates, network, areaRings, new ValidationOptions { RoadTurnAngleDegrees = 135.0 },
            new RoadRulesOptions { RoadWeldRadiusM = 50.0 });

        Assert.Equal(0, welded);
        Assert.Equal(2, candidates[0].Count);
    }

    [Fact]
    public void WeldEndpoints_AlreadyTouching_DoesNotCount()
    {
        // Both endpoints already sit on a network road this run: nothing to weld.
        var candidates = new List<List<(double Lat, double Lng)>>
        {
            new() { (36.7165, 2.9515), (36.7165, 2.9520) },
        };
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> network =
        [
            [(36.7165, 2.9510), (36.7165, 2.9540)],
        ];
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> areaRings =
        [
            [(36.700, 2.940), (36.700, 2.970), (36.740, 2.970), (36.740, 2.940)],
        ];

        var welded = RoadNetworkTopology.WeldEndpoints(
            candidates, network, areaRings, new ValidationOptions { RoadTurnAngleDegrees = 135.0 },
            new RoadRulesOptions { RoadWeldRadiusM = 50.0 });

        Assert.Equal(0, welded);
    }

    [Fact]
    public void Evaluate_ParallelDuplicateWithinSeparation_IsTooClose()
    {
        // The candidate runs ~2 m south of an existing east-west road for its
        // whole length: the same street detected twice, must be pruned.
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> network =
        [
            [(36.7166, 2.9510), (36.7166, 2.9540)],
        ];
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> areaRings =
        [
            [(36.7160, 2.9500), (36.7160, 2.9550), (36.7170, 2.9550), (36.7170, 2.9500)],
        ];
        var outcome = RoadPhaseRules.Evaluate(
            [(36.71662, 2.9510), (36.71662, 2.9540)],
            confidence: 0.9,
            areaRings,
            network,
            new ValidationOptions { RoadTurnAngleDegrees = 135.0, RoadConnectivityMeters = 20.0 },
            new RoadRulesOptions { MinRoadLengthM = 10.0, MinRoadSeparationMeters = 3.5 },
            snapEndpoints: true);

        Assert.Equal(RoadPhaseViolation.TooClose, outcome.Violation);
    }

    [Fact]
    public void Evaluate_TJunctionApproach_NotTooClose()
    {
        // A road ending perpendicular on a network road (a T-junction): only a
        // ~3.5 m approach lies in the separation band, well under the 10 m
        // alongside threshold — kept.
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> network =
        [
            [(36.7160, 2.9525), (36.7170, 2.9525)],
        ];
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> areaRings =
        [
            [(36.7160, 2.9500), (36.7160, 2.9550), (36.7170, 2.9550), (36.7170, 2.9500)],
        ];
        var outcome = RoadPhaseRules.Evaluate(
            [(36.7165, 2.9510), (36.7165, 2.9525)],
            confidence: 0.9,
            areaRings,
            network,
            new ValidationOptions { RoadTurnAngleDegrees = 135.0, RoadConnectivityMeters = 20.0 },
            new RoadRulesOptions { MinRoadLengthM = 10.0, MinRoadSeparationMeters = 3.5 },
            snapEndpoints: true);

        Assert.Equal(RoadPhaseViolation.None, outcome.Violation);
    }

    [Fact]
    public void Evaluate_CollinearContinuationOfNetwork_NotTooClose()
    {
        // The candidate continues the network road end-to-end from its junction
        // (a genuine straight street the merge will fuse). Only the junction
        // approach is within the band — the rest of the road is beyond the
        // network road's extent and must not count as alongside.
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> network =
        [
            [(36.7166, 2.9525), (36.7166, 2.9540)],
        ];
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> areaRings =
        [
            [(36.7160, 2.9500), (36.7160, 2.9550), (36.7170, 2.9550), (36.7170, 2.9500)],
        ];
        var outcome = RoadPhaseRules.Evaluate(
            [(36.7166, 2.9510), (36.7166, 2.9525)],
            confidence: 0.9,
            areaRings,
            network,
            new ValidationOptions { RoadTurnAngleDegrees = 135.0, RoadConnectivityMeters = 20.0 },
            new RoadRulesOptions { MinRoadLengthM = 10.0, MinRoadSeparationMeters = 3.5 },
            snapEndpoints: true);

        Assert.Equal(RoadPhaseViolation.None, outcome.Violation);
    }

    [Fact]
    public void Evaluate_BootstrapEmptyNetwork_NotTooClose()
    {
        // No network yet (first roads of a commune): nothing to be too close to.
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> areaRings =
        [
            [(36.7160, 2.9500), (36.7160, 2.9550), (36.7170, 2.9550), (36.7170, 2.9500)],
        ];
        var outcome = RoadPhaseRules.Evaluate(
            [(36.7165, 2.9510), (36.7165, 2.9525)],
            confidence: 0.9,
            areaRings,
            [],
            new ValidationOptions { RoadTurnAngleDegrees = 135.0, RoadConnectivityMeters = 20.0 },
            new RoadRulesOptions { MinRoadLengthM = 10.0, MinRoadSeparationMeters = 3.5 },
            snapEndpoints: true);

        Assert.Equal(RoadPhaseViolation.None, outcome.Violation);
    }
}
