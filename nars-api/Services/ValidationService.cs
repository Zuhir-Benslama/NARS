using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

public class ValidationService(AppDbContext db) : IValidationService
{
    private static string RoadTable => FeatureTypeRegistry.GetDescriptor(FeatureTypes.Road)?.TableName
        ?? throw new InvalidOperationException("FeatureTypeRegistry missing Road descriptor");
    private static string AreaTable => FeatureTypeRegistry.GetDescriptor(FeatureTypes.Area)?.TableName
        ?? throw new InvalidOperationException("FeatureTypeRegistry missing Area descriptor");
    private static string DistrictTable => FeatureTypeRegistry.GetDescriptor(FeatureTypes.District)?.TableName
        ?? throw new InvalidOperationException("FeatureTypeRegistry missing District descriptor");

    public async Task<bool> CheckRoadConnectivityAsync(Guid userId, string wkt, double maxDistanceMeters, CancellationToken ct = default)
    {
        var conn = db.Database.GetDbConnection();
        await using var handle = await conn.EnsureOpenAsync(ct);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT EXISTS (
                SELECT 1
                FROM {RoadTable} f
                WHERE f.user_id = @uid
                  AND ST_DWithin(
                        ({SqlFragments.LineStringFromData})::geography,
                        ST_SetSRID(ST_GeomFromText(@wkt), 4326)::geography,
                        {maxDistanceMeters}
                      )
            )";
        SqlFragments.AddParam(cmd, "@uid", userId);
        SqlFragments.AddParam(cmd, "@wkt", wkt);

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToBoolean(result);
    }

    public async Task<bool> CheckDistrictCoverageAsync(Guid userId, double toleranceMeters, CancellationToken ct = default)
    {
        var conn = db.Database.GetDbConnection();
        await using var handle = await conn.EnsureOpenAsync(ct);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            WITH
            urban AS (
                SELECT ST_Union({SqlFragments.PolygonFromData}) AS geom
                FROM {AreaTable} f
                WHERE f.user_id = @uid
                  AND f.layer  IN ('central_urban', 'secondary_urban')
            ),
            districts AS (
                SELECT ST_Union({SqlFragments.PolygonFromData}) AS geom
                FROM {DistrictTable} f
                WHERE f.user_id = @uid
            )
            SELECT ST_Covers(
                ST_Buffer(districts.geom::geography, {toleranceMeters})::geometry,
                urban.geom
            )
            FROM urban, districts
            WHERE urban.geom IS NOT NULL AND districts.geom IS NOT NULL";
        SqlFragments.AddParam(cmd, "@uid", userId);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is bool b && b;
    }

    public async Task<bool> CheckDistrictOverlapAsync(Guid userId, string wkt, CancellationToken ct = default)
    {
        var conn = db.Database.GetDbConnection();
        await using var handle = await conn.EnsureOpenAsync(ct);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT EXISTS (
                SELECT 1 FROM {DistrictTable} f
                WHERE f.user_id = @uid
                  AND ST_Intersects(
                        ({SqlFragments.PolygonFromData}),
                        ST_SetSRID(ST_GeomFromText(@wkt), 4326)
                      )
            )";
        SqlFragments.AddParam(cmd, "@uid", userId);
        SqlFragments.AddParam(cmd, "@wkt", wkt);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is bool b && b;
    }

    public async Task<long> CountSiblingsInSameAreaAsync(Guid userId, string wkt, CancellationToken ct = default)
    {
        var conn = db.Database.GetDbConnection();
        await using var handle = await conn.EnsureOpenAsync(ct);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            WITH area_geom AS (
                SELECT {SqlFragments.PolygonFromDataWithAlias("a")} AS geom
                FROM {AreaTable} a
                WHERE a.user_id = @uid
                  AND a.layer IN ('central_urban', 'secondary_urban')
            )
            SELECT COUNT(*) FROM {DistrictTable} d
            WHERE d.user_id = @uid
              AND EXISTS (
                  SELECT 1 FROM area_geom ag
                  WHERE ST_Intersects(ag.geom, ST_SetSRID(ST_GeomFromText(@wkt), 4326))
                    AND ST_Intersects(ag.geom, ({SqlFragments.PolygonFromDataWithAlias("d")}))
              )";
        SqlFragments.AddParam(cmd, "@uid", userId);
        SqlFragments.AddParam(cmd, "@wkt", wkt);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task<bool> CheckDistrictAdjacencyAsync(Guid userId, string wkt, CancellationToken ct = default)
    {
        var conn = db.Database.GetDbConnection();
        await using var handle = await conn.EnsureOpenAsync(ct);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            WITH district_geom AS (
                SELECT {SqlFragments.PolygonFromData} AS geom
                FROM {DistrictTable} f
                WHERE f.user_id = @uid
            ),
            area_geom AS (
                SELECT {SqlFragments.PolygonFromDataWithAlias("a")} AS geom
                FROM {AreaTable} a
                WHERE a.user_id = @uid
                  AND a.layer IN ('central_urban', 'secondary_urban')
            )
            SELECT EXISTS (
                SELECT 1 FROM district_geom dg
                WHERE ST_Touches(ST_SetSRID(ST_GeomFromText(@wkt), 4326), dg.geom)
                   OR ST_Intersects(ST_Boundary(ST_SetSRID(ST_GeomFromText(@wkt), 4326)), ST_Boundary(dg.geom))
                  AND EXISTS (
                      SELECT 1 FROM area_geom ag
                      WHERE ST_Intersects(ag.geom, ST_SetSRID(ST_GeomFromText(@wkt), 4326))
                        AND ST_Intersects(ag.geom, dg.geom)
                  )
            )";
        SqlFragments.AddParam(cmd, "@uid", userId);
        SqlFragments.AddParam(cmd, "@wkt", wkt);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is bool b && b;
    }
}
