using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

public class ValidationService : IValidationService
{
    private readonly AppDbContext _db;
    private readonly string _roadTable;
    private readonly string _areaTable;
    private readonly string _districtTable;

    public ValidationService(AppDbContext db)
    {
        _db = db;
        _roadTable = FeatureTypeRegistry.GetDescriptor(FeatureTypes.Road)?.TableName
            ?? throw new InvalidOperationException("FeatureTypeRegistry missing Road descriptor");
        _areaTable = FeatureTypeRegistry.GetDescriptor(FeatureTypes.Area)?.TableName
            ?? throw new InvalidOperationException("FeatureTypeRegistry missing Area descriptor");
        _districtTable = FeatureTypeRegistry.GetDescriptor(FeatureTypes.District)?.TableName
            ?? throw new InvalidOperationException("FeatureTypeRegistry missing District descriptor");
    }

    /// <summary>
    /// Executes a raw SQL scalar query with strongly-named parameters.
    /// Parameters must be passed as (name, value) tuples where value is the
    /// boxed CLR type expected by Npgsql (e.g. Guid for UUID, string for text,
    /// double for float8). Table names in the SQL string are pre-validated
    /// against the FeatureTypeRegistry allowlist.
    /// </summary>
    private async Task<object?> ExecuteScalarAsync(string sql, List<(string name, object value)> parameters, CancellationToken ct)
    {
        var conn = _db.Database.GetDbConnection();
        await using var handle = await conn.EnsureOpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            SqlFragments.AddParam(cmd, name, value);
        }
        return await cmd.ExecuteScalarAsync(ct);
    }

    public async Task<bool> CheckRoadConnectivityAsync(Guid userId, string wkt, double maxDistanceMeters, CancellationToken ct = default)
    {
        var sql = $@"
            SELECT EXISTS (
                SELECT 1
                FROM {_roadTable} f
                WHERE f.user_id = @uid
                  AND ST_DWithin(
                        ({SqlFragments.LineStringFromData})::geography,
                        ST_SetSRID(ST_GeomFromText(@wkt), 4326)::geography,
                        @maxDist
                      )
            )";
        var result = await ExecuteScalarAsync(sql, [("@uid", (object)userId), ("@wkt", wkt), ("@maxDist", maxDistanceMeters)], ct);
        return result is bool b && b;
    }

    public async Task<bool> CheckDistrictCoverageAsync(Guid userId, double toleranceMeters, CancellationToken ct = default)
    {
        var sql = $@"
            WITH
            urban AS (
                SELECT ST_Union({SqlFragments.PolygonFromData}) AS geom
                FROM {_areaTable} f
                WHERE f.user_id = @uid
                  AND f.layer  IN ('central_urban', 'secondary_urban')
            ),
            districts AS (
                SELECT ST_Union({SqlFragments.PolygonFromData}) AS geom
                FROM {_districtTable} f
                WHERE f.user_id = @uid
            )
            SELECT ST_Covers(
                ST_Buffer(districts.geom::geography, @tolerance)::geometry,
                urban.geom
            )
            FROM urban, districts
            WHERE urban.geom IS NOT NULL AND districts.geom IS NOT NULL";
        var result = await ExecuteScalarAsync(sql, [("@uid", (object)userId), ("@tolerance", toleranceMeters)], ct);
        return result is bool b && b;
    }

    public async Task<bool> CheckDistrictOverlapAsync(Guid userId, string wkt, CancellationToken ct = default)
    {
        var sql = $@"
            SELECT EXISTS (
                SELECT 1 FROM {_districtTable} f
                WHERE f.user_id = @uid
                  AND ST_Intersects(
                        ({SqlFragments.PolygonFromData}),
                        ST_SetSRID(ST_GeomFromText(@wkt), 4326)
                      )
            )";
        var result = await ExecuteScalarAsync(sql, [("@uid", (object)userId), ("@wkt", wkt)], ct);
        return result is bool b && b;
    }

    public async Task<long> CountSiblingsInSameAreaAsync(Guid userId, string wkt, CancellationToken ct = default)
    {
        var sql = $@"
            WITH area_geom AS (
                SELECT {SqlFragments.PolygonFromDataWithAlias("a")} AS geom
                FROM {_areaTable} a
                WHERE a.user_id = @uid
                  AND a.layer IN ('central_urban', 'secondary_urban')
            )
            SELECT COUNT(*) FROM {_districtTable} d
            WHERE d.user_id = @uid
              AND EXISTS (
                  SELECT 1 FROM area_geom ag
                  WHERE ST_Intersects(ag.geom, ST_SetSRID(ST_GeomFromText(@wkt), 4326))
                    AND ST_Intersects(ag.geom, ({SqlFragments.PolygonFromDataWithAlias("d")}))
              )";
        var result = await ExecuteScalarAsync(sql, [("@uid", (object)userId), ("@wkt", wkt)], ct);
        return Convert.ToInt64(result);
    }

    public async Task<bool> CheckDistrictAdjacencyAsync(Guid userId, string wkt, CancellationToken ct = default)
    {
        var sql = $@"
            WITH district_geom AS (
                SELECT {SqlFragments.PolygonFromData} AS geom
                FROM {_districtTable} f
                WHERE f.user_id = @uid
            ),
            area_geom AS (
                SELECT {SqlFragments.PolygonFromDataWithAlias("a")} AS geom
                FROM {_areaTable} a
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
        var result = await ExecuteScalarAsync(sql, [("@uid", (object)userId), ("@wkt", wkt)], ct);
        return result is bool b && b;
    }

    public async Task<bool> UserHasCentralUrbanAreaAsync(Guid userId, CancellationToken ct = default) =>
        await _db.Set<Area>().AnyAsync(a => a.UserId == userId && a.Layer == FeatureTypes.AreaLayers.CentralUrban, ct);

    public Task<int> CountUserRoadsAsync(Guid userId, CancellationToken ct = default) =>
        _db.Set<Road>().CountAsync(r => r.UserId == userId, ct);

    public Task<int> CountUserDistrictsAsync(Guid userId, CancellationToken ct = default) =>
        _db.Set<District>().CountAsync(d => d.UserId == userId, ct);

    public Task<int> CountUserUrbanAreasAsync(Guid userId, CancellationToken ct = default) =>
        _db.Set<Area>().CountAsync(a => a.UserId == userId && (a.Layer == FeatureTypes.AreaLayers.CentralUrban || a.Layer == FeatureTypes.AreaLayers.SecondaryUrban), ct);
}
