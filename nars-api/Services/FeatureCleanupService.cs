using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NarsApi.Data;
using NarsApi.Infrastructure;

namespace NarsApi.Services;

/// <summary>
/// Handles bulk deletion of features across all registered feature tables.
/// </summary>
public sealed class FeatureCleanupService : IFeatureCleanupService
{
    public async Task<int> DeleteAllFeaturesForUserAsync(AppDbContext db, Guid userId, CancellationToken ct)
    {
        var descriptors = FeatureTypeRegistry.GetAllDescriptors();
        if (descriptors.Count == 0)
        {
            return 0;
        }

        // Single compound CTE executed via ADO.NET (EF Core's SqlQueryRaw wraps
        // the query in a subquery which breaks PostgreSQL's requirement that
        // data-modifying CTEs appear at the top level of the WITH clause).
        //
        // 1. Collects all matching feature IDs across every feature table
        // 2. Deletes matching feature_registry rows
        // 3. Deletes matching rows from each feature table
        // 4. Returns the total number of feature rows deleted
        // Table names come from the validated allowlist in FeatureTypeRegistry.
        var sb = new StringBuilder("WITH feature_ids AS (");
        for (var i = 0; i < descriptors.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(" UNION ALL ");
            }

            var table = FeatureTypeRegistry.ValidateTableName(descriptors[i].TableName);
            sb.Append($"SELECT id FROM {table} WHERE user_id = @uid");
        }
        sb.Append("), deleted_registry AS (DELETE FROM feature_registry WHERE id IN (SELECT id FROM feature_ids))");

        var countParts = new List<string>(descriptors.Count);
        for (var i = 0; i < descriptors.Count; i++)
        {
            var table = FeatureTypeRegistry.ValidateTableName(descriptors[i].TableName);
            sb.Append($", del_{i} AS (DELETE FROM {table} WHERE user_id = @uid RETURNING 1)");
            countParts.Add($"(SELECT count(*)::int FROM del_{i})");
        }

        sb.Append(" SELECT ").Append(string.Join(" + ", countParts)).Append(" AS Total");

        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var connHandle = await conn.EnsureOpenAsync(ct);

#pragma warning disable S2077 // Table names are validated allowlist constants from FeatureTypeRegistry
        await using var cmd = new NpgsqlCommand(sb.ToString(), conn);
        cmd.Parameters.Add(new NpgsqlParameter("@uid", userId));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
#pragma warning restore S2077

        return await reader.ReadAsync(ct) ? reader.GetInt32(0) : 0;
    }
}
