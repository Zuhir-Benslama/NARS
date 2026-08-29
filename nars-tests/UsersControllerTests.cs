using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using NarsApi.Controllers;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests;

public class UsersControllerTests
{
    private static UsersController CreateController(
        IUserProfileService? userProfile = null,
        IRefreshTokenService? refreshTokens = null,
        bool authenticated = true,
        Guid? userId = null)
    {
        var ctrl = new UsersController(
            userProfile ?? Mock.Of<IUserProfileService>(),
            refreshTokens ?? Mock.Of<IRefreshTokenService>(),
            Mock.Of<ILogger<UsersController>>(),
            Mock.Of<IWebHostEnvironment>());

        if (authenticated)
        {
            AuthTestHelper.SetUser(ctrl, userId ?? Guid.NewGuid(), UserRoles.CommuneUser);
        }
        else
        {
            ctrl.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };
        }

        return ctrl;
    }

    private static User CreateUser(Guid id, string username = "testuser", string email = "test@example.com") =>
        new()
        {
            Id = id,
            Username = username,
            Email = email,
            Name = "Test User",
            Phone = DefaultPhone,
            PasswordHash = DummyPasswordHash,
            Role = UserRoles.CommuneUser,
            CommuneId = CommuneId1,
        };

    private static UpdateCredentialsResult Success(User user, bool passwordChanged = false) =>
        new(PasswordChanged: passwordChanged, User: user);

    // ── UpdateCredentials ───────────────────────────────────────────────

    [Fact]
    public async Task UpdateCredentials_NoAuth_ThrowsUnauthorized()
    {
        var ctrl = CreateController(authenticated: false);

        // [Authorize] on NarsControllerBase returns 401 via middleware in production.
        // Unit tests bypass the middleware pipeline, so the action reaches
        // NarsControllerBase.RequiredCurrentUserId, whose job is to fail loudly if
        // an unauthenticated code path ever slips through — assert that specific
        // defense-in-depth guard (message match), not just any InvalidOperationException.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ctrl.UpdateCredentials(new UpdateUserRequest("newuser", null, null, null), default));
        Assert.Contains("user_id claim missing", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateCredentials_UserNotFound_Returns404()
    {
        var userId = Guid.NewGuid();
        var mock = new Mock<IUserProfileService>();
        mock.Setup(s => s.UpdateCredentialsAsync(userId, It.IsAny<UpdateUserRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateCredentialsResult(CredentialUpdateError.UserNotFound));

        var ctrl = CreateController(userProfile: mock.Object, userId: userId);

        var result = await ctrl.UpdateCredentials(
            new UpdateUserRequest("newuser", null, null, null), default);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, obj.StatusCode);
    }

    [Fact]
    public async Task UpdateCredentials_DuplicateUsername_Returns409()
    {
        var userId = Guid.NewGuid();
        var mock = new Mock<IUserProfileService>();
        mock.Setup(s => s.UpdateCredentialsAsync(userId, It.IsAny<UpdateUserRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateCredentialsResult(CredentialUpdateError.DuplicateUsername));

        var ctrl = CreateController(userProfile: mock.Object, userId: userId);

        var result = await ctrl.UpdateCredentials(
            new UpdateUserRequest("takenuser", null, null, null), default);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(409, obj.StatusCode);
    }

    [Fact]
    public async Task UpdateCredentials_DuplicateEmail_Returns409()
    {
        var userId = Guid.NewGuid();
        var mock = new Mock<IUserProfileService>();
        mock.Setup(s => s.UpdateCredentialsAsync(userId, It.IsAny<UpdateUserRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateCredentialsResult(CredentialUpdateError.DuplicateEmail));

        var ctrl = CreateController(userProfile: mock.Object, userId: userId);

        var result = await ctrl.UpdateCredentials(
            new UpdateUserRequest(null, "taken@example.com", null, null), default);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(409, obj.StatusCode);
    }

    [Fact]
    public async Task UpdateCredentials_InvalidEmail_Returns400()
    {
        var userId = Guid.NewGuid();
        var mock = new Mock<IUserProfileService>();
        mock.Setup(s => s.UpdateCredentialsAsync(userId, It.IsAny<UpdateUserRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateCredentialsResult(CredentialUpdateError.InvalidEmail, Detail: "Email is not valid."));

        var ctrl = CreateController(userProfile: mock.Object, userId: userId);

        var result = await ctrl.UpdateCredentials(
            new UpdateUserRequest(null, "not-an-email", null, null), default);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, obj.StatusCode);
    }

    [Fact]
    public async Task UpdateCredentials_OversizeUsername_Returns400()
    {
        var userId = Guid.NewGuid();
        var mock = new Mock<IUserProfileService>();
        mock.Setup(s => s.UpdateCredentialsAsync(userId, It.IsAny<UpdateUserRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateCredentialsResult(CredentialUpdateError.InvalidUsername, Detail: "Username is too long."));

        var ctrl = CreateController(userProfile: mock.Object, userId: userId);

        var result = await ctrl.UpdateCredentials(
            new UpdateUserRequest(new string('u', 101), null, null, null), default);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, obj.StatusCode);
    }

    [Fact]
    public async Task UpdateCredentials_WeakPassword_Returns400()
    {
        var userId = Guid.NewGuid();
        var mock = new Mock<IUserProfileService>();
        mock.Setup(s => s.UpdateCredentialsAsync(userId, It.IsAny<UpdateUserRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateCredentialsResult(CredentialUpdateError.WeakPassword, Detail: "Password is too weak."));

        var ctrl = CreateController(userProfile: mock.Object, userId: userId);

        var result = await ctrl.UpdateCredentials(
            new UpdateUserRequest(null, null, "CurrentP@ss1", "short"), default);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, obj.StatusCode);
    }

    [Fact]
    public async Task UpdateCredentials_WrongCurrentPassword_Returns403()
    {
        var userId = Guid.NewGuid();
        var mock = new Mock<IUserProfileService>();
        mock.Setup(s => s.UpdateCredentialsAsync(userId, It.IsAny<UpdateUserRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateCredentialsResult(CredentialUpdateError.WrongCurrentPassword));

        var ctrl = CreateController(userProfile: mock.Object, userId: userId);

        var result = await ctrl.UpdateCredentials(
            new UpdateUserRequest(null, null, null, "NewP@ss123"), default);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, obj.StatusCode);
    }

    [Fact]
    public async Task UpdateCredentials_ValidPasswordChange_Returns200_AndRevokesSessions()
    {
        var userId = Guid.NewGuid();
        var user = CreateUser(userId);
        var mock = new Mock<IUserProfileService>();
        mock.Setup(s => s.UpdateCredentialsAsync(userId, It.IsAny<UpdateUserRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Success(user, passwordChanged: true));

        var refreshTokens = new Mock<IRefreshTokenService>();
        var ctrl = CreateController(userProfile: mock.Object, refreshTokens: refreshTokens.Object, userId: userId);

        var result = await ctrl.UpdateCredentials(
            new UpdateUserRequest(null, null, "CurrentP@ss1", "NewP@ss123"), default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<UpdateCredentialsResponse>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("testuser", resp.User!.Username);
        Assert.Equal("test@example.com", resp.User.Email);
        refreshTokens.Verify(s => s.RevokeAllUserTokensAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateCredentials_ValidUpdate_Returns200()
    {
        var userId = Guid.NewGuid();
        var user = CreateUser(userId, username: "updateduser", email: "new@example.com");
        var mock = new Mock<IUserProfileService>();
        mock.Setup(s => s.UpdateCredentialsAsync(userId, It.IsAny<UpdateUserRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Success(user));

        var ctrl = CreateController(userProfile: mock.Object, userId: userId);

        // Records compare by value: this verifies the controller forwards the
        // request to the service untouched (no normalization at the
        // controller layer — that is UserProfileService's job).
        var request = new UpdateUserRequest("updateduser", "new@example.com", null, null);
        var result = await ctrl.UpdateCredentials(request, default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<UpdateCredentialsResponse>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("updateduser", resp.User!.Username);
        Assert.Equal("new@example.com", resp.User.Email);
        mock.Verify(s => s.UpdateCredentialsAsync(userId, request, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateCredentials_NoPasswordChange_DoesNotRevokeSessions()
    {
        var userId = Guid.NewGuid();
        var user = CreateUser(userId, username: "updateduser");
        var mock = new Mock<IUserProfileService>();
        mock.Setup(s => s.UpdateCredentialsAsync(userId, It.IsAny<UpdateUserRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Success(user, passwordChanged: false));

        var refreshTokens = new Mock<IRefreshTokenService>();
        var ctrl = CreateController(userProfile: mock.Object, refreshTokens: refreshTokens.Object, userId: userId);

        var result = await ctrl.UpdateCredentials(
            new UpdateUserRequest("updateduser", null, null, null), default);

        Assert.IsType<OkObjectResult>(result);
        refreshTokens.Verify(s => s.RevokeAllUserTokensAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
