using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.Models;
using NetTopologySuite.Geometries;
using NetTopologySuite;
using static NarsApi.Tests.TestData;

namespace NarsApi.Tests;

public static class SeedData
{
    private static readonly GeometryFactory GeoFactory =
        NtsGeometryServices.Instance.CreateGeometryFactory(4326);

    public static Polygon Rectangle(double minLon, double minLat, double maxLon, double maxLat) =>
        GeoFactory.CreatePolygon([
            new Coordinate(minLon, minLat),
            new Coordinate(maxLon, minLat),
            new Coordinate(maxLon, maxLat),
            new Coordinate(minLon, maxLat),
            new Coordinate(minLon, minLat),
        ]);

    public static async Task SeedBasicLocationsAsync(AppDbContext db)
    {
        if (await db.Communes.AnyAsync())
        {
            return;
        }

        AddAlgerLocations(db, includeBoundary: true);
        await db.SaveChangesAsync();
    }

    public static async Task SeedExtendedLocationsAsync(AppDbContext db)
    {
        if (await db.Communes.AnyAsync())
        {
            return;
        }

        AddAlgerLocations(db);
        db.Wilayas.Add(new Wilaya { WilayaId = WilayaId2, WilayaFr = "Blida", WilayaAr = "البليدة", WilayaLatitude = 36.47, WilayaLongitude = 2.83 });
        db.Dairas.Add(new Daira { DairaId = 2, WilayaId = WilayaId2, DairaFr = "Blida Centre", DairaAr = "وسط البليدة", DairaLatitude = 36.47, DairaLongitude = 2.82 });
        db.Communes.Add(new Commune { CommuneId = CommuneId2, DairaId = 2, CommuneCode = 2001, CommuneFr = "Blida Centre", CommuneAr = "وسط البليدة", CommuneLatitude = 36.47, CommuneLongitude = 2.82 });
        await db.SaveChangesAsync();
    }

    private static void AddAlgerLocations(AppDbContext db, bool includeBoundary = false)
    {
        db.Wilayas.Add(new Wilaya { WilayaId = WilayaId1, WilayaFr = "Alger", WilayaAr = "الجزائر", WilayaLatitude = 36.75, WilayaLongitude = 3.05 });
        db.Dairas.Add(new Daira { DairaId = 1, WilayaId = WilayaId1, DairaFr = "Draria", DairaAr = "درارية", DairaLatitude = 36.72, DairaLongitude = 2.96 });
        db.Communes.Add(new Commune { CommuneId = CommuneId1, DairaId = 1, CommuneCode = 1001, CommuneFr = "Draria Centre", CommuneAr = "درارية الوسطى", CommuneLatitude = 36.72, CommuneLongitude = 2.96 });
        if (includeBoundary)
        {
            db.CommuneBoundaries.Add(new CommuneBoundary { CommuneId = CommuneId1, Geometry = Rectangle(2.90, 36.70, 3.00, 36.80) });
        }
    }

    public static async Task SeedAdminLocationsAsync(AppDbContext db)
    {
        if (!await db.Wilayas.AnyAsync(w => w.WilayaId == 1))
        {
            db.Wilayas.Add(new Wilaya { WilayaId = WilayaId1, WilayaFr = "Alger", WilayaAr = "الجزائر", WilayaLatitude = 36.75, WilayaLongitude = 3.05 });
        }

        if (!await db.Wilayas.AnyAsync(w => w.WilayaId == 2))
        {
            db.Wilayas.Add(new Wilaya { WilayaId = WilayaId2, WilayaFr = "Blida", WilayaAr = "البليدة", WilayaLatitude = 36.47, WilayaLongitude = 2.83 });
        }

        if (!await db.Dairas.AnyAsync(d => d.DairaId == 10))
        {
            db.Dairas.Add(new Daira { DairaId = DairaId10, WilayaId = WilayaId1, DairaFr = "Draria", DairaAr = "درارية", DairaLatitude = 36.72, DairaLongitude = 2.96 });
        }

        if (!await db.Dairas.AnyAsync(d => d.DairaId == 11))
        {
            db.Dairas.Add(new Daira { DairaId = DairaId11, WilayaId = WilayaId2, DairaFr = "Blida Centre", DairaAr = "وسط البليدة", DairaLatitude = 36.47, DairaLongitude = 2.82 });
        }

        if (!await db.Communes.AnyAsync(c => c.CommuneId == 100))
        {
            db.Communes.Add(new Commune { CommuneId = CommuneId100, DairaId = DairaId10, CommuneCode = 1001, CommuneFr = "Draria Centre", CommuneAr = "درارية الوسطى", CommuneLatitude = 36.72, CommuneLongitude = 2.96 });
        }

        if (!await db.Communes.AnyAsync(c => c.CommuneId == 101))
        {
            db.Communes.Add(new Commune { CommuneId = CommuneId101, DairaId = DairaId11, CommuneCode = 2001, CommuneFr = "Blida Centre", CommuneAr = "وسط البليدة", CommuneLatitude = 36.47, CommuneLongitude = 2.82 });
        }
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Creates a single pending road draft for <paramref name="communeId"/>.
    /// Shared by the InMemory unit suite and the PostgreSQL integration suite so
    /// the two stay in sync. Returns the draft's id.
    /// </summary>
    public static async Task<Guid> AddDraftAsync(AppDbContext db, int communeId)
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

    public static async Task<User> CreateUserAsync(AppDbContext db, string role,
        int? communeId = null, int? dairaId = null, int? wilayaId = null,
        string? name = null, Guid? id = null, string? username = null,
        string? securityStamp = null, string? email = null)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var user = new User
        {
            Id = id ?? Guid.NewGuid(),
            Name = name ?? $"User {suffix[..8]}",
            Email = email ?? $"{suffix[..12]}@test.com",
            Phone = TestData.DefaultPhone,
            Username = username ?? $"user_{suffix[..12]}",
            PasswordHash = DefaultPasswordHash,
            Role = role,
            CommuneId = communeId,
            DairaId = dairaId,
            WilayaId = wilayaId,
            SecurityStamp = securityStamp ?? User.GenerateSecurityStamp(),
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }
}
