using NarsApi.Models;

namespace NarsApi.Services;

public interface IRoadQueryService
{
    Task<Road?> GetUserRoadByIdAsync(Guid roadId, Guid userId, CancellationToken ct = default);
}
