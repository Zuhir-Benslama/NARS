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
        new(
            db,
            Mock.Of<IRefreshTokenService>(),
            Mock.Of<IAccountLockoutService>(),
            Mock.Of<IFeatureCleanupService>(),
            Mock.Of<IDateTimeProvider>(),
            Mock.Of<ISecurityStampCache>());

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
            PasswordHash = DefaultPasswordHash,
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
            Mock.Of<IAccountLockoutService>(), Mock.Of<IFeatureCleanupService>(), timeProvider, stampCacheMock.Object);

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

    // ── CanCreateRole permission matrix ─────────────────────────────────

    [Theory]
    [InlineData(UserRoles.NationalAdmin, UserRoles.WilayaAdmin)]
    [InlineData(UserRoles.WilayaAdmin, UserRoles.DairaAdmin)]
    [InlineData(UserRoles.DairaAdmin, UserRoles.CommuneUser)]
    [InlineData(UserRoles.CommuneUser, UserRoles.FieldWorker)]
    public void CanCreateRole_ValidHierarchyTransitions_ReturnsTrue(string callerRole, string targetRole)
    {
        using var db = CreateInMemoryDb("RoleMatrixValid");
        var svc = CreateService(db);

        Assert.True(svc.CanCreateRole(callerRole, targetRole));
    }

    [Theory]
    [InlineData(UserRoles.NationalAdmin, UserRoles.NationalAdmin)]
    [InlineData(UserRoles.WilayaAdmin, UserRoles.WilayaAdmin)]
    [InlineData(UserRoles.DairaAdmin, UserRoles.WilayaAdmin)]
    [InlineData(UserRoles.CommuneUser, UserRoles.WilayaAdmin)]
    [InlineData(UserRoles.CommuneUser, UserRoles.CommuneUser)]
    [InlineData(UserRoles.FieldWorker, UserRoles.FieldWorker)]
    [InlineData(UserRoles.WilayaAdmin, UserRoles.NationalAdmin)]
    public void CanCreateRole_NonAdjacentOrSameLevelTransitions_ReturnsFalse(string callerRole, string targetRole)
    {
        using var db = CreateInMemoryDb("RoleMatrixInvalid");
        var svc = CreateService(db);

        Assert.False(svc.CanCreateRole(callerRole, targetRole));
    }

    // ── Scope validation ────────────────────────────────────────────────

    [Fact]
    public async Task ValidateCreateUserScopeAsync_CommuneUserToFieldWorker_ReturnsValid()
    {
        using var db = CreateInMemoryDb("ScopeCreateFieldWorker");
        var svc = CreateService(db);

        var result = await svc.ValidateCreateUserScopeAsync(
            UserRoles.CommuneUser, callerDairaId: null, callerWilayaId: null,
            UserRoles.FieldWorker, communeId: 1, dairaId: null, wilayaId: null);

        Assert.Null(result.Error);
    }

    [Fact]
    public async Task ValidateManagedUserScopeAsync_FieldWorkerOutsideCommune_ReturnsDenied()
    {
        using var db = CreateInMemoryDb("ScopeManagedFieldWorkerOut");
        var svc = CreateService(db);

        var result = await svc.ValidateManagedUserScopeAsync(
            UserRoles.CommuneUser, callerCommuneId: 1, callerDairaId: null, callerWilayaId: null,
            UserRoles.FieldWorker, communeId: 2, dairaId: null, wilayaId: null);

        Assert.NotNull(result.Error);
        Assert.True(result.IsAuthorizationFailure);
    }

    [Fact]
    public async Task ValidateManagedUserScopeAsync_FieldWorkerInsideCommune_ReturnsValid()
    {
        using var db = CreateInMemoryDb("ScopeManagedFieldWorkerIn");
        var svc = CreateService(db);

        var result = await svc.ValidateManagedUserScopeAsync(
            UserRoles.CommuneUser, callerCommuneId: 1, callerDairaId: null, callerWilayaId: null,
            UserRoles.FieldWorker, communeId: 1, dairaId: null, wilayaId: null);

        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(NonExistentId)]
    public async Task ValidateCreateUserScopeAsync_DairaToCommune_InvalidCommune_ReturnsError(int? communeId)
    {
        using var db = CreateInMemoryDb("ScopeDairaCommuneInvalid");
        db.Communes.Add(new Commune { CommuneId = 5, DairaId = 1, CommuneFr = "Commune A" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        var result = await svc.ValidateCreateUserScopeAsync(
            UserRoles.DairaAdmin, callerDairaId: 1, callerWilayaId: null,
            UserRoles.CommuneUser, communeId, dairaId: null, wilayaId: null);

        Assert.NotNull(result.Error);
        Assert.False(result.IsAuthorizationFailure);
    }

    [Fact]
    public async Task ValidateCreateUserScopeAsync_DairaToCommune_OtherDaira_ReturnsDenied()
    {
        using var db = CreateInMemoryDb("ScopeDairaCommuneOtherDaira");
        db.Communes.Add(new Commune { CommuneId = 5, DairaId = 1, CommuneFr = "Commune A" });
        db.Communes.Add(new Commune { CommuneId = 6, DairaId = 2, CommuneFr = "Commune B" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        var result = await svc.ValidateCreateUserScopeAsync(
            UserRoles.DairaAdmin, callerDairaId: 1, callerWilayaId: null,
            UserRoles.CommuneUser, communeId: 6, dairaId: null, wilayaId: null);

        Assert.NotNull(result.Error);
        Assert.True(result.IsAuthorizationFailure);
    }

    [Fact]
    public async Task ValidateCreateUserScopeAsync_DairaToCommune_Valid_ReturnsValid()
    {
        using var db = CreateInMemoryDb("ScopeDairaCommuneValid");
        db.Communes.Add(new Commune { CommuneId = 5, DairaId = 1, CommuneFr = "Commune A" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        var result = await svc.ValidateCreateUserScopeAsync(
            UserRoles.DairaAdmin, callerDairaId: 1, callerWilayaId: null,
            UserRoles.CommuneUser, communeId: 5, dairaId: null, wilayaId: null);

        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(NonExistentId)]
    public async Task ValidateCreateUserScopeAsync_WilayaToDaira_InvalidDaira_ReturnsError(int? dairaId)
    {
        using var db = CreateInMemoryDb("ScopeWilayaDairaInvalid");
        db.Dairas.Add(new Daira { DairaId = 10, WilayaId = 1, DairaFr = "Daira A" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        var result = await svc.ValidateCreateUserScopeAsync(
            UserRoles.WilayaAdmin, callerDairaId: null, callerWilayaId: 1,
            UserRoles.DairaAdmin, communeId: null, dairaId, wilayaId: null);

        Assert.NotNull(result.Error);
        Assert.False(result.IsAuthorizationFailure);
    }

    [Fact]
    public async Task ValidateCreateUserScopeAsync_WilayaToDaira_OtherWilaya_ReturnsDenied()
    {
        using var db = CreateInMemoryDb("ScopeWilayaDairaOtherWilaya");
        db.Dairas.Add(new Daira { DairaId = 10, WilayaId = 1, DairaFr = "Daira A" });
        db.Dairas.Add(new Daira { DairaId = 11, WilayaId = 2, DairaFr = "Daira B" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        var result = await svc.ValidateCreateUserScopeAsync(
            UserRoles.WilayaAdmin, callerDairaId: null, callerWilayaId: 1,
            UserRoles.DairaAdmin, communeId: null, dairaId: 11, wilayaId: null);

        Assert.NotNull(result.Error);
        Assert.True(result.IsAuthorizationFailure);
    }

    [Fact]
    public async Task ValidateCreateUserScopeAsync_WilayaToDaira_Valid_ReturnsValid()
    {
        using var db = CreateInMemoryDb("ScopeWilayaDairaValid");
        db.Dairas.Add(new Daira { DairaId = 10, WilayaId = 1, DairaFr = "Daira A" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        var result = await svc.ValidateCreateUserScopeAsync(
            UserRoles.WilayaAdmin, callerDairaId: null, callerWilayaId: 1,
            UserRoles.DairaAdmin, communeId: null, dairaId: 10, wilayaId: null);

        Assert.Null(result.Error);
    }

    [Fact]
    public async Task ValidateCreateUserScopeAsync_NationalToWilaya_MissingWilaya_ReturnsError()
    {
        using var db = CreateInMemoryDb("ScopeNationalWilayaMissing");
        var svc = CreateService(db);

        var result = await svc.ValidateCreateUserScopeAsync(
            UserRoles.NationalAdmin, callerDairaId: null, callerWilayaId: null,
            UserRoles.WilayaAdmin, communeId: null, dairaId: null, wilayaId: null);

        Assert.NotNull(result.Error);
        Assert.False(result.IsAuthorizationFailure);
    }

    [Fact]
    public async Task ValidateCreateUserScopeAsync_UnsupportedTransition_ReturnsError()
    {
        using var db = CreateInMemoryDb("ScopeUnsupported");
        var svc = CreateService(db);

        var result = await svc.ValidateCreateUserScopeAsync(
            UserRoles.NationalAdmin, callerDairaId: null, callerWilayaId: null,
            UserRoles.NationalAdmin, communeId: null, dairaId: null, wilayaId: null);

        Assert.NotNull(result.Error);
        Assert.False(result.IsAuthorizationFailure);
    }

    // ── GetManageableUsersAsync ─────────────────────────────────────────

    private static async Task<UserAuthorizationService> SeedManageableUsersAsync(string dbName)
    {
        var db = CreateInMemoryDb(dbName);
        db.Communes.AddRange(
            new Commune { CommuneId = 5, DairaId = 1, CommuneFr = "Commune A" },
            new Commune { CommuneId = 6, DairaId = 1, CommuneFr = "Commune B" });
        db.Dairas.AddRange(
            new Daira { DairaId = 10, WilayaId = 1, DairaFr = "Daira A" },
            new Daira { DairaId = 11, WilayaId = 2, DairaFr = "Daira B" });
        db.Users.AddRange(
            new User { Id = Guid.NewGuid(), Username = "wilaya_a", Name = "Wilaya A", Email = AltEmail, Phone = DefaultPhone, PasswordHash = DummyPasswordHash, Role = UserRoles.WilayaAdmin, WilayaId = 1 },
            new User { Id = Guid.NewGuid(), Username = "daira_a", Name = "Daira A", Email = AltEmail, Phone = DefaultPhone, PasswordHash = DummyPasswordHash, Role = UserRoles.DairaAdmin, DairaId = 10 },
            new User { Id = Guid.NewGuid(), Username = "daira_b", Name = "Daira B", Email = AltEmail, Phone = DefaultPhone, PasswordHash = DummyPasswordHash, Role = UserRoles.DairaAdmin, DairaId = 11 },
            new User { Id = Guid.NewGuid(), Username = "commune_a", Name = "Commune A", Email = AltEmail, Phone = DefaultPhone, PasswordHash = DummyPasswordHash, Role = UserRoles.CommuneUser, CommuneId = 5 },
            new User { Id = Guid.NewGuid(), Username = "commune_b", Name = "Commune B", Email = AltEmail, Phone = DefaultPhone, PasswordHash = DummyPasswordHash, Role = UserRoles.CommuneUser, CommuneId = 6 },
            new User { Id = Guid.NewGuid(), Username = "worker_a", Name = "Worker A", Email = AltEmail, Phone = DefaultPhone, PasswordHash = DummyPasswordHash, Role = UserRoles.FieldWorker, CommuneId = 5 });
        await db.SaveChangesAsync();
        return CreateService(db);
    }

    // Seeds the dominant UpdateManagedUserAsync scenario: a NationalAdmin caller
    // editing a WilayaAdmin target (wilaya 1). Neither authenticates, so a
    // store-only hash suffices. Returns fresh caller/target ids for the request.
    private static async Task<(Guid CallerId, Guid TargetId)> SeedNationalAdminEditingWilayaAdminAsync(AppDbContext db)
    {
        var callerId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = callerId,
            Username = "caller",
            Email = "caller@test.com",
            Name = "Caller",
            Phone = DefaultPhone,
            PasswordHash = DefaultPasswordHash,
            Role = UserRoles.NationalAdmin,
        });
        var targetId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = targetId,
            Username = "target",
            Email = "target@test.com",
            Name = "Target",
            Phone = DefaultPhone,
            PasswordHash = "hash",
            Role = UserRoles.WilayaAdmin,
            WilayaId = 1,
        });
        await db.SaveChangesAsync();
        return (callerId, targetId);
    }

    [Fact]
    public async Task GetManageableUsersAsync_NationalAdmin_ReturnsOnlyWilayaAdmins()
    {
        var svc = await SeedManageableUsersAsync("ManageableNational");
        var result = await svc.GetManageableUsersAsync(
            UserRoles.NationalAdmin, communeId: null, dairaId: null, wilayaId: null);

        Assert.Equal(1, result.Total);
        Assert.All(result.Items, u => Assert.Equal(UserRoles.WilayaAdmin, u.Role));
    }

    [Fact]
    public async Task GetManageableUsersAsync_WilayaAdmin_ReturnsDairaAdminsInWilaya()
    {
        var svc = await SeedManageableUsersAsync("ManageableWilaya");
        var result = await svc.GetManageableUsersAsync(
            UserRoles.WilayaAdmin, communeId: null, dairaId: null, wilayaId: 1);

        Assert.Equal(1, result.Total);
        Assert.Equal("daira_a", Assert.Single(result.Items).Username);
    }

    [Fact]
    public async Task GetManageableUsersAsync_DairaAdmin_ReturnsCommuneUsersInDaira()
    {
        var svc = await SeedManageableUsersAsync("ManageableDaira");
        var result = await svc.GetManageableUsersAsync(
            UserRoles.DairaAdmin, communeId: null, dairaId: 1, wilayaId: null);

        Assert.Equal(2, result.Total);
    }

    [Fact]
    public async Task GetManageableUsersAsync_CommuneUser_ReturnsFieldWorkersInCommune()
    {
        var svc = await SeedManageableUsersAsync("ManageableCommune");
        var result = await svc.GetManageableUsersAsync(
            UserRoles.CommuneUser, communeId: 5, dairaId: null, wilayaId: null);

        Assert.Equal(1, result.Total);
        Assert.Equal("worker_a", Assert.Single(result.Items).Username);
    }

    [Fact]
    public async Task GetManageableUsersAsync_UnknownRole_ReturnsEmptyPage()
    {
        var svc = await SeedManageableUsersAsync("ManageableUnknown");
        var result = await svc.GetManageableUsersAsync(
            "unknown_role", communeId: null, dairaId: null, wilayaId: null);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.Total);
    }

    // ── VerifyCredentialsAsync ──────────────────────────────────────────

    [Fact]
    public async Task VerifyCredentialsAsync_CorrectPassword_ReturnsSuccess()
    {
        using var db = CreateInMemoryDb("VerifySuccess");
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Username = "locked",
            Name = "Locked",
            Email = AltEmail,
            Phone = DefaultPhone,
            PasswordHash = DefaultPasswordHash,
            Role = UserRoles.FieldWorker,
        });
        await db.SaveChangesAsync();
        var timeProvider = new Mock<IDateTimeProvider>();
        timeProvider.Setup(t => t.UtcNow).Returns(FixedUtcNow);
        var accountLockoutMock = new Mock<IAccountLockoutService>();
        var svc = new UserAuthorizationService(db, Mock.Of<IRefreshTokenService>(), accountLockoutMock.Object,
            Mock.Of<IFeatureCleanupService>(), timeProvider.Object, Mock.Of<ISecurityStampCache>());

        var result = await svc.VerifyCredentialsAsync("locked", DefaultPassword, 5, 30);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.User);
        Assert.Equal("locked", result.User.Username);
    }

    [Fact]
    public async Task VerifyCredentialsAsync_WrongPassword_RecordsFailureAndReturnsInvalid()
    {
        using var db = CreateInMemoryDb("VerifyWrongPassword");
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Username = "locked",
            Name = "Locked",
            Email = AltEmail,
            Phone = DefaultPhone,
            PasswordHash = DefaultPasswordHash,
            Role = UserRoles.FieldWorker,
        });
        await db.SaveChangesAsync();
        var timeProvider = new Mock<IDateTimeProvider>();
        timeProvider.Setup(t => t.UtcNow).Returns(FixedUtcNow);
        var accountLockoutMock = new Mock<IAccountLockoutService>();
        var svc = new UserAuthorizationService(db, Mock.Of<IRefreshTokenService>(), accountLockoutMock.Object,
            Mock.Of<IFeatureCleanupService>(), timeProvider.Object, Mock.Of<ISecurityStampCache>());

        var result = await svc.VerifyCredentialsAsync("locked", "wrong-password", 5, 30);

        Assert.False(result.IsSuccess);
        Assert.Equal(CredentialCheckStatus.InvalidCredentials, result.Status);
        accountLockoutMock.Verify(a => a.RecordFailedLoginAsync(It.IsAny<User>(), 5, 30, FixedUtcNow, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task VerifyCredentialsAsync_LockedAccount_ReturnsLockedWithoutRecordingFailure()
    {
        using var db = CreateInMemoryDb("VerifyLocked");
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Username = "locked",
            Name = "Locked",
            Email = AltEmail,
            Phone = DefaultPhone,
            PasswordHash = DefaultPasswordHash,
            Role = UserRoles.FieldWorker,
            LockedUntil = FixedUtcNow.AddMinutes(10),
        });
        await db.SaveChangesAsync();
        var timeProvider = new Mock<IDateTimeProvider>();
        timeProvider.Setup(t => t.UtcNow).Returns(FixedUtcNow);
        var accountLockoutMock = new Mock<IAccountLockoutService>();
        var svc = new UserAuthorizationService(db, Mock.Of<IRefreshTokenService>(), accountLockoutMock.Object,
            Mock.Of<IFeatureCleanupService>(), timeProvider.Object, Mock.Of<ISecurityStampCache>());

        var result = await svc.VerifyCredentialsAsync("locked", DefaultPassword, 5, 30);

        Assert.False(result.IsSuccess);
        Assert.Equal(CredentialCheckStatus.Locked, result.Status);
        accountLockoutMock.Verify(
            a => a.RecordFailedLoginAsync(It.IsAny<User>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task VerifyCredentialsAsync_UnknownUser_ReturnsInvalid()
    {
        using var db = CreateInMemoryDb("VerifyUnknown");
        var timeProvider = new Mock<IDateTimeProvider>();
        timeProvider.Setup(t => t.UtcNow).Returns(FixedUtcNow);
        var accountLockoutMock = new Mock<IAccountLockoutService>();
        var svc = new UserAuthorizationService(db, Mock.Of<IRefreshTokenService>(), accountLockoutMock.Object,
            Mock.Of<IFeatureCleanupService>(), timeProvider.Object, Mock.Of<ISecurityStampCache>());

        var result = await svc.VerifyCredentialsAsync("no-such-user", DefaultPassword, 5, 30);

        Assert.False(result.IsSuccess);
        Assert.Equal(CredentialCheckStatus.InvalidCredentials, result.Status);
        accountLockoutMock.Verify(
            a => a.RecordFailedLoginAsync(It.IsAny<User>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UpdateManagedUserAsync_CallerCannotManageTargetRole_ReturnsForbidden()
    {
        using var db = CreateInMemoryDb("UserAuthForbiddenTargetRole");
        var callerId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = callerId,
            Username = "caller",
            Email = "caller@test.com",
            Name = "Caller",
            Phone = DefaultPhone,
            PasswordHash = DefaultPasswordHash,
            Role = UserRoles.WilayaAdmin,
            WilayaId = 1,
        });
        var targetId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = targetId,
            Username = "target",
            Email = "target@test.com",
            Name = "Target",
            Phone = DefaultPhone,
            PasswordHash = "hash",
            Role = UserRoles.NationalAdmin,
        });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        var result = await svc.UpdateManagedUserAsync(
            callerId, UserRoles.WilayaAdmin, callerCommuneId: null, callerDairaId: null, callerWilayaId: 1,
            targetId,
            new UpdateAdminRequest(Name: "Renamed", Email: null, Phone: null,
                Role: null, CommuneId: null, DairaId: null, WilayaId: null, Password: null));

        Assert.Equal(UserUpdateErrorCode.Forbidden, result.Code);
    }

    [Fact]
    public async Task UpdateManagedUserAsync_CallerCannotCreateRequestedRole_ReturnsForbidden()
    {
        using var db = CreateInMemoryDb("UserAuthForbiddenRequestedRole");
        var callerId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = callerId,
            Username = "caller",
            Email = "caller@test.com",
            Name = "Caller",
            Phone = DefaultPhone,
            PasswordHash = DefaultPasswordHash,
            Role = UserRoles.DairaAdmin,
            DairaId = 1,
        });
        var targetId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = targetId,
            Username = "target",
            Email = "target@test.com",
            Name = "Target",
            Phone = DefaultPhone,
            PasswordHash = "hash",
            Role = UserRoles.CommuneUser,
            CommuneId = 5,
        });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        var result = await svc.UpdateManagedUserAsync(
            callerId, UserRoles.DairaAdmin, callerCommuneId: null, callerDairaId: 1, callerWilayaId: null,
            targetId,
            new UpdateAdminRequest(Name: null, Email: null, Phone: null,
                Role: UserRoles.WilayaAdmin, CommuneId: null, DairaId: null, WilayaId: null, Password: null));

        Assert.Equal(UserUpdateErrorCode.Forbidden, result.Code);
    }

    [Fact]
    public async Task UpdateManagedUserAsync_SensitiveChangeWithoutPassword_ReturnsPasswordRequired()
    {
        using var db = CreateInMemoryDb("UserAuthPasswordRequired");
        var (callerId, targetId) = await SeedNationalAdminEditingWilayaAdminAsync(db);
        var svc = CreateService(db);

        var result = await svc.UpdateManagedUserAsync(
            callerId, UserRoles.NationalAdmin, callerCommuneId: null, callerDairaId: null, callerWilayaId: null,
            targetId,
            new UpdateAdminRequest(Name: null, Email: null, Phone: null,
                Role: null, CommuneId: null, DairaId: null, WilayaId: 2, Password: null));

        Assert.Equal(UserUpdateErrorCode.PasswordRequired, result.Code);
    }

    [Fact]
    public async Task UpdateManagedUserAsync_SensitiveChangeWrongPassword_ReturnsInvalidPassword()
    {
        using var db = CreateInMemoryDb("UserAuthInvalidPassword");
        var (callerId, targetId) = await SeedNationalAdminEditingWilayaAdminAsync(db);
        var svc = CreateService(db);

        var result = await svc.UpdateManagedUserAsync(
            callerId, UserRoles.NationalAdmin, callerCommuneId: null, callerDairaId: null, callerWilayaId: null,
            targetId,
            new UpdateAdminRequest(Name: null, Email: null, Phone: null,
                Role: null, CommuneId: null, DairaId: null, WilayaId: 2, Password: "wrong-password"));

        Assert.Equal(UserUpdateErrorCode.InvalidPassword, result.Code);
    }

    [Fact]
    public async Task UpdateManagedUserAsync_NameTooLong_ReturnsInvalid()
    {
        using var db = CreateInMemoryDb("UserAuthInvalidName");
        var (callerId, targetId) = await SeedNationalAdminEditingWilayaAdminAsync(db);
        var svc = CreateService(db);

        var result = await svc.UpdateManagedUserAsync(
            callerId, UserRoles.NationalAdmin, callerCommuneId: null, callerDairaId: null, callerWilayaId: null,
            targetId,
            new UpdateAdminRequest(
                Name: new string('x', UserFieldValidator.MaxNameLength + 1),
                Email: null, Phone: null,
                Role: null, CommuneId: null, DairaId: null, WilayaId: null, Password: null));

        Assert.Equal(UserUpdateErrorCode.Invalid, result.Code);
    }

    [Fact]
    public async Task UpdateManagedUserAsync_InvalidEmail_ReturnsInvalid()
    {
        using var db = CreateInMemoryDb("UserAuthInvalidEmail");
        var (callerId, targetId) = await SeedNationalAdminEditingWilayaAdminAsync(db);
        var svc = CreateService(db);

        var result = await svc.UpdateManagedUserAsync(
            callerId, UserRoles.NationalAdmin, callerCommuneId: null, callerDairaId: null, callerWilayaId: null,
            targetId,
            new UpdateAdminRequest(Name: null, Email: "not-an-email", Phone: null,
                Role: null, CommuneId: null, DairaId: null, WilayaId: null, Password: null));

        Assert.Equal(UserUpdateErrorCode.Invalid, result.Code);
    }

    [Fact]
    public async Task UpdateManagedUserAsync_EmailConflict_ReturnsEmailConflict()
    {
        using var db = CreateInMemoryDb("UserAuthEmailConflict");
        var callerId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = callerId,
            Username = "caller",
            Email = "caller@test.com",
            Name = "Caller",
            Phone = DefaultPhone,
            PasswordHash = DefaultPasswordHash,
            Role = UserRoles.NationalAdmin,
        });
        var targetId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = targetId,
            Username = "target",
            Email = "target@test.com",
            Name = "Target",
            Phone = DefaultPhone,
            PasswordHash = "hash",
            Role = UserRoles.WilayaAdmin,
            WilayaId = 1,
        });
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Username = "other",
            Email = "taken@test.com",
            Name = "Other",
            Phone = DefaultPhone,
            PasswordHash = "hash",
            Role = UserRoles.WilayaAdmin,
            WilayaId = 1,
        });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        var result = await svc.UpdateManagedUserAsync(
            callerId, UserRoles.NationalAdmin, callerCommuneId: null, callerDairaId: null, callerWilayaId: null,
            targetId,
            new UpdateAdminRequest(Name: null, Email: "taken@test.com", Phone: null,
                Role: null, CommuneId: null, DairaId: null, WilayaId: null, Password: null));

        Assert.Equal(UserUpdateErrorCode.EmailConflict, result.Code);
    }

    [Fact]
    public async Task UpdateManagedUserAsync_PhoneTooLong_ReturnsInvalid()
    {
        using var db = CreateInMemoryDb("UserAuthInvalidPhone");
        var callerId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = callerId,
            Username = "caller",
            Email = "caller@test.com",
            Name = "Caller",
            Phone = DefaultPhone,
            PasswordHash = DefaultPasswordHash,
            Role = UserRoles.NationalAdmin,
        });
        var targetId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = targetId,
            Username = "target",
            Email = "target@test.com",
            Name = "Target",
            Phone = DefaultPhone,
            PasswordHash = "hash",
            Role = UserRoles.WilayaAdmin,
            WilayaId = 1,
        });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        var result = await svc.UpdateManagedUserAsync(
            callerId, UserRoles.NationalAdmin, callerCommuneId: null, callerDairaId: null, callerWilayaId: null,
            targetId,
            new UpdateAdminRequest(
                Name: null, Email: null, Phone: new string('9', UserFieldValidator.MaxPhoneLength + 1),
                Role: null, CommuneId: null, DairaId: null, WilayaId: null, Password: null));

        Assert.Equal(UserUpdateErrorCode.Invalid, result.Code);
    }

    [Fact]
    public async Task UpdateManagedUserAsync_InvalidGeographyForRole_ReturnsInvalid()
    {
        using var db = CreateInMemoryDb("UserAuthInvalidGeo");
        var callerId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = callerId,
            Username = "caller",
            Email = "caller@test.com",
            Name = "Caller",
            Phone = DefaultPhone,
            PasswordHash = DefaultPasswordHash,
            SecurityStamp = User.GenerateSecurityStamp(),
            Role = UserRoles.NationalAdmin,
        });
        // Target is a wilaya_admin with NO geographic anchor. Reassigning the
        // same role without supplying a wilaya_id is therefore genuinely invalid:
        // the effective geography (merged from the target and the request) still
        // lacks the wilaya that wilaya_admin requires.
        var targetId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = targetId,
            Username = "target",
            Email = "target@test.com",
            Name = "Target",
            Phone = DefaultPhone,
            PasswordHash = "hash",
            Role = UserRoles.WilayaAdmin,
            WilayaId = null,
        });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        var result = await svc.UpdateManagedUserAsync(
            callerId, UserRoles.NationalAdmin, callerCommuneId: null, callerDairaId: null, callerWilayaId: null,
            targetId,
            new UpdateAdminRequest(Name: null, Email: null, Phone: null,
                Role: UserRoles.WilayaAdmin, CommuneId: null, DairaId: null, WilayaId: null,
                Password: DefaultPassword));

        Assert.Equal(UserUpdateErrorCode.Invalid, result.Code);
    }

    [Fact]
    public async Task UpdateManagedUserAsync_RoleAndWilayaChange_AppliesAndInvalidatesSessions()
    {
        using var db = CreateInMemoryDb("UserAuthRoleWilayaChange");
        var callerId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = callerId,
            Username = "caller",
            Email = "caller@test.com",
            Name = "Caller",
            Phone = DefaultPhone,
            PasswordHash = DefaultPasswordHash,
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
        var svc = new UserAuthorizationService(db, refreshMock.Object,
            Mock.Of<IAccountLockoutService>(), Mock.Of<IFeatureCleanupService>(), Mock.Of<IDateTimeProvider>(), stampCacheMock.Object);

        var result = await svc.UpdateManagedUserAsync(
            callerId, UserRoles.NationalAdmin, callerCommuneId: null, callerDairaId: null, callerWilayaId: null,
            targetId,
            new UpdateAdminRequest(Name: null, Email: null, Phone: null,
                Role: UserRoles.WilayaAdmin, CommuneId: null, DairaId: null, WilayaId: 2,
                Password: DefaultPassword));

        Assert.True(result.IsSuccess);
        var target = await db.Users.FindAsync(targetId);
        Assert.NotNull(target);
        Assert.Equal(2, target.WilayaId);
        Assert.NotEqual(originalStamp, target.SecurityStamp);
        refreshMock.Verify(r => r.RevokeAllUserTokensAsync(targetId, It.IsAny<CancellationToken>()), Times.Once);
        stampCacheMock.Verify(c => c.EvictStamp(targetId), Times.Once);
    }

    [Fact]
    public async Task UpdateManagedUserAsync_DairaChange_Applies()
    {
        using var db = CreateInMemoryDb("UserAuthDairaChange");
        var callerId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = callerId,
            Username = "caller",
            Email = "caller@test.com",
            Name = "Caller",
            Phone = DefaultPhone,
            PasswordHash = DefaultPasswordHash,
            Role = UserRoles.WilayaAdmin,
            WilayaId = 1,
        });
        var targetId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = targetId,
            Username = "target",
            Email = "target@test.com",
            Name = "Target",
            Phone = DefaultPhone,
            PasswordHash = "hash",
            Role = UserRoles.DairaAdmin,
            DairaId = 10,
        });
        db.Dairas.AddRange(
            new Daira { DairaId = 10, WilayaId = 1, DairaFr = "Daira A" },
            new Daira { DairaId = 11, WilayaId = 1, DairaFr = "Daira B" });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        var result = await svc.UpdateManagedUserAsync(
            callerId, UserRoles.WilayaAdmin, callerCommuneId: null, callerDairaId: null, callerWilayaId: 1,
            targetId,
            new UpdateAdminRequest(Name: null, Email: null, Phone: null,
                Role: null, CommuneId: null, DairaId: 11, WilayaId: null, Password: DefaultPassword));

        Assert.True(result.IsSuccess);
        var target = await db.Users.FindAsync(targetId);
        Assert.NotNull(target);
        Assert.Equal(11, target.DairaId);
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
            PasswordHash = DefaultPasswordHash,
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
            Mock.Of<IAccountLockoutService>(), Mock.Of<IFeatureCleanupService>(), timeProvider, stampCacheMock.Object);

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

