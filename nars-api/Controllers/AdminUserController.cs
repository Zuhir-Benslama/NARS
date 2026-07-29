using BCrypt.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;

namespace NarsApi.Controllers;

[ApiController]
[Route("/api")]
[Tags("Admin")]
public class AdminUserController(
    ILogger<AdminUserController> logger,
    IUserAuthorizationService authorizationService,
    IUserCreationService userCreationService,
    IWebHostEnvironment webHost) : NarsControllerBase(webHost)
{
    /// <summary>Creates a lower-tier admin account (e.g. commune_user, daira_admin).</summary>
    [HttpPost("admin/users")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAdmin([FromBody] CreateAdminRequest body, CancellationToken cancellationToken = default)
    {
        if (body is null)
        {
            return Problem(detail: "Request body is required.", statusCode: 400);
        }

        var callerRole = CurrentUserRole;

        if (!authorizationService.CanCreateRole(callerRole, body.Role))
        {
            return Forbid();
        }

        var scopeResult = await authorizationService.ValidateCreateUserScopeAsync(
            callerRole, CurrentDairaId, CurrentWilayaId,
            body.Role, body.CommuneId, body.DairaId, body.WilayaId,
            cancellationToken);
        if (scopeResult.Error is not null)
        {
            return scopeResult.IsAuthorizationFailure
                ? Forbid()
                : Problem(detail: scopeResult.Error, statusCode: 400);
        }

        // Resolve commune_id: field_workers inherit the caller's commune.
        var communeId = body.Role == UserRoles.FieldWorker
            ? CurrentCommuneId
            : body.CommuneId;

        // Validate and create user (geographic, uniqueness, password strength).
        var (newUser, error) = await userCreationService.ValidateAndCreateUserAsync(
            body.Name, body.Email, body.Phone, body.Username, body.Password,
            body.Role, communeId, body.DairaId, body.WilayaId,
            cancellationToken);
        if (error is not null || newUser is null)
        {
            var statusCode = error?.Contains("already exists") == true ? 409 : 400;
            return Problem(detail: error ?? "User creation failed.", statusCode: statusCode);
        }

        try
        {
            await userCreationService.SaveUserAsync(newUser, cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Duplicate user during admin signup (username={Username}, email={Email})",
                body.Username, body.Email);
            return Problem(detail: "Username or email already exists.", statusCode: 409);
        }

        logger.LogInformation("[Admin] {CallerRole} {CallerId} created {Role} {UserId}",
            callerRole, CurrentUserId, body.Role, newUser.Id);

        return StatusCode(201, new CreateAdminResponse(
            Success: true,
            UserId: newUser.Id.ToString(),
            Message: $"{body.Role} account created successfully."
        ));
    }

    /// <summary>Lists users that the caller has authority to manage.</summary>
    [HttpGet("admin/users")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetManageableUsers(
        [FromQuery] int skip = 0, [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 500);
        var users = await authorizationService.GetManageableUsersAsync(
            CurrentUserRole, RequiredCurrentUserId, CurrentCommuneId, CurrentDairaId, CurrentWilayaId,
            skip, take,
            cancellationToken);

        return Ok(users);
    }

    /// <summary>Updates a managed user's profile, role, or geographic scope.</summary>
    [HttpPut("admin/users/{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAdmin(
        Guid userId, [FromBody] UpdateAdminRequest body,
        CancellationToken cancellationToken = default)
    {
        if (body is null)
        {
            return Problem(detail: "Request body is required.", statusCode: 400);
        }

        var target = await authorizationService.FindUserByIdAsync(userId, cancellationToken);
        if (target is null)
        {
            return Problem(detail: "User not found.", statusCode: 404);
        }

        var authError = await ValidateAdminUpdatePermissionAsync(target, body, cancellationToken);
        if (authError is not null)
        {
            return authError;
        }

        var fieldError = await ApplyUpdateFieldsAsync(target, body, cancellationToken);
        if (fieldError is not null)
        {
            return fieldError;
        }

        var geoError = ApplyRoleAndGeography(target, body);
        if (geoError is not null)
        {
            return geoError;
        }

        await authorizationService.SaveChangesAsync(cancellationToken);

        logger.LogInformation("[Admin] {CallerRole} {CallerId} updated user {UserId}",
            CurrentUserRole, CurrentUserId, userId);

        return Ok(ApiResponse.Ok());
    }

    /// <summary>Deletes a managed user account.</summary>
    [HttpDelete("admin/users/{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAdmin(
        Guid userId, CancellationToken cancellationToken = default)
    {
        if (userId == RequiredCurrentUserId)
        {
            return Problem(detail: "Cannot delete your own account.", statusCode: 400);
        }

        var target = await authorizationService.FindUserByIdAsync(userId, cancellationToken);
        if (target is null)
        {
            return Problem(detail: "User not found.", statusCode: 404);
        }

        if (!authorizationService.CanCreateRole(CurrentUserRole, target.Role))
        {
            return Forbid();
        }

        await authorizationService.DeleteUserAsync(userId, cancellationToken);

        logger.LogInformation("[Admin] {CallerRole} {CallerId} deleted user {UserId} ({Username})",
            CurrentUserRole, CurrentUserId, userId, target.Username);

        return NoContent();
    }

    private async Task<IActionResult?> ValidateAdminUpdatePermissionAsync(
        User target, UpdateAdminRequest body, CancellationToken ct)
    {
        if (!authorizationService.CanCreateRole(CurrentUserRole, target.Role))
        {
            return Forbid();
        }

        if (body.Role is not null && !authorizationService.CanCreateRole(CurrentUserRole, body.Role))
        {
            return Forbid();
        }

        var sensitiveChange = body.Role is not null
            || body.WilayaId is not null
            || body.DairaId is not null
            || body.CommuneId is not null;
        if (sensitiveChange)
        {
            if (string.IsNullOrEmpty(body.Password))
            {
                return Problem(detail: "Password is required to change role or geographic scope.", statusCode: 400);
            }

            var caller = await authorizationService.FindUserByIdAsync(RequiredCurrentUserId, ct);
            if (caller is null || !BCrypt.Net.BCrypt.Verify(body.Password, caller.PasswordHash))
            {
                return Problem(detail: "Password is incorrect.", statusCode: 403);
            }
        }

        return null;
    }

    private async Task<IActionResult?> ApplyUpdateFieldsAsync(User target, UpdateAdminRequest body, CancellationToken ct)
    {
        if (body.Name is not null)
        {
            target.Name = body.Name;
        }

        if (body.Email is not null)
        {
            var emailConflict = await authorizationService.IsEmailTakenAsync(body.Email, target.Id, ct);
            if (emailConflict)
            {
                return Problem(detail: "Email already exists.", statusCode: 409);
            }

            target.Email = body.Email;
        }

        if (body.Phone is not null)
        {
            target.Phone = body.Phone;
        }

        return null;
    }

    private ObjectResult? ApplyRoleAndGeography(User target, UpdateAdminRequest body)
    {
        if (body.Role is not null)
        {
            var geoCheck = GeographicValidator.Validate(body.Role, body.CommuneId, body.DairaId, body.WilayaId);
            if (geoCheck is not null)
            {
                return Problem(detail: geoCheck, statusCode: 400);
            }

            target.Role = body.Role;
        }

        if (body.WilayaId is not null)
        {
            target.WilayaId = body.WilayaId;
        }

        if (body.DairaId is not null)
        {
            target.DairaId = body.DairaId;
        }

        if (body.CommuneId is not null)
        {
            target.CommuneId = body.CommuneId;
        }

        return null;
    }
}
