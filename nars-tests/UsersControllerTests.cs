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
            ctrl.UpdateCredentials(new UpdateUserRequest("newuser", null, null), default));
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
            new UpdateUserRequest("newuser", null, null), default);

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
            new UpdateUserRequest("takenuser", null, null), default);

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
            new UpdateUserRequest(null, "taken@example.com", null), default);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(409, obj.StatusCode);
    }

    [Fact]
    public async Task UpdateCredentials_InvalidPassword_Returns400()
    {
        var userId = Guid.NewGuid();
        var user = CreateUser(userId);
        var mock = new Mock<IUserProfileService>();
        mock.Setup(s => s.GetUserByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var ctrl = CreateController(userProfile: mock.Object, userId: userId);

        var result = await ctrl.UpdateCredentials(
            new UpdateUserRequest(null, null, "short"), default);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, obj.StatusCode);
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
            new UpdateUserRequest("updateduser", "new@example.com", null), default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<UpdateCredentialsResponse>(ok.Value);
        Assert.True(resp.Success);
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
            new UpdateUserRequest("sameuser", null, null), default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<UpdateCredentialsResponse>(ok.Value);
        Assert.True(resp.Success);
        mock.Verify(s => s.IsUsernameTakenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
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
            new UpdateUserRequest(null, "same@example.com", null), default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<UpdateCredentialsResponse>(ok.Value);
        Assert.True(resp.Success);
        mock.Verify(s => s.IsEmailTakenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
