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
        bool authenticated = true,
        Guid? userId = null)
    {
        var ctrl = new UsersController(
            userProfile ?? Mock.Of<IUserProfileService>(),
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
            PasswordHash = "old-hash",
            Role = UserRoles.CommuneUser,
            CommuneId = CommuneId1,
        };

    // ── UpdateCredentials ───────────────────────────────────────────────

    [Fact]
    public async Task UpdateCredentials_NoAuth_ThrowsUnauthorized()
    {
        var ctrl = CreateController(authenticated: false);

        // [Authorize] on NarsControllerBase returns 401 via middleware in production.
        // Unit tests bypass the middleware pipeline, so RequiredCurrentUserId throws.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            ctrl.UpdateCredentials(new UpdateUserRequest("newuser", null, null, null), default));
    }

    [Fact]
    public async Task UpdateCredentials_UserNotFound_Returns404()
    {
        var userId = Guid.NewGuid();
        var mock = new Mock<IUserProfileService>();
        mock.Setup(s => s.GetUserByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

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
        var user = CreateUser(userId, username: "currentuser");
        var mock = new Mock<IUserProfileService>();
        mock.Setup(s => s.GetUserByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        mock.Setup(s => s.IsUsernameTakenAsync("takenuser", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

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
        var user = CreateUser(userId);
        var mock = new Mock<IUserProfileService>();
        mock.Setup(s => s.GetUserByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        mock.Setup(s => s.IsEmailTakenAsync("taken@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var ctrl = CreateController(userProfile: mock.Object, userId: userId);

        var result = await ctrl.UpdateCredentials(
            new UpdateUserRequest(null, "taken@example.com", null, null), default);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(409, obj.StatusCode);
    }

    [Fact]
    public async Task UpdateCredentials_InvalidPassword_Returns400()
    {
        var userId = Guid.NewGuid();
        var user = CreateUser(userId);
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword("CurrentP@ss1");
        var mock = new Mock<IUserProfileService>();
        mock.Setup(s => s.GetUserByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var ctrl = CreateController(userProfile: mock.Object, userId: userId);

        var result = await ctrl.UpdateCredentials(
            new UpdateUserRequest(null, null, "CurrentP@ss1", "short"), default);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, obj.StatusCode);
    }

    [Fact]
    public async Task UpdateCredentials_NewPassword_RequiresCurrentPassword()
    {
        var userId = Guid.NewGuid();
        var user = CreateUser(userId);
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword("CurrentP@ss1");
        var mock = new Mock<IUserProfileService>();
        mock.Setup(s => s.GetUserByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var ctrl = CreateController(userProfile: mock.Object, userId: userId);

        var result = await ctrl.UpdateCredentials(
            new UpdateUserRequest(null, null, null, "NewP@ss123"), default);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, obj.StatusCode);
        mock.Verify(s => s.UpdateUserAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateCredentials_WrongCurrentPassword_Returns403()
    {
        var userId = Guid.NewGuid();
        var user = CreateUser(userId);
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword("CurrentP@ss1");
        var mock = new Mock<IUserProfileService>();
        mock.Setup(s => s.GetUserByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var ctrl = CreateController(userProfile: mock.Object, userId: userId);

        var result = await ctrl.UpdateCredentials(
            new UpdateUserRequest(null, null, "WrongP@ss", "NewP@ss123"), default);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, obj.StatusCode);
        mock.Verify(s => s.UpdateUserAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateCredentials_ValidPasswordChange_Returns200()
    {
        var userId = Guid.NewGuid();
        var user = CreateUser(userId);
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword("CurrentP@ss1");
        var mock = new Mock<IUserProfileService>();
        mock.Setup(s => s.GetUserByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var ctrl = CreateController(userProfile: mock.Object, userId: userId);

        var result = await ctrl.UpdateCredentials(
            new UpdateUserRequest(null, null, "CurrentP@ss1", "NewP@ss123"), default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<UpdateCredentialsResponse>(ok.Value);
        Assert.True(resp.Success);
        Assert.True(BCrypt.Net.BCrypt.Verify("NewP@ss123", user.PasswordHash));
        mock.Verify(s => s.UpdateUserAsync(user, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateCredentials_ValidUpdate_Returns200()
    {
        var userId = Guid.NewGuid();
        var user = CreateUser(userId);
        var mock = new Mock<IUserProfileService>();
        mock.Setup(s => s.GetUserByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var ctrl = CreateController(userProfile: mock.Object, userId: userId);

        var result = await ctrl.UpdateCredentials(
            new UpdateUserRequest("updateduser", "new@example.com", null, null), default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<UpdateCredentialsResponse>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("updateduser", resp.User!.Username);
        Assert.Equal("new@example.com", resp.User.Email);
        mock.Verify(s => s.GetUserByIdAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
        mock.Verify(s => s.IsUsernameTakenAsync("updateduser", It.IsAny<CancellationToken>()), Times.Once);
        mock.Verify(s => s.IsEmailTakenAsync("new@example.com", It.IsAny<CancellationToken>()), Times.Once);
        mock.Verify(s => s.UpdateUserAsync(user, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateCredentials_SameUsernameNoConflict()
    {
        var userId = Guid.NewGuid();
        var user = CreateUser(userId, username: "sameuser");
        var mock = new Mock<IUserProfileService>();
        mock.Setup(s => s.GetUserByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var ctrl = CreateController(userProfile: mock.Object, userId: userId);

        var result = await ctrl.UpdateCredentials(
            new UpdateUserRequest("sameuser", null, null, null), default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<UpdateCredentialsResponse>(ok.Value);
        Assert.True(resp.Success);
        mock.Verify(s => s.UpdateUserAsync(user, It.IsAny<CancellationToken>()), Times.Once);
        mock.Verify(s => s.IsUsernameTakenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateCredentials_EmailNormalizedToLowercase()
    {
        var userId = Guid.NewGuid();
        var user = CreateUser(userId);
        var mock = new Mock<IUserProfileService>();
        mock.Setup(s => s.GetUserByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var ctrl = CreateController(userProfile: mock.Object, userId: userId);

        var result = await ctrl.UpdateCredentials(
            new UpdateUserRequest(null, "  MiXeD@Example.COM ", null, null), default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<UpdateCredentialsResponse>(ok.Value);
        Assert.Equal("mixed@example.com", resp.User!.Email);
        mock.Verify(s => s.IsEmailTakenAsync("mixed@example.com", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateCredentials_SameEmailNoConflict()
    {
        var userId = Guid.NewGuid();
        var user = CreateUser(userId, email: "same@example.com");
        var mock = new Mock<IUserProfileService>();
        mock.Setup(s => s.GetUserByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var ctrl = CreateController(userProfile: mock.Object, userId: userId);

        var result = await ctrl.UpdateCredentials(
            new UpdateUserRequest(null, "same@example.com", null, null), default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<UpdateCredentialsResponse>(ok.Value);
        Assert.True(resp.Success);
        mock.Verify(s => s.UpdateUserAsync(user, It.IsAny<CancellationToken>()), Times.Once);
        mock.Verify(s => s.IsEmailTakenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
