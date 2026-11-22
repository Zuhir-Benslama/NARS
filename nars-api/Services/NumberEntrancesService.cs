using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

/// <summary>
/// Raised when a side's parity series is exhausted (no collision-free number is
/// available up to <see cref="GeometryHelper"/>'s cap). The whole batch must be
/// rolled back so partial numbering cannot leave gaps/duplicates behind.
/// </summary>
public sealed class NumberSeriesExhaustedException : InvalidOperationException
{
    public NumberSeriesExhaustedException(string side)
        : base($"The entrance-number series for side '{side}' is exhausted; no collision-free number is available.")
    {
    }
}

/// <summary>
/// Atomically assigns a dense, collision-free sequence of entrance numbers to an
/// ordered list of house entrances on one road.
///
/// The whole batch runs inside a single database transaction that first takes a
/// row lock on the road (<c>SELECT ... FOR UPDATE</c>). That lock is the
/// serialization anchor: two concurrent numbering batches on the same road block
/// on it, so each sees the other's committed numbers and the two batches produce
/// disjoint sequences. Without this, two clients numbering the same road would
/// both start from the same "next free number" and write duplicates (the stored
/// data column carries no unique constraint on the number).
/// </summary>
public sealed class NumberEntrancesService(IDbContextFactory<AppDbContext> dbFactory) : INumberEntrancesService
{
    private static readonly string HeTable = FeatureTypeRegistry.ValidateTableName(
        FeatureTypeRegistry.GetDescriptor(FeatureTypes.HouseEntrance)?.TableName ?? "house_entrances");

    public async Task<IReadOnlyList<NumberedEntrance>?> NumberAsync(
        Guid userId, Guid roadId, IReadOnlyList<Guid> entranceIds, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var conn = db.Database.GetDbConnection();
        await using var handle = await conn.EnsureOpenAsync(ct);
        // Success path commits explicitly; on failure the awaited transaction
        // disposal rolls back, so nothing is partially written.
        await using var tx = await conn.BeginTransactionAsync(ct);

        // entrance id -> (side, stored data) for the entrances being numbered.
        var targets = new Dictionary<Guid, (string Side, string Data)>();
        var usedNumbers = new Dictionary<string, HashSet<int>>
        {
            ["left"] = [],
            ["right"] = [],
        };

        // The road's current geometry. Left/right is relative to the order of its
        // coordinates, which "compute road directions" can reverse after an entrance
        // was created, so the side is recomputed here instead of trusting the value
        // stored on the entrance.
        List<(double Lat, double Lng)>? roadCoords = null;

        // Lock the road as the serialization anchor and verify ownership. Any
        // concurrent NumberAsync for the same road blocks here until we commit,
        // which is what makes the read-compute-write below atomic.
        await using (var lockCmd = conn.CreateCommand())
        {
            lockCmd.Transaction = tx;
            lockCmd.CommandText = "SELECT data::text FROM roads WHERE id = @rid AND user_id = @uid FOR UPDATE";
            SqlFragments.AddParam(lockCmd, "@rid", roadId);
            SqlFragments.AddParam(lockCmd, "@uid", userId);
            if (await lockCmd.ExecuteScalarAsync(ct) is not { } roadRow)
            {
                return null; // road missing or not owned
            }

            roadCoords = TryParseRoadCoords(roadRow as string);
        }

        // Load all of this user's main-layer entrances on the road under the
        // same lock, so a concurrent per-entrance write cannot slip a number in
        // mid-batch. Entrances being numbered feed the side lookup; the other
        // (already-numbered) rows seed the used-number sets.
        await using (var rowsCmd = conn.CreateCommand())
        {
            rowsCmd.Transaction = tx;
#pragma warning disable S2077 // Table name is allowlist-validated; values are parameterized
            rowsCmd.CommandText =
                $"SELECT id, data FROM {HeTable} " +
                "WHERE user_id = @uid AND road_id = @rid AND layer = @layer FOR UPDATE";
#pragma warning restore S2077
            SqlFragments.AddParam(rowsCmd, "@uid", userId);
            SqlFragments.AddParam(rowsCmd, "@rid", roadId);
            SqlFragments.AddParam(rowsCmd, "@layer", FeatureTypes.HouseEntranceLayers.Main);

            await using var reader = await rowsCmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var id = reader.GetGuid(0);
                var data = reader.GetString(1);
                if (entranceIds.Contains(id))
                {
                    if (ResolveSide(data, roadCoords) is { } side)
                    {
                        targets[id] = (side, HouseEntranceData.SetSide(data, side));
                    }
                }
                else
                {
                    AddUsedNumbers(data, usedNumbers);
                }
            }
        }

