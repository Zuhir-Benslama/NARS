using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

/// <summary>
/// Recomputes the scattered area geometry for a given user/commune pair and
/// persists the result to the features table.
///
/// The scattered area is defined as the commune boundary minus the union of all
/// urban area polygons drawn by the user. It is recomputed asynchronously
/// (fire-and-forget) after any urban area is saved or deleted.
///
/// Uses <see cref="IDbContextFactory{TContext}"/> so each call owns an
/// independent <see cref="AppDbContext"/> — it never borrows the request-scoped
/// context, which may be disposed before the task completes.
/// </summary>
public interface IScatteredAreaService
{
    /// <summary>
    /// Triggers an async recompute. The returned <see cref="Task"/> is intended
    /// for fire-and-forget: callers should use <c>_ = service.RefreshAsync(…)</c>
    /// and not await it on the request path.
    /// </summary>
    Task RefreshAsync(int userId, int communeId);
}

public sealed class ScatteredAreaService(IDbContextFactory<AppDbContext> dbFactory)
    : IScatteredAreaService
{
    public async Task RefreshAsync(int userId, int communeId)
    {
        try
        {
            await using var db   = await dbFactory.CreateDbContextAsync();
            var conn             = db.Database.GetDbConnection();
            await conn.OpenAsync();

            string? scatteredGeoJson = null;
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $@"
                    WITH
                    boundary AS (
                        SELECT geometry AS geom
                        FROM communes_boundaries
                        WHERE commune_id = @cid
                    ),
                    urban AS (
                        SELECT ST_Union({SqlFragments.PolygonFromData}) AS geom
                        FROM areas f
                        WHERE f.user_id = @uid
                          AND f.layer  IN ('central_urban', 'secondary_urban')
                    )
                    SELECT ST_AsGeoJSON(
                        ST_Difference(
                            boundary.geom,
                            COALESCE(urban.geom, ST_GeomFromText('GEOMETRYCOLLECTION EMPTY', 4326))
                        ),
                        6
                    )
                    FROM boundary LEFT JOIN urban ON true";

                AddParam(cmd, "@cid", communeId);
                AddParam(cmd, "@uid", userId);

                // CommandBehavior.SequentialAccess streams the large GeoJSON column
                // instead of buffering it in Npgsql's internal buffer.
                using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess);
                if (await reader.ReadAsync() && !await reader.IsDBNullAsync(0))
                    scatteredGeoJson = await reader.GetTextReader(0).ReadToEndAsync();
            }
            finally { await conn.CloseAsync(); }

            if (scatteredGeoJson is null) return;

            await db.Areas
                .Where(f => f.UserId == userId &&
                            f.Layer  == FeatureTypes.AreaLayers.Scattered)
                .ExecuteDeleteAsync();

            db.Areas.Add(new Area
            {
                UserId = userId,
                Layer  = FeatureTypes.AreaLayers.Scattered,
                Label  = "Scattered Area",
                Data   = JsonSerializer.Serialize(new
                {
                    type     = "areas",
                    label    = "Scattered Area",
                    layer    = "scattered",
                    geometry = scatteredGeoJson,
                }),
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ScatteredAreaService] Refresh error: {ex.Message}");
        }
    }

    private static void AddParam(IDbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value         = value;
        cmd.Parameters.Add(p);
    }
}
