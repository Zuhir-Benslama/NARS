using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.Infrastructure;

namespace NarsApi.Services;

public sealed class BoundaryService(AppDbContext db) : IBoundaryService
{
    public async Task<string?> GetBoundaryGeoJsonAsync(int communeId, CancellationToken ct = default)
    {
        var conn = db.Database.GetDbConnection();
        await using var handle = await conn.EnsureOpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ST_AsGeoJSON(geometry) FROM communes_boundaries WHERE commune_id = @id";
        SqlFragments.AddParam(cmd, "@id", communeId);

        var scalar = await cmd.ExecuteScalarAsync(ct);
        return scalar as string;
    }
}
