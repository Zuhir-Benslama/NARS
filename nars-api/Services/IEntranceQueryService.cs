namespace NarsApi.Services;

/// <summary>
/// Service for querying house entrance data directly via ADO.NET.
/// Raw ADO.NET is needed for JSONB field extraction that EF Core's
/// Npgsql mapper does not handle efficiently.
/// </summary>
public interface IEntranceQueryService
{
    Task<HashSet<int>> GetUsedEntranceNumbersAsync(Guid userId, Guid roadId, CancellationToken ct = default);
}
