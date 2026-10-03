using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests.Service;

/// <summary>
/// Integration tests for the districts generation phase against real
/// PostgreSQL + PostGIS. The partition is one SQL statement (GEOS
/// polygonize over a noded network), so it cannot be covered by the in-memory
/// seam — these tests assert the three district invariants the manual
/// validation rules demand: no overlap, gapless coverage of the urban zone,
/// and every district touching a sibling.
/// </summary>
[Collection(PostgreSqlCollection.CollectionName)]
[Trait("Category", "Service")]
public class DistrictGenerationServiceTests(NarsDatabaseFixture fixture) : ServiceTestBase(fixture)
{
    /// <summary>
    /// Square urban zone, lat 36.0000..36.0100 (≈1106 m), lng 3.0000..3.0200
    /// (≈1801 m at this latitude), so the whole zone is ≈1.99 km².
    /// </summary>
    private const double ZoneSouth = 36.0000;
    private const double ZoneNorth = 36.0100;
    private const double ZoneWest = 3.0000;
    private const double ZoneEast = 3.0200;

    private static DistrictGenerationService CreateService(
        IDbContextFactory<AppDbContext> factory, double minAreaM2 = 50_000)
    {
        return new DistrictGenerationService(
            factory,
            new CommuneScopeService(factory),
            Mock.Of<IDateTimeProvider>(x => x.UtcNow == FixedUtcNow),
            Options.Create(new DistrictGenerationOptions { MinDistrictAreaM2 = minAreaM2 }),
            Mock.Of<ILogger<DistrictGenerationService>>());
    }

    private static async Task<Guid> SeedOwnerAsync(AppDbContext db, int communeId)
    {
        var owner = await SeedData.CreateUserAsync(db, UserRoles.CommuneUser, communeId: communeId);
        await db.SaveChangesAsync();
        return owner.Id;
    }

    private static void AddUrbanZone(AppDbContext db, Guid ownerId)
    {
        db.Areas.Add(new Area
        {
            Id = Guid.CreateVersion7(),
            UserId = ownerId,
            Layer = FeatureTypes.AreaLayers.CentralUrban,
            Label = "Zone",
            Data = $$"""{"type":"areas","label":"","areaTypeKey":"central_urban","coordinates":[{"lat":{{ZoneSouth}},"lng":{{ZoneWest}}},{"lat":{{ZoneSouth}},"lng":{{ZoneEast}}},{"lat":{{ZoneNorth}},"lng":{{ZoneEast}}},{"lat":{{ZoneNorth}},"lng":{{ZoneWest}}}]}""",
            CreatedAt = FixedUtcNow,
        });
    }

    /// <summary>East-west cut spanning the zone (a boulevard by default).</summary>
    private static void AddEastWestRoad(
        AppDbContext db, Guid ownerId, double lat, string layer = FeatureTypes.RoadLayers.Boulevard)
    {
        db.Roads.Add(new Road
        {
            Id = Guid.CreateVersion7(),
            UserId = ownerId,
            Layer = layer,
            Label = "Cut",
            Data = $$"""{"type":"roads","label":"","roadTypeKey":"{{layer}}","coordinates":[{"lat":{{lat}},"lng":{{ZoneWest}}},{"lat":{{lat}},"lng":{{ZoneEast}}}]}""",
            CreatedAt = FixedUtcNow,
        });
    }

    /// <summary>North-south cut spanning the zone (an avenue by default).</summary>
    private static void AddNorthSouthRoad(
        AppDbContext db, Guid ownerId, double lng, string layer = FeatureTypes.RoadLayers.Avenue)
    {
        db.Roads.Add(new Road
        {
            Id = Guid.CreateVersion7(),
            UserId = ownerId,
            Layer = layer,
            Label = "Cut",
            Data = $$"""{"type":"roads","label":"","roadTypeKey":"{{layer}}","coordinates":[{"lat":{{ZoneSouth}},"lng":{{lng}}},{"lat":{{ZoneNorth}},"lng":{{lng}}}]}""",
            CreatedAt = FixedUtcNow,
        });
    }

