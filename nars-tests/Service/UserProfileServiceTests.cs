using Microsoft.EntityFrameworkCore;
using Moq;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests.Service;

/// <summary>
/// Integration tests for the profile-update business logic in
/// <see cref="UserProfileService"/> (validated at the service layer).
/// </summary>
[Collection(PostgreSqlCollection.CollectionName)]
[Trait("Category", "Service")]
public class UserProfileServiceTests(NarsDatabaseFixture fixture) : IAsyncLifetime
{
    private readonly NarsDatabaseFixture _fixture = fixture;
    private AppDbContext _db = null!;
    private UserProfileService _service = null!;

    public async Task InitializeAsync()
    {
        _db = _fixture.CreateDbContext();
        _service = new UserProfileService(_db, Mock.Of<ISecurityStampCache>());
    }

    public async Task DisposeAsync()
    {
        try { await _db.DisposeAsync(); }
        finally { await _fixture.CleanTablesAsync(); }
    }

    [Fact]
    public async Task UpdateCredentials_UserNotFound_ReturnsUserNotFound()
    {
        var result = await _service.UpdateCredentialsAsync(
            Guid.NewGuid(), new UpdateUserRequest("newname", null, null, null));

        Assert.False(result.Succeeded);
        Assert.Equal(CredentialUpdateError.UserNotFound, result.Error);
    }

    [Fact]
    public async Task UpdateCredentials_ValidUpdate_NormalizesAndPersists()
    {
        var user = await SeedData.CreateUserAsync(_db, UserRoles.CommuneUser, communeId: 1);

        var result = await _service.UpdateCredentialsAsync(
            user.Id, new UpdateUserRequest("  NewUser  ", "  MiXeD@Example.COM ", null, null));

        Assert.True(result.Succeeded);
        Assert.Equal("newuser", result.User!.Username);
        Assert.Equal("mixed@example.com", result.User.Email);

        var reloaded = await _db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.Equal("newuser", reloaded.Username);
        Assert.Equal("mixed@example.com", reloaded.Email);
    }

    [Fact]
    public async Task UpdateCredentials_DuplicateUsername_ReturnsDuplicate()
    {
        var user = await SeedData.CreateUserAsync(_db, UserRoles.CommuneUser, communeId: 1);
        var other = await SeedData.CreateUserAsync(_db, UserRoles.CommuneUser, communeId: 1);

        var result = await _service.UpdateCredentialsAsync(
            user.Id, new UpdateUserRequest(other.Username, null, null, null));

        Assert.False(result.Succeeded);
        Assert.Equal(CredentialUpdateError.DuplicateUsername, result.Error);
    }

    [Fact]
    public async Task UpdateCredentials_DuplicateEmail_ReturnsDuplicate()
    {
        var user = await SeedData.CreateUserAsync(_db, UserRoles.CommuneUser, communeId: 1);
        var other = await SeedData.CreateUserAsync(_db, UserRoles.CommuneUser, communeId: 1);

        var result = await _service.UpdateCredentialsAsync(
            user.Id, new UpdateUserRequest(null, other.Email, null, null));

        Assert.False(result.Succeeded);
        Assert.Equal(CredentialUpdateError.DuplicateEmail, result.Error);
    }

    [Fact]
    public async Task UpdateCredentials_InvalidEmail_ReturnsInvalidEmail()
    {
        var user = await SeedData.CreateUserAsync(_db, UserRoles.CommuneUser, communeId: 1);

        var result = await _service.UpdateCredentialsAsync(
            user.Id, new UpdateUserRequest(null, "not-an-email", null, null));

        Assert.False(result.Succeeded);
        Assert.Equal(CredentialUpdateError.InvalidEmail, result.Error);
    }

    [Fact]
    public async Task UpdateCredentials_OversizeUsername_ReturnsInvalidUsername()
    {
        var user = await SeedData.CreateUserAsync(_db, UserRoles.CommuneUser, communeId: 1);

        var result = await _service.UpdateCredentialsAsync(
            user.Id, new UpdateUserRequest(new string('u', UserFieldValidator.MaxUsernameLength + 1), null, null, null));

        Assert.False(result.Succeeded);
        Assert.Equal(CredentialUpdateError.InvalidUsername, result.Error);
    }

    [Fact]
    public async Task UpdateCredentials_WrongCurrentPassword_ReturnsWrongCurrentPassword()
    {
        var user = await SeedData.CreateUserAsync(_db, UserRoles.CommuneUser, communeId: 1);

        var result = await _service.UpdateCredentialsAsync(
            user.Id, new UpdateUserRequest(null, null, "WrongP@ss", "NewP@ss123"));

        Assert.False(result.Succeeded);
        Assert.Equal(CredentialUpdateError.WrongCurrentPassword, result.Error);
    }

    [Fact]
    public async Task UpdateCredentials_WeakNewPassword_ReturnsWeakPassword()
    {
        var user = await SeedData.CreateUserAsync(_db, UserRoles.CommuneUser, communeId: 1);

        var result = await _service.UpdateCredentialsAsync(
            user.Id, new UpdateUserRequest(null, null, DefaultPassword, "short"));

        Assert.False(result.Succeeded);
        Assert.Equal(CredentialUpdateError.WeakPassword, result.Error);
    }

    [Fact]
    public async Task UpdateCredentials_ValidPasswordChange_HashesAndFlags()
    {
        var user = await SeedData.CreateUserAsync(_db, UserRoles.CommuneUser, communeId: 1);

        var result = await _service.UpdateCredentialsAsync(
            user.Id, new UpdateUserRequest(null, null, DefaultPassword, "NewP@ss123"));

        Assert.True(result.Succeeded);
        Assert.True(result.PasswordChanged);
        Assert.True(BCrypt.Net.BCrypt.Verify("NewP@ss123", result.User!.PasswordHash));

        var reloaded = await _db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.True(BCrypt.Net.BCrypt.Verify("NewP@ss123", reloaded.PasswordHash));
    }

    [Fact]
    public async Task UpdateCredentials_SameUsernameAndEmail_NoConflict_NoChange()
    {
        var user = await SeedData.CreateUserAsync(_db, UserRoles.CommuneUser, communeId: 1);
        var username = user.Username;
        var email = user.Email;

        var result = await _service.UpdateCredentialsAsync(
            user.Id, new UpdateUserRequest(username.ToUpperInvariant(), email.ToUpperInvariant(), null, null));

        Assert.True(result.Succeeded);
        Assert.False(result.PasswordChanged);
        Assert.Equal(username, result.User!.Username);
        Assert.Equal(email, result.User.Email);
    }
}
