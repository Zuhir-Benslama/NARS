using Moq;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests;

public class UserAuthorizationServiceTests
{
    private static UserAuthorizationService CreateService(AppDbContext db) =>
        new(db, Mock.Of<IRefreshTokenService>(), Mock.Of<IFeatureCleanupService>(), Mock.Of<IDateTimeProvider>(), Mock.Of<ISecurityStampCache>());

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

    // ── UpdateManagedUserAsync session invalidation ─────────────────────

    [Fact]
    public async Task UpdateManagedUserAsync_ScopeChange_RotatesStampAndRevokesTokens()
    {
        using var db = CreateInMemoryDb("UserAuthScopeChangeInvalidates");
        db.Communes.AddRange(
            new Commune { CommuneId = 5, DairaId = 1, CommuneFr = "Commune A" },
            new Commune { CommuneId = 6, DairaId = 1, CommuneFr = "Commune B" });
        var callerId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = callerId,
            Username = "caller",
            Email = "caller@test.com",
            Name = "Caller",
            Phone = DefaultPhone,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(DefaultPassword),
            SecurityStamp = User.GenerateSecurityStamp(),
            Role = UserRoles.DairaAdmin,
            DairaId = 1,
        });
        var targetId = Guid.NewGuid();
        var originalStamp = User.GenerateSecurityStamp();
        db.Users.Add(new User
        {
            Id = targetId,
            Username = "target",
            Email = "target@test.com",
            Name = "Target",
            Phone = DefaultPhone,
            PasswordHash = "hash",
            SecurityStamp = originalStamp,
            Role = UserRoles.CommuneUser,
            CommuneId = 5,
        });
        await db.SaveChangesAsync();

        var refreshMock = new Mock<IRefreshTokenService>();
        var stampCacheMock = new Mock<ISecurityStampCache>();
        var timeProvider = Mock.Of<IDateTimeProvider>();
        var svc = new UserAuthorizationService(db, refreshMock.Object,
            Mock.Of<IFeatureCleanupService>(), timeProvider, stampCacheMock.Object);

        var result = await svc.UpdateManagedUserAsync(
            callerId, UserRoles.DairaAdmin, callerCommuneId: null, callerDairaId: 1, callerWilayaId: null,
            targetId,
            new UpdateAdminRequest(Name: null, Email: null, Phone: null,
                Role: null, CommuneId: 6, DairaId: null, WilayaId: null,
                Password: DefaultPassword));

        Assert.True(result.IsSuccess);
        var target = await db.Users.FindAsync(targetId);
        Assert.NotNull(target);
        Assert.NotEqual(originalStamp, target.SecurityStamp);
        refreshMock.Verify(r => r.RevokeAllUserTokensAsync(targetId, It.IsAny<CancellationToken>()), Times.Once);
        stampCacheMock.Verify(c => c.EvictStamp(targetId), Times.Once);
    }

    [Fact]
    public async Task UpdateManagedUserAsync_ProfileOnlyEdit_KeepsSessionsAlive()
    {
        using var db = CreateInMemoryDb("UserAuthProfileEditKeepsSessions");
        var callerId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = callerId,
            Username = "caller",
            Email = "caller@test.com",
            Name = "Caller",
            Phone = DefaultPhone,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(DefaultPassword),
            SecurityStamp = User.GenerateSecurityStamp(),
            Role = UserRoles.NationalAdmin,
        });
        var targetId = Guid.NewGuid();
        var originalStamp = User.GenerateSecurityStamp();
        db.Users.Add(new User
        {
            Id = targetId,
            Username = "target",
            Email = "target@test.com",
            Name = "Target",
            Phone = DefaultPhone,
            PasswordHash = "hash",
            SecurityStamp = originalStamp,
            Role = UserRoles.WilayaAdmin,
            WilayaId = 1,
        });
        await db.SaveChangesAsync();

        var refreshMock = new Mock<IRefreshTokenService>();
        var stampCacheMock = new Mock<ISecurityStampCache>();
        var timeProvider = Mock.Of<IDateTimeProvider>();
        var svc = new UserAuthorizationService(db, refreshMock.Object,
            Mock.Of<IFeatureCleanupService>(), timeProvider, stampCacheMock.Object);

        var result = await svc.UpdateManagedUserAsync(
            callerId, UserRoles.NationalAdmin, callerCommuneId: null, callerDairaId: null, callerWilayaId: null,
            targetId,
            new UpdateAdminRequest(Name: "Renamed", Email: null, Phone: null,
                Role: null, CommuneId: null, DairaId: null, WilayaId: null,
                Password: null));

        Assert.True(result.IsSuccess);
        var target = await db.Users.FindAsync(targetId);
        Assert.NotNull(target);
        Assert.Equal(originalStamp, target.SecurityStamp);
        refreshMock.Verify(r => r.RevokeAllUserTokensAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        stampCacheMock.Verify(c => c.EvictStamp(It.IsAny<Guid>()), Times.Never);
    }
}
