using System.Collections.Frozen;
using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

/// <summary>
/// Numbers the main entrances around a cité, roundabout or square inside one
/// locked transaction. The feature row is the serialization anchor (the same
/// role the road plays in <see cref="NumberEntrancesService"/>), so concurrent
/// numbering of one feature cannot produce interleaved or duplicated numbers.
///
/// Cité: sequential numbers counter-clockwise from the user-picked gate;
/// secondary entrances linked to a main entrance get letter suffixes (1/A…).
/// Roundabout / square: counter-clockwise from north, even outside, odd inside,
/// each series numbered independently.
/// </summary>
public sealed class SpecialNumberingService(IDbContextFactory<AppDbContext> dbFactory) : ISpecialNumberingService
{
    private static readonly string EntrancesTable = FeatureTypeRegistry.ValidateTableName(
        FeatureTypeRegistry.GetDescriptor(FeatureTypes.HouseEntrance)?.TableName ?? "house_entrances");

    private static readonly FrozenDictionary<string, (string Type, string Layer)> FeatureByMode =
        new Dictionary<string, (string, string)>
        {
            [FeatureTypes.SpecialNumberingModes.Cite] = (FeatureTypes.District, FeatureTypes.DistrictLayers.Cite),
            [FeatureTypes.SpecialNumberingModes.Roundabout] = (FeatureTypes.Roundabouts, FeatureTypes.RoundaboutsLayers.Default),
            [FeatureTypes.SpecialNumberingModes.Square] = (FeatureTypes.PublicSpace, FeatureTypes.PublicSpaceLayers.Square),
        }.ToFrozenDictionary();

    private sealed record FeatureGeometry(
        List<(double Lat, double Lng)> Ring,
        (double Lat, double Lng) Center,
        double RadiusM,
        double OutsideBandM);

    private sealed record Target(
        Guid Id,
        string Data,
        string Layer,
        double Lat,
        double Lng,
        string Zone,
        Guid MainEntranceDbId);

    public async Task<IReadOnlyList<NumberedEntrance>?> NumberAsync(
        Guid userId,
        string mode,
        Guid featureId,
        IReadOnlyList<Guid> entranceIds,
        (double Lat, double Lng)? startPoint,
        CancellationToken ct)
    {
        if (!FeatureByMode.TryGetValue(mode, out var feature)
            || (mode == FeatureTypes.SpecialNumberingModes.Cite && startPoint is null))
        {
            return null;
        }

        var descriptor = FeatureTypeRegistry.GetDescriptor(feature.Type);
        if (descriptor is null)
        {
            return null;
        }

        var featureTable = FeatureTypeRegistry.ValidateTableName(descriptor.TableName);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var conn = db.Database.GetDbConnection();
        await using var handle = await conn.EnsureOpenAsync(ct);
        // Explicit commit on success; disposal rolls back anything written before
        // a validation failure or a database error.
        await using var tx = await conn.BeginTransactionAsync(ct);

        string? featureData;
        await using (var lockCmd = conn.CreateCommand())
        {
            lockCmd.Transaction = tx;
#pragma warning disable S2077 // Table name is allowlist-validated; values are parameterized
            lockCmd.CommandText = $"SELECT data FROM {featureTable} WHERE id = @fid AND user_id = @uid AND layer = @layer FOR UPDATE";
#pragma warning restore S2077
            SqlFragments.AddParam(lockCmd, "@fid", featureId);
            SqlFragments.AddParam(lockCmd, "@uid", userId);
            SqlFragments.AddParam(lockCmd, "@layer", feature.Layer);
            await using var reader = await lockCmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                return null;
            }

            featureData = reader.IsDBNull(0) ? null : reader.GetString(0);
        }

        var geometry = ParseFeatureGeometry(mode, featureData);
        if (geometry is null)
        {
            return null;
        }

