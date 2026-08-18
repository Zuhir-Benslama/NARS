using Moq;
using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests;

public class UserAuthorizationServiceTests
{
    private static UserAuthorizationService CreateService(AppDbContext db) =>
        new(db, Mock.Of<IRefreshTokenService>(), Mock.Of<IFeatureCleanupService>(), Mock.Of<IDateTimeProvider>());

    [Fact]
    public async Task FindUserByIdAsync_ExistingUser_ReturnsUser()
    {
        using var db = CreateInMemoryDb("UserAuthFindById");
        db.Users.Add(new User
        {
            Id = UserId,
            Username = "testuser",
            Name = "Test User",
            Email = AltEmail,
            Phone = DefaultPhone,
            PasswordHash = "hash",
            SecurityStamp = User.GenerateSecurityStamp(),
            Role = UserRoles.CommuneUser,
            CommuneId = 1,
        });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        var found = await svc.FindUserByIdAsync(UserId);

        Assert.NotNull(found);
        Assert.Equal(UserId, found.Id);
        Assert.Equal("testuser", found.Username);
    }

    [Fact]
    public async Task FindUserByIdAsync_NonexistentUser_ReturnsNull()
    {
        using var db = CreateInMemoryDb("UserAuthFindByIdMissing");
        var svc = CreateService(db);

        var found = await svc.FindUserByIdAsync(Guid.NewGuid());

        Assert.Null(found);
    }

    [Fact]
    public async Task FindUserByUsernameAsync_ExistingUser_ReturnsUser()
    {
        using var db = CreateInMemoryDb("UserAuthFindByUsername");
        db.Users.Add(new User
        {
            Id = UserId,
            Username = "testuser",
            Name = "Test User",
            Email = AltEmail,
            Phone = DefaultPhone,
            PasswordHash = "hash",
            SecurityStamp = User.GenerateSecurityStamp(),
            Role = UserRoles.CommuneUser,
            CommuneId = 1,
        });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        var found = await svc.FindUserByUsernameAsync("testuser");

        Assert.NotNull(found);
        Assert.Equal(UserId, found.Id);
    }

    [Fact]
    public async Task FindUserByUsernameAsync_NonexistentUser_ReturnsNull()
    {
        using var db = CreateInMemoryDb("UserAuthFindByUsernameMissing");
        var svc = CreateService(db);

        var found = await svc.FindUserByUsernameAsync("no-such-user");

        Assert.Null(found);
    }
}
