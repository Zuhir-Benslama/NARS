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
    IFeatureStatsService featureStatsService) : NarsControllerBase
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

        var result = await BuildWilayaReportAsync(wilayaId, cancellationToken);
        return result is null ? NotFound(new { detail = "Wilaya not found." }) : Ok(result);
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

        var result = await BuildDairaReportAsync(dairaId, cancellationToken);
        return result is null ? NotFound(new { detail = "Daira not found." }) : Ok(result);
    }

    [HttpPost("admin/users")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAdmin([FromBody] CreateAdminRequest body, CancellationToken cancellationToken = default)
    {
        if (body is null) return BadRequest(new { detail = "Request body is required." });
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
                        return BadRequest(new { detail = "commune_id is required." });
                    var commune = await db.Communes.FindAsync(body.CommuneId.Value, cancellationToken);
                    if (commune is null || commune.DairaId != creator.DairaId)
                        return Forbid();
                    break;
                }
            case (UserRoles.WilayaAdmin, UserRoles.DairaAdmin):
                {
                    if (!body.DairaId.HasValue)
                        return BadRequest(new { detail = "daira_id is required." });
                    var daira = await db.Dairas.FindAsync(body.DairaId.Value, cancellationToken);
                    if (daira is null || daira.WilayaId != creator.WilayaId)
                        return Forbid();
                    break;
                }
            case (UserRoles.NationalAdmin, UserRoles.WilayaAdmin):
                if (!body.WilayaId.HasValue)
                    return BadRequest(new { detail = "wilaya_id is required." });
                break;
        }

        var geoError = ValidateGeographicFields(body.Role, body.CommuneId, body.DairaId, body.WilayaId);
        if (geoError is not null)
            return BadRequest(new { detail = geoError });

        var existing = await db.Users.FirstOrDefaultAsync(u =>
            u.Username == body.Username || u.Email == body.Email, cancellationToken);
        if (existing is not null)
        {
            var field = existing.Username == body.Username ? "Username" : "Email";
            return Conflict(new { detail = $"{field} already exists." });
        }

        var pwdErr = PasswordValidator.Validate(body.Password);
        if (pwdErr is not null)
            return BadRequest(new { detail = pwdErr });

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

        return StatusCode(201, new { success = true, id = newUser.Id.ToString() });
    }

    // ── Permission helpers ──────────────────────────────────────────────────

    private static bool CanCreateRole(string callerRole, string targetRole) => (callerRole, targetRole) switch
    {
        (UserRoles.NationalAdmin, UserRoles.WilayaAdmin) => true,
        (UserRoles.WilayaAdmin, UserRoles.DairaAdmin) => true,
        (UserRoles.DairaAdmin, UserRoles.CommuneUser) => true,
        (UserRoles.CommuneUser, UserRoles.FieldWorker) => true,
        _ => false,
    };

    // ── Overview builders ──────────────────────────────────────────────────

    private async Task<IActionResult> NationalOverview(CancellationToken cancellationToken)
    {
        var wilayas = await db.Wilayas.OrderBy(w => w.WilayaId).ToListAsync(cancellationToken);
        var wilayaIds = wilayas.Select(w => w.WilayaId).ToArray();

        var admins = (await db.Users
                .Where(u => u.Role == UserRoles.WilayaAdmin && u.WilayaId.HasValue && wilayaIds.Contains(u.WilayaId.Value))
                .ToListAsync(cancellationToken))
            .GroupBy(u => u.WilayaId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var dairasByWilaya = (await db.Dairas
                .Where(d => wilayaIds.Contains(d.WilayaId))
                .ToListAsync(cancellationToken))
            .GroupBy(d => d.WilayaId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var allDairaIds = dairasByWilaya.Values.SelectMany(d => d).Select(d => d.DairaId).ToArray();

        var communesByDaira = (await db.Communes
                .Where(c => allDairaIds.Contains(c.DairaId))
                .ToListAsync(cancellationToken))
            .GroupBy(c => c.DairaId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var allCommuneIds = communesByDaira.Values.SelectMany(c => c).Select(c => c.CommuneId).ToArray();

        var communeUserCounts = await db.Users
            .Where(u => u.Role == UserRoles.CommuneUser && u.CommuneId.HasValue && allCommuneIds.Contains(u.CommuneId.Value))
            .GroupBy(u => u.CommuneId!.Value)
            .Select(g => new { CommuneId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var userCountByCommune = communeUserCounts.ToDictionary(x => x.CommuneId, x => x.Count);

        var results = wilayas.Select(wilaya =>
        {
            var admin = admins.GetValueOrDefault(wilaya.WilayaId);
            var dairas = dairasByWilaya.GetValueOrDefault(wilaya.WilayaId) ?? [];
            var communeIds = dairas.SelectMany(d =>
                communesByDaira.GetValueOrDefault(d.DairaId) ?? []
            ).Select(c => c.CommuneId).ToArray();

            return new WilayaSummary(
                WilayaId: wilaya.WilayaId,
                WilayaNameFr: wilaya.WilayaFr ?? "",
                WilayaNameAr: wilaya.WilayaAr ?? "",
                WilayaAdmin: admin is null ? null : new AdminInfo(
                    admin.Id.ToString(), admin.Username, admin.Name, admin.Email, admin.Role),
                DairaCount: dairas.Count,
                CommuneCount: communeIds.Length,
                CommuneUserCount: communeIds.Sum(cid => userCountByCommune.GetValueOrDefault(cid))
            );
        }).ToList();

        return Ok(new { level = "national", wilayas = results });
    }

    private async Task<IActionResult> WilayaOverview(int wilayaId, CancellationToken cancellationToken)
    {
        var report = await BuildWilayaReportAsync(wilayaId, cancellationToken);
        return report is null ? NotFound(new { detail = "Wilaya not found." }) : Ok(report);
    }

    private async Task<IActionResult> DairaOverview(int dairaId, CancellationToken cancellationToken)
    {
        var report = await BuildDairaReportAsync(dairaId, cancellationToken);
        return Ok(report ?? new DairaReport(dairaId, "", "", null, Array.Empty<CommuneReport>()));
    }

    // ── Report builders ────────────────────────────────────────────────────

    private async Task<DairaReport?> BuildDairaReportAsync(int dairaId, CancellationToken cancellationToken)
    {
        var daira = await db.Dairas.FindAsync([dairaId], cancellationToken);
        if (daira is null) return null;

        var admin = await db.Users.FirstOrDefaultAsync(u =>
            u.Role == UserRoles.DairaAdmin && u.DairaId == dairaId, cancellationToken);

        var communesByDaira = await BuildCommunesForDairasAsync([dairaId], cancellationToken);
        var communes = communesByDaira.GetValueOrDefault(dairaId) ?? [];

        return new DairaReport(
            DairaId: dairaId,
            DairaNameFr: daira.DairaFr,
            DairaNameAr: daira.DairaAr,
            DairaAdmin: admin is null ? null : new AdminInfo(
                admin.Id.ToString(), admin.Username, admin.Name, admin.Email, admin.Role),
            Communes: communes
        );
    }

    private async Task<WilayaReport?> BuildWilayaReportAsync(int wilayaId, CancellationToken cancellationToken)
    {
        var wilaya = await db.Wilayas.FindAsync([wilayaId], cancellationToken);
        if (wilaya is null) return null;

        var admin = await db.Users.FirstOrDefaultAsync(u =>
            u.Role == UserRoles.WilayaAdmin && u.WilayaId == wilayaId, cancellationToken);

        var dairas = await db.Dairas.Where(d => d.WilayaId == wilayaId)
            .OrderBy(d => d.DairaFr).ToListAsync(cancellationToken);

        var dairaIds = dairas.Select(d => d.DairaId).ToArray();

        var dairaAdmins = (await db.Users
                .Where(u => u.Role == UserRoles.DairaAdmin && u.DairaId.HasValue && dairaIds.Contains(u.DairaId.Value))
                .ToListAsync(cancellationToken))
            .GroupBy(u => u.DairaId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var communeReports = await BuildCommunesForDairasAsync(dairaIds, cancellationToken);

        var dairaReports = dairas.Select(daira =>
        {
            var da = dairaAdmins.GetValueOrDefault(daira.DairaId);

            return new DairaReport(
                DairaId: daira.DairaId,
                DairaNameFr: daira.DairaFr ?? "",
                DairaNameAr: daira.DairaAr ?? "",
                DairaAdmin: da is null ? null : new AdminInfo(
                    da.Id.ToString(), da.Username, da.Name, da.Email, da.Role),
                Communes: communeReports.GetValueOrDefault(daira.DairaId) ?? []
            );
        }).ToList();

        return new WilayaReport(
            WilayaId: wilayaId,
            WilayaNameFr: wilaya.WilayaFr ?? "",
            WilayaNameAr: wilaya.WilayaAr ?? "",
            WilayaAdmin: admin is null ? null : new AdminInfo(
                admin.Id.ToString(), admin.Username, admin.Name, admin.Email, admin.Role),
            Dairas: dairaReports
        );
    }

    private async Task<Dictionary<int, List<CommuneReport>>> BuildCommunesForDairasAsync(int[] dairaIds, CancellationToken cancellationToken)
    {
        var communes = await db.Communes.Where(c => dairaIds.Contains(c.DairaId))
            .OrderBy(c => c.CommuneFr).ToListAsync(cancellationToken);

        var communeIds = communes.Select(c => c.CommuneId).ToArray();

        var users = await db.Users
            .Where(u => u.CommuneId.HasValue && communeIds.Contains(u.CommuneId.Value))
            .Where(u => u.Role == UserRoles.CommuneUser)
            .Select(u => new { u.Id, u.Username, u.Name, u.Email, u.Role, u.CommuneId })
            .ToListAsync(cancellationToken);

        var userGroups = users.GroupBy(u => u.CommuneId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var allUserIds = users.Select(u => u.Id).ToArray();
        var featureCounts = allUserIds.Length > 0
            ? await featureStatsService.GetUserFeatureCountsAsync(allUserIds, cancellationToken)
            : new Dictionary<Guid, UserFeatureStats>();

        var result = new Dictionary<int, List<CommuneReport>>();
        foreach (var communeGroup in communes.GroupBy(c => c.DairaId))
        {
            var reports = communeGroup.Select(commune =>
            {
                var communeUsers = userGroups.GetValueOrDefault(commune.CommuneId);
                var userStats = (communeUsers ?? []).Select(u =>
                {
                    featureCounts.TryGetValue(u.Id, out var stats);
                    return stats ?? new UserFeatureStats(
                        u.Id.ToString(), u.Username, u.Name, u.Email, u.Role,
                        0, 0, 0, 0, 0, 0, 0, 0, 0
                    );
                }).ToList();

                return new CommuneReport(
                    CommuneId: commune.CommuneId,
                    CommuneNameFr: commune.CommuneFr ?? "",
                    CommuneNameAr: commune.CommuneAr ?? "",
                    Users: userStats
                );
            }).ToList();

            result[communeGroup.Key] = reports;
        }

        return result;
    }
}
