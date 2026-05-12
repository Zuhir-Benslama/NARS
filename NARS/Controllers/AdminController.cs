using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Controllers;

/// <summary>
/// Monitoring and admin-user management endpoints.
///
/// Creation hierarchy (each role may only create the role one level below):
///   commune_user  → field_worker  (inherits the creator's commune_id)
///   daira_admin    → commune_user  (commune must belong to admin's daira)
///   wilaya_admin   → daira_admin   (daira must belong to admin's wilaya)
///   national_admin → wilaya_admin  (any wilaya)
///   national_admin → created directly in the database only (no API endpoint)
///
/// Monitoring hierarchy:
///   daira_admin    → GET /api/admin/overview   (own daira, full depth)
///   wilaya_admin   → GET /api/admin/overview   (own wilaya, full depth)
///                    GET /api/admin/daira/{id}  (one daira within scope)
///   national_admin → GET /api/admin/overview   (all wilayas, shallow)
///                    GET /api/admin/wilaya/{id} (one wilaya, full depth)
///
///   POST /api/admin/users — create an account one level below the caller's role.
///   Auth is split action-level: commune_user may call POST /api/admin/users
///   but not monitoring endpoints.
/// </summary>
[ApiController]
[Route("/api")]
[Tags("Admin")]
public class AdminController(
    AppDbContext db,
    ILogger<AdminController> logger) : NarsControllerBase
{
    // ── GET /api/admin/overview ───────────────────────────────────────────────

    [HttpGet("admin/overview")]
    [Authorize(Roles = "daira_admin,wilaya_admin,national_admin")]
    public async Task<IActionResult> Overview()
    {
        // Read role and geographic IDs directly from the database.
        // JWT claim names can vary depending on when the token was issued
        // (before or after the claim mapping fix), so DB is the source of truth.
        var user = await db.Users.FindAsync(CurrentUserId);
        if (user is null) return Unauthorized(new { detail = "User not found." });

        return user.Role switch
        {
            UserRoles.DairaAdmin when user.DairaId is null =>
                Forbid("daira_id missing on account. Contact your administrator."),
            UserRoles.WilayaAdmin when user.WilayaId is null =>
                Forbid("wilaya_id missing on account. Contact your administrator."),
            UserRoles.DairaAdmin => await DairaOverview(user.DairaId!.Value),
            UserRoles.WilayaAdmin => await WilayaOverview(user.WilayaId!.Value),
            UserRoles.NationalAdmin => await NationalOverview(),
            _ => Forbid(),
        };
    }

    // ── GET /api/admin/wilaya/{wilayaId} ─────────────────────────────────────
    // National admin only — drill into a specific wilaya.

    [HttpGet("admin/wilaya/{wilayaId:int}")]
    [Authorize(Roles = "daira_admin,wilaya_admin,national_admin")]
    public async Task<IActionResult> GetWilaya(int wilayaId)
    {
        var user = await db.Users.FindAsync(CurrentUserId);
        if (user is null) return Unauthorized(new { detail = "User not found." });
        if (user.Role != UserRoles.NationalAdmin) return Forbid();

        var result = await BuildWilayaReportAsync(wilayaId);
        return result is null ? NotFound(new { detail = "Wilaya not found." }) : Ok(result);
    }

    // ── GET /api/admin/daira/{dairaId} ───────────────────────────────────────
    // Wilaya admin (own dairas) or national admin.

    [HttpGet("admin/daira/{dairaId:int}")]
    [Authorize(Roles = "daira_admin,wilaya_admin,national_admin")]
    public async Task<IActionResult> GetDaira(int dairaId)
    {
        var user = await db.Users.FindAsync(CurrentUserId);
        if (user is null) return Unauthorized(new { detail = "User not found." });

        switch (user.Role)
        {
            case UserRoles.WilayaAdmin:
                {
                    var daira = await db.Dairas.FindAsync(dairaId);
                    if (daira is null || daira.WilayaId != user.WilayaId)
                        return Forbid();
                    break;
                }
            case UserRoles.NationalAdmin:
                break;
            default:
                return Forbid();
        }

        var result = await BuildDairaReportAsync(dairaId);
        return result is null ? NotFound(new { detail = "Daira not found." }) : Ok(result);
    }

    // ── POST /api/admin/users ────────────────────────────────────────────────
    // Create an account one level below the caller's role.
    // daira_admin  → commune_user  (commune must be in their daira)
    // wilaya_admin → daira_admin   (daira must be in their wilaya)
    // national_admin → wilaya_admin (any wilaya)
    // national_admin is NOT creatable via API.

    [HttpPost("admin/users")]
    public async Task<IActionResult> CreateAdmin([FromBody] CreateAdminRequest body)
    {
        var creator = await db.Users.FindAsync(CurrentUserId);
        if (creator is null) return Unauthorized();
        var callerRole = creator.Role;

        if (!CanCreateRole(callerRole, body.Role))
            return Forbid();

        switch (callerRole, body.Role)
        {
            case (UserRoles.CommuneUser, UserRoles.FieldWorker):
                // field_worker inherits creator's commune_id, no extra validation needed
                break;
            case (UserRoles.DairaAdmin, UserRoles.CommuneUser):
                {
                    if (!body.CommuneId.HasValue)
                        return BadRequest(new { detail = "commune_id is required." });
                    var commune = await db.Communes.FindAsync(body.CommuneId.Value);
                    if (commune is null || commune.DairaId != creator.DairaId)
                        return Forbid();
                    break;
                }
            case (UserRoles.WilayaAdmin, UserRoles.DairaAdmin):
                {
                    if (!body.DairaId.HasValue)
                        return BadRequest(new { detail = "daira_id is required." });
                    var daira = await db.Dairas.FindAsync(body.DairaId.Value);
                    if (daira is null || daira.WilayaId != creator.WilayaId)
                        return Forbid();
                    break;
                }
            case (UserRoles.NationalAdmin, UserRoles.WilayaAdmin):
                if (!body.WilayaId.HasValue)
                    return BadRequest(new { detail = "wilaya_id is required." });
                break;
        }

        var geoError = ValidateGeographicFields(body);
        if (geoError is not null)
            return BadRequest(new { detail = geoError });

        var existing = await db.Users.FirstOrDefaultAsync(u =>
            u.Username == body.Username || u.Email == body.Email);
        if (existing is not null)
        {
            var field = existing.Username == body.Username ? "Username" : "Email";
            return Conflict(new { detail = $"{field} already exists." });
        }

        var pwdErr = PasswordValidator.Validate(body.Password);
        if (pwdErr is not null)
            return BadRequest(new { detail = pwdErr });

        // field_worker inherits the creator's commune_id
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
            CommuneId = body.Role is UserRoles.CommuneUser or UserRoles.FieldWorker ? communeId : null,
            DairaId = body.Role == UserRoles.DairaAdmin ? body.DairaId : null,
            WilayaId = body.Role == UserRoles.WilayaAdmin ? body.WilayaId : null,
            FailedLoginAttempts = 0,
        };

        db.Users.Add(newUser);
        await db.SaveChangesAsync();

        logger.LogInformation("[Admin] {Creator} ({CreatorRole}) created {Role} account {Username}",
            CurrentUsername, CurrentUserRole, newUser.Role, newUser.Username);

        return StatusCode(201, new CreateAdminResponse(Success: true, UserId: newUser.Id.ToString(), Message: "Account created."));
    }

    // ─── Private: overview builders ──────────────────────────────────────────

    private async Task<IActionResult> DairaOverview(int dairaId)
    {
        var report = await BuildDairaReportAsync(dairaId);
        return report is null ? NotFound(new { detail = "Daira not found." }) : Ok(report);
    }

    private async Task<IActionResult> WilayaOverview(int wilayaId)
    {
        var report = await BuildWilayaReportAsync(wilayaId);
        return report is null ? NotFound(new { detail = "Wilaya not found." }) : Ok(report);
    }

    private async Task<IActionResult> NationalOverview()
    {
        var wilayas = await db.Wilayas.OrderBy(w => w.WilayaId).ToListAsync();

        // Batch-load wilaya admins, daira counts, commune counts, user counts
        var wilayaAdmins = await db.Users
            .Where(u => u.Role == UserRoles.WilayaAdmin)
            .Select(u => new { u.Id, u.Username, u.Name, u.Email, u.Role, u.WilayaId })
            .ToListAsync();

        var dairaCounts = await db.Dairas
            .GroupBy(d => d.WilayaId)
            .Select(g => new { WilayaId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.WilayaId, x => x.Count);

        var communeByWilaya = await db.Communes
            .Join(db.Dairas, c => c.DairaId, d => d.DairaId, (c, d) => new { c.CommuneId, d.WilayaId })
            .GroupBy(x => x.WilayaId)
            .Select(g => new { WilayaId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.WilayaId, x => x.Count);

        var usersByWilaya = await db.Users
            .Where(u => (u.Role == UserRoles.CommuneUser || u.Role == UserRoles.FieldWorker) && u.CommuneId.HasValue)
            .Join(db.Communes, u => u.CommuneId, c => c.CommuneId, (u, c) => new { c.DairaId, u.Id })
            .Join(db.Dairas, x => x.DairaId, d => d.DairaId, (x, d) => new { d.WilayaId, x.Id })
            .GroupBy(x => x.WilayaId)
            .Select(g => new { WilayaId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.WilayaId, x => x.Count);

        var summaries = wilayas.Select(w =>
        {
            var admin = wilayaAdmins.FirstOrDefault(a => a.WilayaId == w.WilayaId);
            return new WilayaSummary(
                WilayaId: w.WilayaId,
                WilayaNameFr: w.WilayaFr ?? "",
                WilayaNameAr: w.WilayaAr ?? "",
                WilayaAdmin: admin is null ? null
                                   : new AdminInfo(admin.Id.ToString(), admin.Username, admin.Name, admin.Email, admin.Role),
                DairaCount: dairaCounts.GetValueOrDefault(w.WilayaId),
                CommuneCount: communeByWilaya.GetValueOrDefault(w.WilayaId),
                CommuneUserCount: usersByWilaya.GetValueOrDefault(w.WilayaId)
            );
        }).ToList();

        return Ok(new { wilayas = summaries });
    }

    // ─── Private: report builders ─────────────────────────────────────────────

    private async Task<WilayaReport?> BuildWilayaReportAsync(int wilayaId)
    {
        var wilaya = await db.Wilayas.FindAsync(wilayaId);
        if (wilaya is null) return null;

        var wilayaAdmin = await db.Users
            .Where(u => u.Role == UserRoles.WilayaAdmin && u.WilayaId == wilayaId)
            .Select(u => new AdminInfo(u.Id.ToString(), u.Username, u.Name, u.Email, u.Role))
            .FirstOrDefaultAsync();

        var dairas = await db.Dairas
            .Where(d => d.WilayaId == wilayaId)
            .OrderBy(d => d.DairaId)
            .ToListAsync();

        var dairaReports = new List<DairaReport>();
        foreach (var daira in dairas)
        {
            var report = await BuildDairaReportAsync(daira);
            if (report is not null)
                dairaReports.Add(report);
        }

        return new WilayaReport(
            WilayaId: wilaya.WilayaId,
            WilayaNameFr: wilaya.WilayaFr ?? "",
            WilayaNameAr: wilaya.WilayaAr ?? "",
            WilayaAdmin: wilayaAdmin,
            Dairas: dairaReports
        );
    }

    private async Task<DairaReport?> BuildDairaReportAsync(int dairaId)
    {
        var daira = await db.Dairas.FindAsync(dairaId);
        return daira is null ? null : await BuildDairaReportAsync(daira);
    }

    private async Task<DairaReport?> BuildDairaReportAsync(Daira daira)
    {
        var dairaAdmin = await db.Users
            .Where(u => u.Role == UserRoles.DairaAdmin && u.DairaId == daira.DairaId)
            .Select(u => new AdminInfo(u.Id.ToString(), u.Username, u.Name, u.Email, u.Role))
            .FirstOrDefaultAsync();

        var communes = await db.Communes
            .Where(c => c.DairaId == daira.DairaId)
            .OrderBy(c => c.CommuneId)
            .ToListAsync();

        // Batch-load all commune-scoped users for this daira in one query
        var communeIds = communes.Select(c => c.CommuneId).ToArray();
        var users = await db.Users
            .Where(u => (u.Role == UserRoles.CommuneUser || u.Role == UserRoles.FieldWorker) && u.CommuneId.HasValue
                     && communeIds.Contains(u.CommuneId!.Value))
            .Select(u => new { u.Id, u.Username, u.Name, u.Email, u.CommuneId, u.Role })
            .ToListAsync();

        // Batch feature counts for all users in one UNION ALL query
        var userIds = users.Select(u => u.Id).ToArray();
        var counts = await GetUserFeatureCountsAsync(userIds);

        var communeReports = communes.Select(c =>
        {
            var communeUsers = users
                .Where(u => u.CommuneId == c.CommuneId)
                .Select(u => counts.TryGetValue(u.Id, out var s) ? s
                    : new UserFeatureStats(u.Id.ToString(), u.Username, u.Name, u.Email, u.Role,
                        0, 0, 0, 0, 0, 0, 0, 0, 0))
                .ToList();

            return new CommuneReport(
                CommuneId: c.CommuneId,
                CommuneNameFr: c.CommuneFr,
                CommuneNameAr: c.CommuneAr,
                Users: communeUsers
            );
        }).ToList();

        return new DairaReport(
            DairaId: daira.DairaId,
            DairaNameFr: daira.DairaFr,
            DairaNameAr: daira.DairaAr,
            DairaAdmin: dairaAdmin,
            Communes: communeReports
        );
    }

    /// <summary>
    /// Returns per-user feature counts for the given user IDs in one UNION ALL
    /// query — O(1) round-trips regardless of how many users are in scope.
    /// </summary>
    private async Task<Dictionary<Guid, UserFeatureStats>> GetUserFeatureCountsAsync(Guid[] userIds)
    {
        if (userIds.Length == 0) return new Dictionary<Guid, UserFeatureStats>();

        await db.Database.OpenConnectionAsync();
        var conn = db.Database.GetDbConnection();
        try
        {
            await using var cmd = conn.CreateCommand();
            // Build the inner UNION ALL from FeatureTypeRegistry so new types
            // are automatically included — no more hardcoded table list.
            var unionBuilder = new System.Text.StringBuilder();
            var descriptors = FeatureTypeRegistry.GetAllDescriptors();
            for (int i = 0; i < descriptors.Count; i++)
            {
                if (i > 0) unionBuilder.Append(" UNION ALL ");
                unionBuilder.Append($"SELECT id, user_id, '{descriptors[i].Type}' AS ft FROM {descriptors[i].TableName}");
            }

            var caseBuilder = new System.Text.StringBuilder();
            for (int i = 0; i < descriptors.Count; i++)
            {
                caseBuilder.AppendLine(
                    $"                    COALESCE(SUM(CASE WHEN f.ft = '{descriptors[i].Type}' THEN 1 ELSE 0 END), 0),");
            }

            cmd.CommandText = $"""
                SELECT
                    u.id,
                    u.username,
                    u.name,
                    u.email,
                    u.role,
                    {caseBuilder}                    COUNT(f.id)
                FROM users u
                LEFT JOIN (
                    {unionBuilder}
                ) f ON f.user_id = u.id
                WHERE u.id = ANY(@ids)
                GROUP BY u.id, u.username, u.name, u.email, u.role
                """;

            var param = cmd.CreateParameter();
            param.ParameterName = "@ids";
            param.Value = userIds;
            cmd.Parameters.Add(param);

            // Build column index map: first 5 columns are id/username/name/email/role,
            // then one CASE SUM per descriptor (+ 1 for the final COUNT column).
            var typeColIndex = new Dictionary<string, int>(descriptors.Count);
            for (int i = 0; i < descriptors.Count; i++)
                typeColIndex[descriptors[i].Type] = 5 + i;
            var totalCol = 5 + descriptors.Count;

            var result = new Dictionary<Guid, UserFeatureStats>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var id = reader.GetGuid(0);
                result[id] = new UserFeatureStats(
                    UserId: id.ToString(),
                    Username: reader.GetString(1),
                    Name: reader.GetString(2),
                    Email: reader.GetString(3),
                    Role: reader.GetString(4),
                    Areas: reader.GetInt64(typeColIndex[FeatureTypes.Area]),
                    Districts: reader.GetInt64(typeColIndex[FeatureTypes.District]),
                    CityCenters: reader.GetInt64(typeColIndex[FeatureTypes.CityCenter]),
                    Roads: reader.GetInt64(typeColIndex[FeatureTypes.Road]),
                    HouseEntrances: reader.GetInt64(typeColIndex[FeatureTypes.HouseEntrance]),
                    PublicBuildings: reader.GetInt64(typeColIndex[FeatureTypes.PublicBuilding]),
                    PublicSpaces: reader.GetInt64(typeColIndex[FeatureTypes.PublicSpace]),
                    NamingPanels: reader.GetInt64(typeColIndex[FeatureTypes.NamingPanel]),
                    Total: reader.GetInt64(totalCol)
                );
            }
            return result;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static bool CanCreateRole(string creatorRole, string targetRole) =>
        AuthController.CanCreateRole(creatorRole, targetRole);

    private static string? ValidateGeographicFields(CreateAdminRequest body) =>
        body.Role switch
        {
            UserRoles.CommuneUser when !body.CommuneId.HasValue => "commune_id is required for commune_user.",
            UserRoles.DairaAdmin when !body.DairaId.HasValue => "daira_id is required for daira_admin.",
            UserRoles.WilayaAdmin when !body.WilayaId.HasValue => "wilaya_id is required for wilaya_admin.",
            UserRoles.NationalAdmin => "national_admin accounts must be created directly in the database.",
            UserRoles.FieldWorker => null, // field_worker inherits commune_id from creator
            _ => null,
        };
}
