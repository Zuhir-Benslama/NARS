using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests;

public class EntranceServiceTests
{
    private static async Task<Guid> SeedRoadOwnerAsync(AppDbContext db, Guid userId)
    {
        db.Users.Add(new User
        {
            Id = userId,
            Username = "roadowner",
            Name = "Road Owner",
            Email = AltEmail,
            Phone = DefaultPhone,
            PasswordHash = DummyPasswordHash,
            Role = UserRoles.CommuneUser,
            CommuneId = CommuneId1,
        });
        return await AddRoadAsync(db, userId, """{"type":"LineString","coordinates":[[36.0,3.0],[36.1,3.1]]}""");
    }

    // ── GetRoadOwnerAsync ───────────────────────────────────────────────

    [Fact]
    public async Task GetRoadOwnerAsync_ExistingRoad_ReturnsOwnerAndCommune()
    {
        var (db, factory) = CreateInMemoryDbPair("EntranceServiceGetOwner");
        await using (db)
        {
            var roadId = await SeedRoadOwnerAsync(db, UserId);
            var svc = new EntranceService(factory);

            var result = await svc.GetRoadOwnerAsync(roadId);

            Assert.NotNull(result);
            Assert.Equal(UserId, result!.Value.OwnerUserId);
            Assert.Equal(CommuneId1, result.Value.CommuneId);
        }
    }

    [Fact]
    public async Task GetRoadOwnerAsync_NonexistentRoad_ReturnsNull()
    {
        var (db, factory) = CreateInMemoryDbPair("EntranceServiceGetOwnerMissing");
        await using (db)
        {
            var svc = new EntranceService(factory);

            var result = await svc.GetRoadOwnerAsync(Guid.NewGuid());

            Assert.Null(result);
        }
    }
}
