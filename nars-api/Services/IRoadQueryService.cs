using NarsApi.Models;

namespace NarsApi.Services;

public interface IRoadQueryService
{
    /// <summary>Returns a road feature by ID, scoped to the given user.</summary>
    Task<Road?> GetUserRoadByIdAsync(Guid roadId, Guid userId, CancellationToken ct = default);
}