        // Reject duplicate IDs: the loop below iterates entranceIds in order
        // and would number the same entrance twice, overwriting the first
        // assignment. Every requested entrance must also exist, belong to the
        // user, be on the road, and carry a valid side.
        if (entranceIds.Count != entranceIds.Distinct().Count()
            || targets.Count != entranceIds.Distinct().Count())
        {
            return null;
        }

        var results = new List<NumberedEntrance>(entranceIds.Count);
        foreach (var id in entranceIds)
        {
            if (!targets.TryGetValue(id, out var target))
            {
                return null;
            }

            var (side, data) = target;
            var suggested = GeometryHelper.SuggestEntranceNumber(side, usedNumbers[side]);
            if (suggested < 0)
            {
                throw new NumberSeriesExhaustedException(side);
            }

            usedNumbers[side].Add(suggested);
            var label = suggested.ToString(CultureInfo.InvariantCulture);
            var newData = HouseEntranceData.SetEntranceNumber(data, suggested, label);

            await UpdateEntranceAsync(conn, tx, id, userId, roadId, newData, ct);
            results.Add(new NumberedEntrance(id.ToString(), side, suggested, label));
        }

        await tx.CommitAsync(ct);
        return results;
    }

    private static async Task UpdateEntranceAsync(
        DbConnection conn, DbTransaction tx, Guid id, Guid userId, Guid roadId, string data, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
#pragma warning disable S2077 // Table name is allowlist-validated; values are parameterized
        cmd.CommandText = $"UPDATE {HeTable} SET data = @data::jsonb WHERE id = @id AND user_id = @uid AND road_id = @rid";
#pragma warning restore S2077
        SqlFragments.AddParam(cmd, "@data", data);
        SqlFragments.AddParam(cmd, "@id", id);
        SqlFragments.AddParam(cmd, "@uid", userId);
        SqlFragments.AddParam(cmd, "@rid", roadId);
        var affected = await cmd.ExecuteNonQueryAsync(ct);
        if (affected == 0)
        {
            throw new InvalidOperationException($"Entrance {id} could not be updated.");
        }
    }

    /// <summary>
    /// Side from the road's current geometry and the entrance's own position; falls
    /// back to the stored side when either is unavailable.
    /// </summary>
    private static string? ResolveSide(string data, List<(double Lat, double Lng)>? roadCoords)
    {
        if (roadCoords is not null && HouseEntranceData.TryGetLatLng(data, out var lat, out var lng))
        {
            return GeometryHelper.DetermineSideOnPolyline(lat, lng, roadCoords);
        }

        return HouseEntranceData.TryGetSide(data);
    }

    private static List<(double Lat, double Lng)>? TryParseRoadCoords(string? roadData)
    {
        if (roadData is null)
        {
            return null;
        }

        try
        {
            var coordsNode = JsonNode.Parse(roadData)?["coordinates"];
            return coordsNode is null ? null : GeometryHelper.ParseRoadCoordinates(coordsNode);
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidOperationException)
        {
            return null; // unusable geometry: fall back to stored sides
        }
    }

    private static void AddUsedNumbers(string data, Dictionary<string, HashSet<int>> usedNumbers)
    {
        // Keyed by the number's parity (odd = left series, even = right series), not
        // the stored side: a side made stale by a road reversal must not hide a used
        // number from the series it actually occupies.
        if (HouseEntranceData.TryGetEntranceNumber(data, out var num))
        {
            usedNumbers[num % 2 != 0 ? "left" : "right"].Add(num);
        }
    }
}