    [Fact]
    public async Task GenerateAsync_ThrowsWhenCommuneHasNoUrbanArea()
    {
        await using var seedDb = Fixture.CreateDbContext();
        var ownerId = await SeedOwnerAsync(seedDb, CommuneId100);
        AddEastWestRoad(seedDb, ownerId, ZoneSouth + 0.005);
        await seedDb.SaveChangesAsync();

        var svc = CreateService(Fixture.CreateDbContextFactory());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.GenerateAsync(UserRoles.NationalAdmin, null, null, null, CommuneId100));
        Assert.Contains("urban area", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateAsync_ThrowsWhenCommuneHasNoPrimaryRoad()
    {
        await using var seedDb = Fixture.CreateDbContext();
        var ownerId = await SeedOwnerAsync(seedDb, CommuneId100);
        AddUrbanZone(seedDb, ownerId);
        // A street is not a cutting layer: the pass must still refuse rather
        // than hand back one district covering the whole zone.
        AddEastWestRoad(seedDb, ownerId, ZoneSouth + 0.005, FeatureTypes.RoadLayers.Street);
        await seedDb.SaveChangesAsync();

        var svc = CreateService(Fixture.CreateDbContextFactory());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.GenerateAsync(UserRoles.NationalAdmin, null, null, null, CommuneId100));
        Assert.Contains("boulevard", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("avenue", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateAsync_ThrowsWhenCallerCannotAccessCommune()
    {
        await using var seedDb = Fixture.CreateDbContext();
        var ownerId = await SeedOwnerAsync(seedDb, CommuneId100);
        AddUrbanZone(seedDb, ownerId);
        AddEastWestRoad(seedDb, ownerId, ZoneSouth + 0.005);
        await seedDb.SaveChangesAsync();

        var svc = CreateService(Fixture.CreateDbContextFactory());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            svc.GenerateAsync(UserRoles.CommuneUser, CommuneId101, null, null, CommuneId100));
    }

    [Fact]
    public async Task GenerateAsync_CrossingPrimaryRoads_ProducesFourGaplessNonOverlappingDistricts()
    {
        await using var seedDb = Fixture.CreateDbContext();
        var ownerId = await SeedOwnerAsync(seedDb, CommuneId100);
        AddUrbanZone(seedDb, ownerId);
        AddEastWestRoad(seedDb, ownerId, ZoneSouth + 0.005, FeatureTypes.RoadLayers.Boulevard);
        AddNorthSouthRoad(seedDb, ownerId, ZoneWest + 0.010, FeatureTypes.RoadLayers.Avenue);
        await seedDb.SaveChangesAsync();

        var svc = CreateService(Fixture.CreateDbContextFactory());

        var summary = await svc.GenerateAsync(UserRoles.NationalAdmin, null, null, null, CommuneId100);

        Assert.Equal(4, summary.Drafts.Count);
        Assert.Equal(1, summary.UrbanAreaCount);
        Assert.Equal(2, summary.PrimaryRoadCount);
        Assert.Equal(0, summary.AbsorbedSlivers);

        // Every piece is a single valid polygon carrying a real area.
        Assert.All(summary.Drafts, d =>
        {
            Assert.Contains("\"Polygon\"", d.GeoJson, StringComparison.Ordinal);
            Assert.DoesNotContain("MultiPolygon", d.GeoJson, StringComparison.Ordinal);
            Assert.True(d.AreaM2 > 0, $"district {d.DraftId} has area {d.AreaM2}");
            Assert.InRange(d.Lat, ZoneSouth, ZoneNorth);
            Assert.InRange(d.Lng, ZoneWest, ZoneEast);
        });

        // Invariant 1: the pieces tile the zone, so their areas sum to it.
        var zoneAreaM2 = await ReadZoneAreaAsync();
        Assert.Equal(zoneAreaM2, summary.Drafts.Sum(d => d.AreaM2), 3);

        // Invariant 2 + 3, measured in PostGIS rather than asserted from the
        // same numbers the generator returned.
        var (overlappingPairs, uncoveredM2) = await MeasurePartitionQualityAsync(summary.Drafts);
        Assert.Equal(0, overlappingPairs);
        Assert.True(uncoveredM2 < 10, $"uncovered area {uncoveredM2} m² exceeds the 10 m² tolerance");

        // A 2x2 grid has no interior face: each cell shares a positive-length
        // edge with two siblings and only point-touches the diagonal one. Both
        // counts are asserted because production accepts a district when
        // ST_Touches finds any sibling (ValidationService.CheckDistrictAdjacencyAsync),
        // so the touching count is the one that must never drop below 1.
        var adjacency = await CountAdjacenciesAsync(summary.Drafts);
        Assert.All(summary.Drafts, d =>
        {
            Assert.Equal(2, adjacency[d.DraftId].EdgeNeighbours);
            Assert.Equal(3, adjacency[d.DraftId].TouchingNeighbours);
        });
    }

    [Fact]
    public async Task GenerateAsync_WritesPendingDistrictDraftsWithGenerationSource()
    {
        await using var seedDb = Fixture.CreateDbContext();
        var ownerId = await SeedOwnerAsync(seedDb, CommuneId100);
        AddUrbanZone(seedDb, ownerId);
        AddEastWestRoad(seedDb, ownerId, ZoneSouth + 0.005);
        await seedDb.SaveChangesAsync();

        var svc = CreateService(Fixture.CreateDbContextFactory());

        var summary = await svc.GenerateAsync(UserRoles.NationalAdmin, null, null, null, CommuneId100);
        Assert.Equal(2, summary.Drafts.Count);

        await using var verifyDb = Fixture.CreateDbContext();
        var drafts = await verifyDb.AiDraftFeatures.AsNoTracking()
            .Where(d => summary.Drafts.Select(s => s.DraftId).Contains(d.Id))
            .ToListAsync();

        Assert.Equal(2, drafts.Count);
        Assert.All(drafts, d =>
        {
            Assert.Equal(AiDraftFeature.TypeDistrict, d.FeatureType);
            Assert.Equal(AiDraftFeature.StatusPending, d.Status);
            Assert.Equal(AiDraftFeature.SourceDistrictPartition, d.Source);
            Assert.Equal(CommuneId100, d.CommuneId);
        });

        // Drafts only — nothing is materialized into districts until review.
        Assert.False(await verifyDb.Districts.AsNoTracking().AnyAsync());
    }

    [Fact]
    public async Task GenerateAsync_ParallelBoulevards_AbsorbThinStripAndStayGapless()
    {
        await using var seedDb = Fixture.CreateDbContext();
        var ownerId = await SeedOwnerAsync(seedDb, CommuneId100);
        AddUrbanZone(seedDb, ownerId);
        // Two boulevards ~1.1 m apart carve a ~2000 m² strip, which is under the
        // 5000 m² floor and so must be absorbed into a neighbour rather than
        // emitted as its own sliver district.
        AddEastWestRoad(seedDb, ownerId, ZoneSouth + 0.004, FeatureTypes.RoadLayers.Boulevard);
        AddEastWestRoad(seedDb, ownerId, ZoneSouth + 0.00401, FeatureTypes.RoadLayers.Boulevard);
        await seedDb.SaveChangesAsync();

        var svc = CreateService(Fixture.CreateDbContextFactory(), minAreaM2: 5_000);

        var summary = await svc.GenerateAsync(UserRoles.NationalAdmin, null, null, null, CommuneId100);

        Assert.Equal(1, summary.AbsorbedSlivers);
        Assert.Equal(2, summary.Drafts.Count);
        Assert.All(summary.Drafts, d => Assert.True(d.AreaM2 >= 5_000));

        // Absorption must not punch a hole or leave a sliver behind.
        var zoneAreaM2 = await ReadZoneAreaAsync();
        Assert.Equal(zoneAreaM2, summary.Drafts.Sum(d => d.AreaM2), 3);
        var (overlappingPairs, uncoveredM2) = await MeasurePartitionQualityAsync(summary.Drafts);
        Assert.Equal(0, overlappingPairs);
        Assert.True(uncoveredM2 < 10, $"uncovered area {uncoveredM2} m² exceeds the 10 m² tolerance");
    }

    [Fact]
    public async Task GenerateAsync_HugeMinimumArea_KeepsEverySliverRatherThanPunchingHoles()
    {
        await using var seedDb = Fixture.CreateDbContext();
        var ownerId = await SeedOwnerAsync(seedDb, CommuneId100);
        AddUrbanZone(seedDb, ownerId);
        AddEastWestRoad(seedDb, ownerId, ZoneSouth + 0.005);
        await seedDb.SaveChangesAsync();

        // Nothing qualifies as "kept", so every piece is an orphan sliver. They
        // must all survive: dropping them would leave the zone uncovered.
        var svc = CreateService(Fixture.CreateDbContextFactory(), minAreaM2: 500_000_000);

        var summary = await svc.GenerateAsync(UserRoles.NationalAdmin, null, null, null, CommuneId100);

        Assert.Equal(2, summary.Drafts.Count);
        Assert.Equal(0, summary.AbsorbedSlivers);
        var (overlappingPairs, uncoveredM2) = await MeasurePartitionQualityAsync(summary.Drafts);
        Assert.Equal(0, overlappingPairs);
        Assert.True(uncoveredM2 < 10, $"uncovered area {uncoveredM2} m² exceeds the 10 m² tolerance");
    }

    [Fact]
    public async Task GenerateAsync_IsIdempotentAcrossTwoCommunesScopedToTheirOwnRoads()
    {
        await using var seedDb = Fixture.CreateDbContext();
        var owner100 = await SeedOwnerAsync(seedDb, CommuneId100);
        var owner101 = await SeedData.CreateUserAsync(seedDb, UserRoles.CommuneUser, communeId: CommuneId101);
        AddUrbanZone(seedDb, owner100);
        AddEastWestRoad(seedDb, owner100, ZoneSouth + 0.005);
        AddUrbanZone(seedDb, owner101.Id);
        AddEastWestRoad(seedDb, owner101.Id, ZoneSouth + 0.002);
        AddEastWestRoad(seedDb, owner101.Id, ZoneNorth - 0.002);
        await seedDb.SaveChangesAsync();

        var svc = CreateService(Fixture.CreateDbContextFactory());

        var first = await svc.GenerateAsync(UserRoles.NationalAdmin, null, null, null, CommuneId100);
        var second = await svc.GenerateAsync(UserRoles.NationalAdmin, null, null, null, CommuneId101);

        Assert.Equal(2, first.Drafts.Count);
        Assert.Equal(3, second.Drafts.Count);
        Assert.Equal(1, first.PrimaryRoadCount);
        Assert.Equal(2, second.PrimaryRoadCount);
        Assert.Empty(first.Drafts.Select(d => d.DraftId).Intersect(second.Drafts.Select(d => d.DraftId)));
    }

    protected override async Task SeedAsync()
    {
        // users.commune_id references communes, so the reference rows must exist
        // before any owner or district draft is created.
        await SeedData.SeedAdminLocationsAsync(Db);
    }

    /// <summary>
    /// The generated drafts as a PostGIS CTE, read back from the rows the
    /// generator stored rather than from the geometries it returned. Measuring
    /// what was persisted keeps the invariant checks independent of the
    /// generator's own arithmetic.
    /// </summary>
    private static string DraftsCte(IReadOnlyList<GeneratedDistrictDraft> drafts, string alias)
    {
        var ids = string.Join(",", drafts.Select(d => $"'{d.DraftId}'"));
        return $"{alias} AS (SELECT id, ST_SetSRID(ST_GeomFromGeoJSON(geometry), 4326) AS geom "
            + $"FROM ai_draft_features WHERE id IN ({ids}))";
    }

    /// <summary>
    /// Area of the seeded urban zone in m², read back from the areas table with
    /// the same geometry fragment the service uses, so the coverage assertion
    /// compares like with like.
    /// </summary>
    private Task<double> ReadZoneAreaAsync() => ScalarAsync(
        $"SELECT COALESCE(SUM(ST_Area(({SqlFragments.PolygonFromDataWithAlias("a")})::geography)), 0) "
        + "FROM areas a JOIN users u ON a.user_id = u.id "
        + $"WHERE u.commune_id = {CommuneId100} AND a.layer = '{FeatureTypes.AreaLayers.CentralUrban}'");

    /// <summary>
    /// Counts district pairs whose interiors overlap and the area of the zone no
    /// district covers. A shared edge is not an overlap, hence the interior-only
    /// 'T********' mask rather than ST_Intersects (which is what makes the manual
    /// validate path reject generated neighbours).
    /// </summary>
    private async Task<(int OverlappingPairs, double UncoveredM2)> MeasurePartitionQualityAsync(
        IReadOnlyList<GeneratedDistrictDraft> drafts)
    {
        var overlappingPairs = (int)await ScalarAsync(
            $"WITH {DraftsCte(drafts, "a")}, {DraftsCte(drafts, "b")} "
            + "SELECT COUNT(*) FROM a JOIN b ON a.id <> b.id "
            + "WHERE ST_Relate(a.geom, b.geom, 'T********')");

        // Coverage uses the union so it stays correct even when pairs overlap;
        // the assertion on overlappingPairs is what rules that case out.
        var zoneAreaM2 = await ReadZoneAreaAsync();
        var unionAreaM2 = await ScalarAsync(
            $"WITH {DraftsCte(drafts, "d")} "
            + "SELECT COALESCE(ST_Area(ST_UnaryUnion(ST_Collect(geom))::geography), 0) FROM d");

        return (overlappingPairs, Math.Max(0, zoneAreaM2 - unionAreaM2));
    }

    /// <summary>
    /// Neighbour counts per district: siblings sharing a positive-length edge,
    /// and siblings merely touching (edge or point) the way the production
    /// adjacency rule counts them.
    /// </summary>
    private async Task<Dictionary<Guid, (int EdgeNeighbours, int TouchingNeighbours)>> CountAdjacenciesAsync(
        IReadOnlyList<GeneratedDistrictDraft> drafts)
    {
        var counts = new Dictionary<Guid, (int EdgeNeighbours, int TouchingNeighbours)>();

        await using var reader = await ExecuteAsync(
            $"WITH {DraftsCte(drafts, "a")}, {DraftsCte(drafts, "b")} "
            + "SELECT a.id,"
            + " COUNT(*) FILTER (WHERE ST_Length("
            + "ST_Intersection(ST_Boundary(a.geom), ST_Boundary(b.geom))::geography) > 0),"
            + " COUNT(*) FILTER (WHERE ST_Touches(a.geom, b.geom)) "
            + "FROM a JOIN b ON a.id <> b.id GROUP BY a.id");
        while (await reader.ReadAsync())
        {
            counts[reader.GetGuid(0)] = (reader.GetInt32(1), reader.GetInt32(2));
        }

        return counts;
    }

    private async Task<double> ScalarAsync(string sql) =>
        Convert.ToDouble(await ExecuteScalarAsync(sql));

    private async Task<object?> ExecuteScalarAsync(string sql)
    {
        await using var conn = Fixture.CreateDbContext().Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return await cmd.ExecuteScalarAsync();
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    private async Task<DbDataReader> ExecuteAsync(string sql)
    {
        var conn = Fixture.CreateDbContext().Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return await cmd.ExecuteReaderAsync();
        }
        catch
        {
            await conn.CloseAsync();
            throw;
        }
    }
}
