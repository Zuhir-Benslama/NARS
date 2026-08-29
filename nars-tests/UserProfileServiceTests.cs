using Moq;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests;

public sealed class UserProfileServiceTests
{
    private sealed class Harness
    {
        public AppDbContext Db { get; }
        public Mock<ISecurityStampCache> StampCache { get; } = new();
        public UserProfileService Service { get; }

        public Harness(Action<AppDbContext> seed)
        {
            var (db, factory) = CreateInMemoryDbPair("UserProfile_" + Guid.NewGuid().ToString("N"));
            Db = db;
            seed(Db);
            Db.SaveChanges();
            Service = new UserProfileService(factory, StampCache.Object);
        }
    }

    private static User SeedUser(AppDbContext db, string email = "profile@test.com", string username = "profileuser")
    {
        var user = new User
        {
            Id = UserId,
            Username = username,
            Name = "Profile User",
            Email = email,
            Phone = DefaultPhone,
            PasswordHash = DefaultPasswordHash,
            SecurityStamp = User.GenerateSecurityStamp(),
            Role = UserRoles.FieldWorker,
        };
        db.Users.Add(user);
        return user;
    }

    [Fact]
    public async Task GetUserByIdAsync_ExistingUser_ReturnsUser()
    {
        var h = new Harness(db => SeedUser(db));
        var user = await h.Service.GetUserByIdAsync(UserId);
        Assert.NotNull(user);
        Assert.Equal("profileuser", user.Username);
    }

    [Fact]
    public async Task GetUserByIdAsync_UnknownUser_ReturnsNull()
    {
        var h = new Harness(db => SeedUser(db));
        var user = await h.Service.GetUserByIdAsync(Guid.NewGuid());
        Assert.Null(user);
    }

    [Fact]
    public async Task IsUsernameTakenAsync_Taken_ReturnsTrue()
    {
        var h = new Harness(db => SeedUser(db));
        Assert.True(await h.Service.IsUsernameTakenAsync("profileuser"));
    }

    [Fact]
    public async Task IsUsernameTakenAsync_Free_ReturnsFalse()
    {
        var h = new Harness(db => SeedUser(db));
        Assert.False(await h.Service.IsUsernameTakenAsync("otheruser"));
    }

    [Fact]
    public async Task IsEmailTakenAsync_Taken_ReturnsTrue()
    {
        var h = new Harness(db => SeedUser(db));
        Assert.True(await h.Service.IsEmailTakenAsync("profile@test.com"));
    }

    [Fact]
    public async Task IsEmailTakenAsync_Free_ReturnsFalse()
    {
        var h = new Harness(db => SeedUser(db));
        Assert.False(await h.Service.IsEmailTakenAsync("free@test.com"));
    }

    [Fact]
    public async Task UpdateUserAsync_PersistsChanges()
    {
        var h = new Harness(db => SeedUser(db));
        var user = await h.Service.GetUserByIdAsync(UserId);
        Assert.NotNull(user);
        user.Name = "Renamed";
        await h.Service.UpdateUserAsync(user);
        var updated = await h.Service.GetUserByIdAsync(UserId);
        Assert.NotNull(updated);
        Assert.Equal("Renamed", updated.Name);
    }

    [Fact]
    public async Task UpdateCredentialsAsync_UnknownUser_ReturnsUserNotFound()
    {
        var h = new Harness(db => SeedUser(db));
        var result = await h.Service.UpdateCredentialsAsync(Guid.NewGuid(),
            new UpdateUserRequest(Username: null, Email: null, CurrentPassword: null, Password: null));
        Assert.Equal(CredentialUpdateError.UserNotFound, result.Error);
    }

