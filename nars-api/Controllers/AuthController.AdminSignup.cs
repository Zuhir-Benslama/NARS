using BCrypt.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Controllers;

/// <summary>
/// Handles account creation that originates from the public login page.
///
/// Creation hierarchy (all enforced here and in AdminController):
///   commune_user  → may create field_worker (inherits the creator's commune_id)
///   daira_admin   → may create commune_user (within their own daira only)
///   wilaya_admin  → may create daira_admin  (within their own wilaya only)
///   national_admin → may create wilaya_admin (any wilaya)
///   national_admin → NOT creatable via API; insert directly into the database.
///
/// POST /api/admin/authorized-signup
///   No session required — the authorizing admin's credentials are included
///   in the request body so this can be called from the unauthenticated login page.
/// </summary>
public partial class AuthController
{
    // ── POST /api/admin/authorized-signup ─────────────────────────────────────

    [HttpPost("admin/authorized-signup")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status423Locked)]
    public async Task<IActionResult> AuthorizedAdminSignup(
        [FromBody] AuthorizedAdminSignupRequest body,
        CancellationToken cancellationToken = default)
    {
        if (body is null)
        {
            return Problem(detail: "Request body is required.", statusCode: 400);
        }
        // 1. Verify the authorizing admin's credentials.
        // IMPORTANT: always run BCrypt.Verify even when the user is not found.
        // Short-circuiting on "admin is null" leaks whether a username exists
        // via response-time difference (~0 µs vs ~300 ms for a real BCrypt check).
        var adminNormalized = body.AdminUsername.ToLowerInvariant();
        var admin = await db.Users
            .FirstOrDefaultAsync(u => u.Username == adminNormalized, cancellationToken);

        var hashToCheck = admin?.PasswordHash ?? DummyHash;
        var passwordValid = BCrypt.Net.BCrypt.Verify(body.AdminPassword, hashToCheck);

        if (admin is null || !passwordValid)
        {
            return Problem(detail: "Admin credentials are invalid.", statusCode: 401);
        }

        if (!UserRoles.IsAdmin(admin.Role))
        {
            return Forbid();
        }

        // 2. Lockout check.
        if (admin.LockedUntil.HasValue && admin.LockedUntil > timeProvider.UtcNow)
        {
            return Problem(detail: "Admin account is temporarily locked.", statusCode: 423);
        }

        // 3. Role hierarchy.
        if (!authorizationService.CanCreateRole(admin.Role, body.Role))
        {
            return Problem(
                detail: $"A {admin.Role} cannot create a {body.Role} account.",
                statusCode: 403);
        }

        // 4. Geographic scope per role.
        var scopeResult = await authorizationService.ValidateCreateUserScopeAsync(
            admin.Role, admin.DairaId, admin.WilayaId,
            body.Role, body.CommuneId, body.DairaId, body.WilayaId,
            cancellationToken);
        if (scopeResult.Error is not null)
        {
            return Problem(detail: scopeResult.Error, statusCode: 403);
        }

        // 5. Geographic fields present.
        var geoError = GeographicValidator.Validate(body.Role, body.CommuneId, body.DairaId, body.WilayaId);
        if (geoError is not null)
        {
            return Problem(detail: geoError, statusCode: 400);
        }

        // 6. Uniqueness (normalised to lowercase for case-insensitive matching).
        var normalizedNewUsername = body.Username.ToLowerInvariant();
        var existing = await db.Users
            .FirstOrDefaultAsync(u => u.Username == normalizedNewUsername || u.Email == body.Email, cancellationToken);
        if (existing is not null)
        {
            var field = existing.Username == normalizedNewUsername ? "Username" : "Email";
            return Problem(detail: $"{field} already exists.", statusCode: 409);
        }

        // 7. Password strength.
        var pwdErr = PasswordValidator.Validate(body.Password);
        if (pwdErr is not null)
        {
            return Problem(detail: pwdErr, statusCode: 400);
        }

        // 8. Create.
        var newUser = new User
        {
            Name = body.Name,
            Email = body.Email,
            Phone = body.Phone,
            Username = normalizedNewUsername,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(body.Password),
            Role = body.Role,
            // commune_user and field_worker get a CommuneId; admins get their geographic anchor.
            CommuneId = body.Role is UserRoles.CommuneUser or UserRoles.FieldWorker ? body.CommuneId : null,
            DairaId = body.Role == UserRoles.DairaAdmin ? body.DairaId : null,
            WilayaId = body.Role == UserRoles.WilayaAdmin ? body.WilayaId : null,
            FailedLoginAttempts = 0,
        };

        db.Users.Add(newUser);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Duplicate user during authorized signup (username={Username}, email={Email})",
                body.Username, body.Email);
            return Problem(detail: "Username or email already exists.", statusCode: 409);
        }

        logger.LogInformation(
            "[Auth] {AdminUser} ({AdminRole}) created {NewRole} account {NewUser} via login page",
            admin.Username, admin.Role, newUser.Role, newUser.Username);

        return StatusCode(201, ApiResponse.Ok($"{body.Role} account created successfully."));
    }

}
