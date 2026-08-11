namespace NarsApi.Services;

/// <summary>
/// Service for querying house entrance data directly via ADO.NET.
/// Raw ADO.NET is needed for JSONB field extraction that EF Core's
/// Npgsql mapper does not handle efficiently.
/// </summary>
public interface IEntranceQueryService
{
    /// <summary>
    /// Returns the entrance numbers already used on a given road, restricted to
    /// the requested side's parity (odd for left, even for right). Filtering by
    /// parity in SQL avoids materializing the other half of the road's numbers,
    /// which <see cref="GeometryHelper.SuggestEntranceNumber"/> never consults.
    /// </summary>
    Task<HashSet<int>> GetUsedEntranceNumbersAsync(Guid userId, Guid roadId, string side, CancellationToken ct = default);
}
