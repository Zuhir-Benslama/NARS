using System.Data;
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
public class AdminController(
    AppDbContext db,
    ILogger<AdminController> logger,
    IAdminOverviewService overviewService) : NarsControllerBase
{
    [HttpGet("admin/overview")]
    [Authorize(Roles = "daira_admin,wilaya_admin,national_admin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Overview(CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FindAsync([CurrentUserId], cancellationToken);
        if (user is null) return Unauthorized(new { detail = "User not found." });

        return user.Role switch
        {
            UserRoles.DairaAdmin when user.DairaId is null =>
                Forbid("daira_id missing on account. Contact your administrator."),
            UserRoles.WilayaAdmin when user.WilayaId is null =>
                Forbid("wilaya_id missing on account. Contact your administrator."),
            UserRoles.DairaAdmin => await DairaOverview(user.DairaId!.Value, cancellationToken),
            UserRoles.WilayaAdmin => await WilayaOverview(user.WilayaId!.Value, cancellationToken),
            UserRoles.NationalAdmin => await NationalOverview(cancellationToken),
            _ => Forbid(),
        };
    }

    [HttpGet("admin/wilaya/{wilayaId:int}")]
    [Authorize(Roles = "daira_admin,wilaya_admin,national_admin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetWilaya(int wilayaId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FindAsync([CurrentUserId], cancellationToken);
        if (user is null) return Unauthorized(new { detail = "User not found." });
        if (user.Role != UserRoles.NationalAdmin) return Forbid();

        var result = await overviewService.GetWilayaReportAsync(wilayaId, cancellationToken);
        return result is null ? Problem(detail: "Wilaya not found.", statusCode: 404) : Ok(result);
    }

    [HttpGet("admin/daira/{dairaId:int}")]
    [Authorize(Roles = "daira_admin,wilaya_admin,national_admin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDaira(int dairaId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FindAsync([CurrentUserId], cancellationToken);
        if (user is null) return Unauthorized(new { detail = "User not found." });

        switch (user.Role)
        {
            case UserRoles.WilayaAdmin:
                {
                    var daira = await db.Dairas.FindAsync(dairaId, cancellationToken);
                    if (daira is null || daira.WilayaId != user.WilayaId)
                        return Forbid();
                    break;
                }
            case UserRoles.NationalAdmin:
                break;
            default:
                return Forbid();
        }

        var result = await overviewService.GetDairaReportAsync(dairaId, cancellationToken);
        return result is null ? Problem(detail: "Daira not found.", statusCode: 404) : Ok(result);
    }

    [HttpPost("admin/users")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAdmin([FromBody] CreateAdminRequest body, CancellationToken cancellationToken = default)
    {
        if (body is null) return Problem(detail: "Request body is required.", statusCode: 400);
        var creator = await db.Users.FindAsync([CurrentUserId], cancellationToken);
        if (creator is null) return Unauthorized();
        var callerRole = creator.Role;

        if (!CanCreateRole(callerRole, body.Role))
            return Forbid();

        switch (callerRole, body.Role)
        {
            case (UserRoles.CommuneUser, UserRoles.FieldWorker):
                break;
            case (UserRoles.DairaAdmin, UserRoles.CommuneUser):
                {
                    if (!body.CommuneId.HasValue)
                        return Problem(detail: "commune_id is required.", statusCode: 400);
                    var commune = await db.Communes.FindAsync(body.CommuneId.Value, cancellationToken);
                    if (commune is null || commune.DairaId != creator.DairaId)
                        return Forbid();
                    break;
                }
            case (UserRoles.WilayaAdmin, UserRoles.DairaAdmin):
                {
                    if (!body.DairaId.HasValue)
                        return Problem(detail: "daira_id is required.", statusCode: 400);
                    var daira = await db.Dairas.FindAsync(body.DairaId.Value, cancellationToken);
                    if (daira is null || daira.WilayaId != creator.WilayaId)
                        return Forbid();
                    break;
                }
            case (UserRoles.NationalAdmin, UserRoles.WilayaAdmin):
                if (!body.WilayaId.HasValue)
                    return Problem(detail: "wilaya_id is required.", statusCode: 400);
                break;
        }

        var geoError = ValidateGeographicFields(body.Role, body.CommuneId, body.DairaId, body.WilayaId);
        if (geoError is not null)
            return Problem(detail: geoError, statusCode: 400);

        var existing = await db.Users.FirstOrDefaultAsync(u =>
            u.Username == body.Username || u.Email == body.Email, cancellationToken);
        if (existing is not null)
        {
            var field = existing.Username == body.Username ? "Username" : "Email";
            return Conflict(new { detail = $"{field} already exists." });
        }

        var pwdErr = PasswordValidator.Validate(body.Password);
        if (pwdErr is not null)
            return Problem(detail: pwdErr, statusCode: 400);

        var communeId = body.Role == UserRoles.FieldWorker
            ? creator.CommuneId
            : body.CommuneId;

        var newUser = new User
        {
            Name = body.Name,
            Email = body.Email,
            Phone = body.Phone,
            Username = body.Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(body.Password),
            Role = body.Role,
            WilayaId = body.WilayaId,
            DairaId = body.DairaId,
            CommuneId = communeId,
        };

        db.Users.Add(newUser);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("[Admin] {CallerRole} {CallerId} created {Role} {UserId}",
            callerRole, CurrentUserId, body.Role, newUser.Id);

        return StatusCode(201, new CreateAdminResponse(
            Success: true,
            UserId: newUser.Id.ToString(),
            Message: $"{body.Role} account created successfully."
        ));
    }

    private static readonly System.Linq.Expressions.Expression<Func<User, AdminUserSummary>> ToAdminSummary =
        u => new AdminUserSummary(u.Id.ToString(), u.Username, u.Name, u.Email, u.Role, u.Phone ?? "", u.CommuneId, u.DairaId, u.WilayaId);

    [HttpGet("admin/users")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetManageableUsers(CancellationToken cancellationToken = default)
    {
        var creator = await db.Users.FindAsync([CurrentUserId], cancellationToken);
        if (creator is null) return Unauthorized();

        List<AdminUserSummary> users = creator.Role switch
        {
            UserRoles.NationalAdmin => await db.Users
                .Where(u => u.Role == UserRoles.WilayaAdmin)
                .Select(ToAdminSummary)
                .ToListAsync(cancellationToken),

            UserRoles.WilayaAdmin when creator.WilayaId.HasValue => await db.Users
                .Where(u => u.Role == UserRoles.DairaAdmin && u.DairaId.HasValue)
                .Join(db.Dairas.Where(d => d.WilayaId == creator.WilayaId.Value),
                    u => u.DairaId!.Value, d => d.DairaId, (u, _) => u)
                .Select(ToAdminSummary)
                .ToListAsync(cancellationToken),

            UserRoles.DairaAdmin when creator.DairaId.HasValue => await db.Users
                .Where(u => u.Role == UserRoles.CommuneUser && u.CommuneId.HasValue)
                .Join(db.Communes.Where(c => c.DairaId == creator.DairaId.Value),
                    u => u.CommuneId!.Value, c => c.CommuneId, (u, _) => u)
                .Select(ToAdminSummary)
                .ToListAsync(cancellationToken),

            UserRoles.CommuneUser when creator.CommuneId.HasValue => await db.Users
                .Where(u => u.Role == UserRoles.FieldWorker && u.CommuneId == creator.CommuneId)
                .Select(ToAdminSummary)
                .ToListAsync(cancellationToken),

            _ => [],
        };

        return Ok(users);
    }

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
        var creator = await db.Users.FindAsync([CurrentUserId], cancellationToken);
        if (creator is null) return Unauthorized();

        var target = await db.Users.FindAsync([userId], cancellationToken);
        if (target is null) return Problem(detail: "User not found.", statusCode: 404);

        if (!CanCreateRole(creator.Role, target.Role))
            return Forbid();

        if (body.Role is not null && !CanCreateRole(creator.Role, body.Role))
            return Forbid();

        if (body.Name is not null) target.Name = body.Name;
        if (body.Email is not null)
        {
            var emailConflict = await db.Users.AnyAsync(
                u => u.Email == body.Email && u.Id != userId, cancellationToken);
            if (emailConflict) return Conflict(new { detail = "Email already exists." });
            target.Email = body.Email;
        }
        if (body.Phone is not null) target.Phone = body.Phone;

        if (body.Role is not null)
        {
            var geoCheck = ValidateGeographicFields(body.Role, body.CommuneId, body.DairaId, body.WilayaId);
            if (geoCheck is not null) return Problem(detail: geoCheck, statusCode: 400);
            target.Role = body.Role;
        }

        if (body.WilayaId is not null) target.WilayaId = body.WilayaId;
        if (body.DairaId is not null) target.DairaId = body.DairaId;
        if (body.CommuneId is not null) target.CommuneId = body.CommuneId;

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("[Admin] {CallerRole} {CallerId} updated user {UserId}",
            creator.Role, CurrentUserId, userId);

        return Ok(new ActionResponse(Success: true));
    }

    [HttpDelete("admin/users/{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAdmin(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var creator = await db.Users.FindAsync([CurrentUserId], cancellationToken);
        if (creator is null) return Unauthorized();

        var target = await db.Users.FindAsync([userId], cancellationToken);
        if (target is null) return Problem(detail: "User not found.", statusCode: 404);

        if (!CanCreateRole(creator.Role, target.Role))
            return Forbid();

        db.Users.Remove(target);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("[Admin] {CallerRole} {CallerId} deleted user {UserId} ({Username})",
            creator.Role, CurrentUserId, userId, target.Username);

        return Ok(new ActionResponse(Success: true));
    }

    // ── Permission helpers ──────────────────────────────────────────────────

    internal static bool CanCreateRole(string callerRole, string targetRole) => (callerRole, targetRole) switch
    {
        (UserRoles.NationalAdmin, UserRoles.WilayaAdmin) => true,
        (UserRoles.WilayaAdmin, UserRoles.DairaAdmin) => true,
        (UserRoles.DairaAdmin, UserRoles.CommuneUser) => true,
        (UserRoles.CommuneUser, UserRoles.FieldWorker) => true,
        _ => false,
    };

    private async Task<IActionResult> NationalOverview(CancellationToken cancellationToken)
    {
        var wilayas = await overviewService.GetNationalOverviewAsync(cancellationToken);
        return Ok(new { level = "national", wilayas });
    }

    private async Task<IActionResult> WilayaOverview(int wilayaId, CancellationToken cancellationToken)
    {
        var report = await overviewService.GetWilayaReportAsync(wilayaId, cancellationToken);
        return report is null ? Problem(detail: "Wilaya not found.", statusCode: 404) : Ok(report);
    }

    private async Task<IActionResult> DairaOverview(int dairaId, CancellationToken cancellationToken)
    {
        var report = await overviewService.GetDairaReportAsync(dairaId, cancellationToken);
        return Ok(report ?? new DairaReport(dairaId, "", "", null, Array.Empty<CommuneReport>()));
    }
}