    [Fact]
    public async Task UpdateCredentialsAsync_EmptyRequest_IsNoOpSuccess()
    {
        var h = new Harness(db => SeedUser(db));
        var result = await h.Service.UpdateCredentialsAsync(UserId,
            new UpdateUserRequest(Username: null, Email: null, CurrentPassword: null, Password: null));
        Assert.True(result.Succeeded);
        Assert.False(result.PasswordChanged);
        h.StampCache.Verify(c => c.EvictStamp(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task UpdateCredentialsAsync_UsernameTooLong_ReturnsInvalidUsername()
    {
        var h = new Harness(db => SeedUser(db));
        var result = await h.Service.UpdateCredentialsAsync(UserId,
            new UpdateUserRequest(Username: new string('u', UserFieldValidator.MaxUsernameLength + 1),
                Email: null, CurrentPassword: null, Password: null));
        Assert.Equal(CredentialUpdateError.InvalidUsername, result.Error);
    }

    [Fact]
    public async Task UpdateCredentialsAsync_DuplicateUsername_ReturnsDuplicateUsername()
    {
        var h = new Harness(db => SeedUser(db));
        db_AddOtherUser(h.Db);
        var result = await h.Service.UpdateCredentialsAsync(UserId,
            new UpdateUserRequest(Username: "otheruser", Email: null, CurrentPassword: null, Password: null));
        Assert.Equal(CredentialUpdateError.DuplicateUsername, result.Error);
    }

    [Fact]
    public async Task UpdateCredentialsAsync_UsernameChange_NormalizesLowercase()
    {
        var h = new Harness(db => SeedUser(db));
        var result = await h.Service.UpdateCredentialsAsync(UserId,
            new UpdateUserRequest(Username: "  NewUsername  ", Email: null, CurrentPassword: null, Password: null));
        Assert.True(result.Succeeded);
        Assert.Equal("newusername", result.User?.Username);
    }

    [Fact]
    public async Task UpdateCredentialsAsync_InvalidEmail_ReturnsInvalidEmail()
    {
        var h = new Harness(db => SeedUser(db));
        var result = await h.Service.UpdateCredentialsAsync(UserId,
            new UpdateUserRequest(Username: null, Email: "not-an-email", CurrentPassword: null, Password: null));
        Assert.Equal(CredentialUpdateError.InvalidEmail, result.Error);
    }

    [Fact]
    public async Task UpdateCredentialsAsync_DuplicateEmail_ReturnsDuplicateEmail()
    {
        var h = new Harness(db => SeedUser(db));
        db_AddOtherUser(h.Db);
        var result = await h.Service.UpdateCredentialsAsync(UserId,
            new UpdateUserRequest(Username: null, Email: "OTHER@test.com", CurrentPassword: null, Password: null));
        Assert.Equal(CredentialUpdateError.DuplicateEmail, result.Error);
    }

    [Fact]
    public async Task UpdateCredentialsAsync_EmailChange_NormalizesLowercase()
    {
        var h = new Harness(db => SeedUser(db));
        var result = await h.Service.UpdateCredentialsAsync(UserId,
            new UpdateUserRequest(Username: null, Email: "  NewMail@test.com  ", CurrentPassword: null, Password: null));
        Assert.True(result.Succeeded);
        Assert.Equal("newmail@test.com", result.User?.Email);
    }

    [Fact]
    public async Task UpdateCredentialsAsync_PasswordWithoutCurrent_ReturnsWrongCurrentPassword()
    {
        var h = new Harness(db => SeedUser(db));
        var result = await h.Service.UpdateCredentialsAsync(UserId,
            new UpdateUserRequest(Username: null, Email: null, CurrentPassword: null, Password: "NewStr0ng!Pass"));
        Assert.Equal(CredentialUpdateError.WrongCurrentPassword, result.Error);
    }

    [Fact]
    public async Task UpdateCredentialsAsync_WrongCurrentPassword_ReturnsWrongCurrentPassword()
    {
        var h = new Harness(db => SeedUser(db));
        var result = await h.Service.UpdateCredentialsAsync(UserId,
            new UpdateUserRequest(Username: null, Email: null, CurrentPassword: "wrong-password", Password: "NewStr0ng!Pass"));
        Assert.Equal(CredentialUpdateError.WrongCurrentPassword, result.Error);
    }

    [Fact]
    public async Task UpdateCredentialsAsync_WeakPassword_ReturnsWeakPassword()
    {
        var h = new Harness(db => SeedUser(db));
        var result = await h.Service.UpdateCredentialsAsync(UserId,
            new UpdateUserRequest(Username: null, Email: null, CurrentPassword: DefaultPassword, Password: "Weak"));
        Assert.Equal(CredentialUpdateError.WeakPassword, result.Error);
    }

    [Fact]
    public async Task UpdateCredentialsAsync_PasswordChange_RotatesStampAndEvicts()
    {
        var h = new Harness(db => SeedUser(db));
        var before = await h.Service.GetUserByIdAsync(UserId);
        Assert.NotNull(before);
        var originalStamp = before.SecurityStamp;

        var result = await h.Service.UpdateCredentialsAsync(UserId,
            new UpdateUserRequest(Username: null, Email: null, CurrentPassword: DefaultPassword, Password: "NewStr0ng!Pass"));

        Assert.True(result.Succeeded);
        Assert.True(result.PasswordChanged);
        Assert.NotNull(result.User);
        Assert.NotEqual(originalStamp, result.User.SecurityStamp);
        h.StampCache.Verify(c => c.EvictStamp(UserId), Times.Once);
        Assert.True(BCrypt.Net.BCrypt.Verify("NewStr0ng!Pass", result.User.PasswordHash));
    }

    private static void db_AddOtherUser(AppDbContext db)
    {
        if (db.Users.Any(u => u.Username == "otheruser"))
        {
            return;
        }

        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Username = "otheruser",
            Name = "Other",
            Email = "other@test.com",
            Phone = DefaultPhone,
            PasswordHash = "hash",
            Role = UserRoles.FieldWorker,
        });
        db.SaveChanges();
    }
}