        // Lock each requested entrance and read its position under the same
        // transaction. Selecting by layer here also rejects secondary entrances
        // for the zone-based modes, which only number main entrances.
        var layers = mode == FeatureTypes.SpecialNumberingModes.Cite
            ? new[] { FeatureTypes.HouseEntranceLayers.Main, FeatureTypes.HouseEntranceLayers.Secondary }
            : new[] { FeatureTypes.HouseEntranceLayers.Main };
        var targets = new Dictionary<Guid, Target>();
        foreach (var id in entranceIds)
        {
            var target = await LoadTargetAsync(conn, tx, id, userId, layers, ct);
            if (target is null
                || !ClassifyZone(mode, target.Lat, target.Lng, geometry, out var zone)
                || zone is null)
            {
                return null;
            }

            targets[id] = target with { Zone = zone };
        }

        if (targets.Count != entranceIds.Distinct().Count())
        {
            return null;
        }

        var results = new List<NumberedEntrance>(targets.Count);
        if (mode == FeatureTypes.SpecialNumberingModes.Cite)
        {
            if (!await NumberCiteAsync(conn, tx, userId, featureId, startPoint!.Value,
                    geometry.Center, targets, results, ct))
            {
                return null;
            }
        }
        else
        {
            await NumberZonesAsync(conn, tx, userId, featureId, mode, geometry, targets, results, ct);
        }

