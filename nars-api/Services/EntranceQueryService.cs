using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

public class EntranceQueryService(AppDbContext db) : IEntranceQueryService
{
    public async Task<HashSet<int>> GetUsedEntranceNumbersAsync(Guid userId, Guid roadId, CancellationToken ct = default)
    {
        var usedNumbers = new HashSet<int>();
        var conn = db.Database.GetDbConnection();
        await using var handle = await conn.EnsureOpenAsync(ct);

        var heTable = FeatureTypeRegistry.GetDescriptor(FeatureTypes.HouseEntrance)?.TableName ?? "house_entrances";
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT (data::jsonb->>'entranceNumber')::int
            FROM {heTable}
            WHERE user_id = @uid
              AND layer   = @layer
              AND road_id = @rid
              AND data::jsonb->>'entranceNumber' IS NOT NULL";
        SqlFragments.AddParam(cmd, "@uid", userId);
        SqlFragments.AddParam(cmd, "@rid", roadId);
        SqlFragments.AddParam(cmd, "@layer", FeatureTypes.HouseEntranceLayers.Main);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (!await reader.IsDBNullAsync(0, ct))
            {
                usedNumbers.Add(reader.GetInt32(0));
            }
        }

        return usedNumbers;
    }
}
