using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.Models;

namespace NarsApi.Services;

public sealed class RoadQueryService(AppDbContext db) : IRoadQueryService
{
    // The road row (including its large JSONB payload) is only parsed for
    // coordinates downstream — never mutated — so skip change tracking.
    public Task<Road?> GetUserRoadByIdAsync(Guid roadId, Guid userId, CancellationToken ct = default) =>
        db.Roads.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roadId && r.UserId == userId, ct);
}
