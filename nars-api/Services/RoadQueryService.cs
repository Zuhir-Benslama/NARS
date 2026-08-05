using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.Models;

namespace NarsApi.Services;

public sealed class RoadQueryService(AppDbContext db) : IRoadQueryService
{
    public Task<Road?> GetUserRoadByIdAsync(Guid roadId, Guid userId, CancellationToken ct = default) =>
        db.Roads.FirstOrDefaultAsync(r => r.Id == roadId && r.UserId == userId, ct);
}
