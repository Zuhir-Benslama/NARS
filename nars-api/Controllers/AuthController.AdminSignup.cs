using System.Data;
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
        var admin = await db.Users
            .FirstOrDefaultAsync(u => u.Username == body.AdminUsername, cancellationToken);

        // Use a stable dummy hash so BCrypt always does the full work.
        const string _dummyHash = "$2a$11$dummy.constant.time.hash.padding.abcdefghijklmnop";
        var hashToCheck = admin?.PasswordHash ?? _dummyHash;
        var passwordValid = BCrypt.Net.BCrypt.Verify(body.AdminPassword, hashToCheck);

        if (admin is null || !passwordValid)
        {
            return Unauthorized(new { detail = "Admin credentials are invalid." });
        }

        if (!UserRoles.IsAdmin(admin.Role))
        {
            return Forbid();
        }

        // 2. Lockout check.
        if (admin.LockedUntil.HasValue && admin.LockedUntil > timeProvider.UtcNow)
        {
            return StatusCode(423, new { detail = "Admin account is temporarily locked." });
        }

        // 3. Role hierarchy.
        if (!AdminController.CanCreateRole(admin.Role, body.Role))
        {
            return StatusCode(403, new
            {
                detail = $"A {admin.Role} cannot create a {body.Role} account."
            });
        }

        // 4. Geographic scope per role.
        var scopeError = await ValidateScopeAsync(admin, body, cancellationToken);
        if (scopeError is not null)
        {
            return StatusCode(403, new { detail = scopeError });
        }

        // 5. Geographic fields present.
        var geoError = ValidateGeographicFields(body.Role, body.CommuneId, body.DairaId, body.WilayaId);
        if (geoError is not null)
        {
            return Problem(detail: geoError, statusCode: 400);
        }

        // 6. Uniqueness.
        var existing = await db.Users
            .FirstOrDefaultAsync(u => u.Username == body.Username || u.Email == body.Email, cancellationToken);
        if (existing is not null)
        {
            var field = existing.Username == body.Username ? "Username" : "Email";
            return Conflict(new { detail = $"{field} already exists." });
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
            Username = body.Username,
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
        catch (DbUpdateException)
        {
            var field = await db.Users.AnyAsync(u => u.Username == body.Username, cancellationToken)
                ? "Username"
                : "Email";
            return Conflict(new { detail = $"{field} already exists." });
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "[Auth] {AdminUser} ({AdminRole}) created {NewRole} account {NewUser} via login page",
                admin.Username, admin.Role, newUser.Role, newUser.Username);
        }

        return StatusCode(201, new ActionResponse(
            Success: true,
            Message: $"{body.Role} account created successfully."
        ));
    }

    // ─── Scope validation ─────────────────────────────────────────────────────

    private async Task<string?> ValidateScopeAsync(User admin, AuthorizedAdminSignupRequest body, CancellationToken cancellationToken = default)
    {
        switch (admin.Role, body.Role)
        {
            // commune_user creates field_worker: inherits creator's commune_id, no extra scope needed
            case (UserRoles.CommuneUser, UserRoles.FieldWorker):
                return null;
            // daira_admin creates commune_user: commune must belong to admin's daira.
            case (UserRoles.DairaAdmin, UserRoles.CommuneUser):
                {
                    if (!body.CommuneId.HasValue)
                    {
                        return "commune_id is required when creating a commune_user.";
                    }

                    var commune = await db.Communes.FindAsync([body.CommuneId.Value], cancellationToken);
                    if (commune is null)
                    {
                        return "Commune not found.";
                    }

                    if (commune.DairaId != admin.DairaId)
                    {
                        return "That commune does not belong to your daira.";
                    }

                    return null;
                }
            // wilaya_admin creates daira_admin: daira must belong to admin's wilaya.
            case (UserRoles.WilayaAdmin, UserRoles.DairaAdmin):
                {
                    if (!body.DairaId.HasValue)
                    {
                        return "daira_id is required when creating a daira_admin.";
                    }

                    var daira = await db.Dairas.FindAsync([body.DairaId.Value], cancellationToken);
                    if (daira is null)
                    {
                        return "Daira not found.";
                    }

                    if (daira.WilayaId != admin.WilayaId)
                    {
                        return "That daira does not belong to your wilaya.";
                    }

                    return null;
                }
            // national_admin creates wilaya_admin: any wilaya is valid.
            case (UserRoles.NationalAdmin, UserRoles.WilayaAdmin):
                if (body.WilayaId.HasValue && await db.Wilayas.FindAsync([body.WilayaId.Value], cancellationToken) is null)
                {
                    return "Wilaya not found.";
                }

                return null;

            default:
                return null;
        }
    }

}
