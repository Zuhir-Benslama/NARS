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
    AppDbContext db,
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
        if (body is null) return Problem(detail: "Request body is required.", statusCode: 400);

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
        if (error is not null)
        {
            var statusCode = error.Contains("already exists") ? 409 : 400;
            return Problem(detail: error, statusCode: statusCode);
        }

        db.Users.Add(newUser!);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Duplicate user during admin signup (username={Username}, email={Email})",
                body.Username, body.Email);
            return Problem(detail: "Username or email already exists.", statusCode: 409);
        }

        logger.LogInformation("[Admin] {CallerRole} {CallerId} created {Role} {UserId}",
            callerRole, CurrentUserId, body.Role, newUser!.Id);

        return StatusCode(201, new CreateAdminResponse(
            Success: true,
            UserId: newUser.Id.ToString(),
            Message: $"{body.Role} account created successfully."
        ));
    }

    private static readonly System.Linq.Expressions.Expression<Func<User, AdminUserSummary>> ToAdminSummary =
        u => new AdminUserSummary(u.Id.ToString(), u.Username, u.Name, u.Email, u.Role, u.Phone ?? "", u.CommuneId, u.DairaId, u.WilayaId);

    /// <summary>Lists users that the caller has authority to manage.</summary>
    [HttpGet("admin/users")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetManageableUsers(CancellationToken cancellationToken = default)
    {
        List<AdminUserSummary> users = CurrentUserRole switch
        {
            UserRoles.NationalAdmin => await db.Users
                .Where(u => u.Role == UserRoles.WilayaAdmin)
                .Select(ToAdminSummary)
                .ToListAsync(cancellationToken),

            UserRoles.WilayaAdmin when CurrentWilayaId.HasValue => await db.Users
                .Where(u => u.Role == UserRoles.DairaAdmin && u.DairaId.HasValue)
                .Join(db.Dairas.Where(d => d.WilayaId == CurrentWilayaId.Value),
                    u => u.DairaId!.Value, d => d.DairaId, (u, _) => u)
                .Select(ToAdminSummary)
                .ToListAsync(cancellationToken),

            UserRoles.DairaAdmin when CurrentDairaId.HasValue => await db.Users
                .Where(u => u.Role == UserRoles.CommuneUser && u.CommuneId.HasValue)
                .Join(db.Communes.Where(c => c.DairaId == CurrentDairaId.Value),
                    u => u.CommuneId!.Value, c => c.CommuneId, (u, _) => u)
                .Select(ToAdminSummary)
                .ToListAsync(cancellationToken),

            UserRoles.CommuneUser when CurrentCommuneId.HasValue => await db.Users
                .Where(u => u.Role == UserRoles.FieldWorker && u.CommuneId == CurrentCommuneId)
                .Select(ToAdminSummary)
                .ToListAsync(cancellationToken),

            _ => [],
        };

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
        if (body is null) return Problem(detail: "Request body is required.", statusCode: 400);

        var target = await db.Users.FindAsync([userId], cancellationToken);
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

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("[Admin] {CallerRole} {CallerId} updated user {UserId}",
            CurrentUserRole, CurrentUserId, userId);

        return Ok(ApiResponse.Ok());
    }

    /// <summary>Deletes a managed user account.</summary>
    [HttpDelete("admin/users/{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAdmin(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var target = await db.Users.FindAsync([userId], cancellationToken);
        if (target is null)
        {
            return Problem(detail: "User not found.", statusCode: 404);
        }

        if (!authorizationService.CanCreateRole(CurrentUserRole, target.Role))
        {
            return Forbid();
        }

        db.Users.Remove(target);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("[Admin] {CallerRole} {CallerId} deleted user {UserId} ({Username})",
            CurrentUserRole, CurrentUserId, userId, target.Username);

        return Ok(ApiResponse.Ok());
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

            var caller = await db.Users.FindAsync([RequiredCurrentUserId], ct);
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
            var emailConflict = await db.Users.AnyAsync(
                u => u.Email == body.Email && u.Id != target.Id, ct);
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

    private IActionResult? ApplyRoleAndGeography(User target, UpdateAdminRequest body)
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
