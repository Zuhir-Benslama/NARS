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
            var (userId, draftId) = await SeedCommuneAsync(
                db, LineGeoJson((2.9512, 36.7166), (2.9538, 36.7166)));
            await AddUrbanAreaAsync(db, userId);
            await SeedRoadAsync(db, userId); // east-west network at lat 36.7165
            var svc = CreateService(db, factory);

            // Both endpoints sit ~11 m off the network (within 20 m), so they
            // are snapped onto lat 36.7165; endpoints further away would drop.
            var summary = await svc.GenerateAsync(
                UserRole, CommuneId100, null, null, userId, CommuneId100, [draftId], default);

            var road = Assert.Single(summary.Created);
            var coords = road.Data["coordinates"]!.AsArray();
            Assert.Equal(2, coords.Count);
            Assert.Equal(36.7165, coords[0]!["lat"]!.GetValue<double>(), 6);
            Assert.Equal(36.7165, coords[1]!["lat"]!.GetValue<double>(), 6);
        }
    }

    [Fact]
    public async Task Generate_UnconnectableEndpoint_DropsAndLeavesDraftPending()
    {
        var (db, factory) = CreateInMemoryDbPair("RoadGenUnconnected");
        await using (db)
        {
            await SeedAdminLocationsAsync(db);
            // Inside the urban area but ~500 m from the seeded network.
            var (userId, draftId) = await SeedCommuneAsync(
                db, LineGeoJson((2.9510, 36.7210), (2.9520, 36.7210)));
            await AddUrbanAreaAsync(db, userId);
            await SeedRoadAsync(db, userId);
            var svc = CreateService(db, factory);

            var summary = await svc.GenerateAsync(
                UserRole, CommuneId100, null, null, userId, CommuneId100, [draftId], default);

            Assert.Empty(summary.Created);
            Assert.Equal(1, summary.Dropped);
            Assert.Equal(AiDraftFeature.StatusPending, await DraftStatusAsync(factory, draftId));
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
    public async Task Generate_LocalAndRemoteRoads_StillRequiresConnectionToLocal()
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

            // A local mapped road still gates connectivity (and wins snapping),
            // despite a second, far-away road co-existing in the commune.
            var summary = await svc.GenerateAsync(
                UserRole, CommuneId100, null, null, userId, CommuneId100, [draftId], default);

            var road = Assert.Single(summary.Created);
            var coords = road.Data["coordinates"]!.AsArray();
            Assert.Equal(36.7165, coords[0]!["lat"]!.GetValue<double>(), 6);
            Assert.Equal(36.7165, coords[1]!["lat"]!.GetValue<double>(), 6);
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
            Assert.Equal(AiDraftFeature.StatusAccepted, await DraftStatusAsync(factory, okId));
            Assert.Equal(AiDraftFeature.StatusPending, await DraftStatusAsync(factory, outId));
            Assert.Equal(AiDraftFeature.StatusPending, await DraftStatusAsync(factory, turnId));
            Assert.Equal(AiDraftFeature.StatusPending, await DraftStatusAsync(factory, lowConfId));
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
}