        await tx.CommitAsync(ct);
        return results;
    }

    private static FeatureGeometry? ParseFeatureGeometry(string mode, string? data)
    {
        if (data is null)
        {
            return null;
        }

        try
        {
            var node = JsonNode.Parse(data);
            var band = node?["numberingOutsideBandM"] is JsonValue bandNode
                       && bandNode.TryGetValue<double>(out var parsedBand)
                       && double.IsFinite(parsedBand) && parsedBand > 0
                ? parsedBand
                : SpecialNumberingRequest.DefaultOutsideBandM;

            if (mode == FeatureTypes.SpecialNumberingModes.Roundabout)
            {
                // The plain city center shares this layer but is not numbered
                // in the even-outside/odd-inside series. Older rows used
                // `cityCenterTypeKey`, so accept both spellings.
                var subtype = ReadRoundaboutsSubtype(node);
                if (subtype != FeatureTypes.RoundaboutsSubtypes.Roundabout)
                {
                    return null;
                }
                if (TryReadDouble(node, "lat", out var lat) && TryReadDouble(node, "lng", out var lng)
                    && TryReadDouble(node, "radius", out var radius)
                    && double.IsFinite(lat) && double.IsFinite(lng)
                    && (lat != 0.0 || lng != 0.0)
                    && double.IsFinite(radius) && radius > 0)
                {
                    return new FeatureGeometry([], (lat, lng), radius, band);
                }

                return null;
            }

            if (node?["coordinates"] is not JsonArray coords || coords.Count < 3)
            {
                return null;
            }

            var ring = new List<(double Lat, double Lng)>(coords.Count);
            foreach (var c in coords)
            {
                if (c is JsonObject point && TryReadDouble(point, "lat", out var lat)
                    && TryReadDouble(point, "lng", out var lng)
                    && double.IsFinite(lat) && double.IsFinite(lng))
                {
                    ring.Add((lat, lng));
                }
            }

            var center = GeometryHelper.ComputeCentroid(ring);
            return ring.Count < 3 || center is null
                ? null
                : new FeatureGeometry(ring, center.Value, 0, band);
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadRoundaboutsSubtype(JsonNode? node)
    {
        var current = node?["roundaboutsTypeKey"]?.GetValue<string>();
        return !string.IsNullOrEmpty(current)
            ? current
            : node?["cityCenterTypeKey"]?.GetValue<string>();
    }

    private static bool TryReadDouble(JsonNode? node, string key, out double value)
    {
        value = 0;
        return node is JsonObject obj && obj[key] is JsonValue child && child.TryGetValue<double>(out value);
    }

    private async static Task<Target?> LoadTargetAsync(
        DbConnection conn, DbTransaction tx, Guid id, Guid userId,
        IReadOnlyList<string> layers, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        // Layers come from our own catalog constants; bind each one separately
        // so the provider never has to infer an array parameter type.
        var layerParams = string.Join(", ", layers.Select((_, i) => $"@l{i}"));
#pragma warning disable S2077 // Table name is allowlist-validated; values are parameterized
        cmd.CommandText =
            $"SELECT data, layer FROM {EntrancesTable} WHERE id = @id AND user_id = @uid AND layer IN ({layerParams}) FOR UPDATE";
#pragma warning restore S2077
        SqlFragments.AddParam(cmd, "@id", id);
        SqlFragments.AddParam(cmd, "@uid", userId);
        for (var i = 0; i < layers.Count; i++)
        {
            SqlFragments.AddParam(cmd, $"@l{i}", layers[i]);
        }

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || reader.IsDBNull(0))
        {
            return null;
        }

        var data = reader.GetString(0);
        var layer = reader.GetString(1);
        if (!HouseEntranceData.TryGetLatLng(data, out var lat, out var lng))
        {
            return null;
        }

        HouseEntranceData.TryGetMainEntranceDbId(data, out var mainId);
        return new Target(id, data, layer, lat, lng, string.Empty, mainId);
    }

    private static bool ClassifyZone(
        string mode, double lat, double lng, FeatureGeometry geometry, out string? zone)
    {
        zone = null;
        switch (mode)
        {
            case FeatureTypes.SpecialNumberingModes.Cite:
                if (!GeometryHelper.IsPointInPolygon(lat, lng, geometry.Ring))
                {
                    return false;
                }

                zone = "inside";
                return true;

            case FeatureTypes.SpecialNumberingModes.Square:
                if (GeometryHelper.IsPointInPolygon(lat, lng, geometry.Ring))
                {
                    zone = "inside";
                    return true;
                }

                if (GeometryHelper.DistanceToPolygonBoundaryM(lat, lng, geometry.Ring) <= geometry.OutsideBandM)
                {
                    zone = "outside";
                    return true;
                }

                return false;

            case FeatureTypes.SpecialNumberingModes.Roundabout:
                var distanceM = DraftGeometry.HaversineM(
                    lng, lat, geometry.Center.Lng, geometry.Center.Lat);
                if (distanceM <= geometry.RadiusM)
                {
                    zone = "inside";
                    return true;
                }

                if (distanceM <= geometry.RadiusM + geometry.OutsideBandM)
                {
                    zone = "outside";
                    return true;
                }

                return false;

            default:
                return false;
        }
    }

    private static async Task<bool> NumberCiteAsync(
        DbConnection conn, DbTransaction tx, Guid userId, Guid featureId,
        (double Lat, double Lng) startPoint, (double Lat, double Lng) center,
        IReadOnlyDictionary<Guid, Target> targets, List<NumberedEntrance> results, CancellationToken ct)
    {
        var angles = targets.ToDictionary(
            t => t.Key,
            t => GeometryHelper.AngleFromNorthCounterClockwise(
                t.Value.Lat, t.Value.Lng, center.Lat, center.Lng, startPoint));
        var ordered = targets.Values.OrderBy(t => angles[t.Id]).ThenBy(t => t.Id).ToList();
        var mains = ordered
            .Where(t => t.Layer == FeatureTypes.HouseEntranceLayers.Main)
            .ToList();
        var secondaries = ordered.Except(mains).ToList();

        var mainIds = mains.Select(t => t.Id).ToHashSet();
        if (secondaries.Any(t => !HouseEntranceData.TryGetMainEntranceDbId(t.Data, out var mainId)
                                 || !mainIds.Contains(mainId)))
        {
            // A secondary entrance whose building was not selected cannot be
            // numbered; fail the whole batch instead of writing partial data.
            return false;
        }

        var numbers = new Dictionary<Guid, int>();
        for (var i = 0; i < mains.Count; i++)
        {
            var number = i + 1;
            if (number > 100_000)
            {
                throw new NumberSeriesExhaustedException("cité");
            }

            numbers[mains[i].Id] = number;
            await PersistAsync(conn, tx, userId, mains[i], number, null, "inside", featureId,
                "cite", startPoint, results, ct);
        }

        var siblings = secondaries.GroupBy(t => HouseEntranceData.TryGetMainEntranceDbId(t.Data, out var mainId)
            ? mainId : Guid.Empty);
        foreach (var group in siblings)
        {
            var index = 0;
            foreach (var secondary in group.OrderBy(t => angles[t.Id]).ThenBy(t => t.Id))
            {
                var suffix = LetterSuffix(index++);
                var baseNumber = numbers[group.Key];
                await PersistAsync(conn, tx, userId, secondary, baseNumber, suffix, "inside", featureId,
                    "cite", startPoint, results, ct);
            }
        }

        return true;
    }

    private static async Task NumberZonesAsync(
        DbConnection conn, DbTransaction tx, Guid userId, Guid featureId, string mode,
        FeatureGeometry geometry, IReadOnlyDictionary<Guid, Target> targets,
        List<NumberedEntrance> results, CancellationToken ct)
    {
        var angles = targets.ToDictionary(
            t => t.Key,
            t => GeometryHelper.AngleFromNorthCounterClockwise(t.Value.Lat, t.Value.Lng, geometry.Center.Lat, geometry.Center.Lng));

        var used = new Dictionary<string, HashSet<int>>
        {
            ["inside"] = [],
            ["outside"] = [],
        };

        foreach (var zone in new[] { "inside", "outside" })
        {
            var ordered = targets.Values
                .Where(t => t.Zone == zone)
                .OrderBy(t => angles[t.Id])
                .ThenBy(t => t.Id);

            foreach (var target in ordered)
            {
                var number = GeometryHelper.SuggestEntranceNumber(
                    zone == "inside" ? "left" : "right", used[zone]);
                if (number < 0)
                {
                    throw new NumberSeriesExhaustedException(zone);
                }

                used[zone].Add(number);
                await PersistAsync(conn, tx, userId, target, number, null, zone, featureId,
                    mode, null, results, ct);
            }
        }
    }

    private static async Task PersistAsync(
        DbConnection conn, DbTransaction tx, Guid userId, Target target,
        int number, string? suffix, string zone, Guid featureId, string mode,
        (double Lat, double Lng)? startPoint, List<NumberedEntrance> results, CancellationToken ct)
    {
        var label = suffix is null
            ? number.ToString(CultureInfo.InvariantCulture)
            : $"{number.ToString(CultureInfo.InvariantCulture)}/{suffix}";

        var data = HouseEntranceData.SetSpecialNumberingMetadata(
            HouseEntranceData.SetNumberSuffix(
                HouseEntranceData.SetEntranceNumber(target.Data, number, label), suffix),
            mode, featureId, startPoint);

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
#pragma warning disable S2077 // Table name is allowlist-validated; values are parameterized
        cmd.CommandText = $"UPDATE {EntrancesTable} SET data = @data::jsonb WHERE id = @id AND user_id = @uid";
#pragma warning restore S2077
        SqlFragments.AddParam(cmd, "@data", data);
        SqlFragments.AddParam(cmd, "@id", target.Id);
        SqlFragments.AddParam(cmd, "@uid", userId);
        if (await cmd.ExecuteNonQueryAsync(ct) == 0)
        {
            throw new InvalidOperationException($"Entrance {target.Id} could not be updated.");
        }

        results.Add(new NumberedEntrance(target.Id.ToString(), zone, number, label, suffix));
    }

    private static string LetterSuffix(int index)
    {
        var value = index + 1;
        var sb = new StringBuilder();
        while (value > 0)
        {
            value--;
            sb.Insert(0, (char)('A' + value % 26));
            value /= 26;
        }

        return sb.ToString();
    }
}
