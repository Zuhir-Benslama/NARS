using System.Data;
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
/// Hierarchy:
///   national_admin  →  GET /api/admin/overview          (all wilayas, shallow)
///                      GET /api/admin/wilaya/{id}        (one wilaya, full depth)
///   wilaya_admin    →  GET /api/admin/overview          (own wilaya, full depth)
///                      GET /api/admin/daira/{id}         (one daira, full depth — within scope)
///   daira_admin     →  GET /api/admin/overview          (own daira, full depth)
///
///   POST /api/admin/users — create a lower-level admin account.
/// </summary>
[ApiController]
[Tags("Admin")]
public class AdminController(
    AppDbContext db,
    ILogger<AdminController> logger) : NarsControllerBase
{
    // ── GET /api/admin/overview ───────────────────────────────────────────────

    [HttpGet("/api/admin/overview")]
    public async Task<IActionResult> Overview()
    {
        return CurrentUserRole switch
        {
            UserRoles.DairaAdmin    => await DairaOverview(CurrentDairaId
                ?? throw new InvalidOperationException("daira_id claim missing for daira_admin.")),
            UserRoles.WilayaAdmin   => await WilayaOverview(CurrentWilayaId
                ?? throw new InvalidOperationException("wilaya_id claim missing for wilaya_admin.")),
            UserRoles.NationalAdmin => await NationalOverview(),
            _ => Forbid(),
        };
    }

    // ── GET /api/admin/wilaya/{wilayaId} ─────────────────────────────────────
    // National admin only — drill into a specific wilaya.

    [HttpGet("/api/admin/wilaya/{wilayaId:int}")]
    public async Task<IActionResult> GetWilaya(int wilayaId)
    {
        if (CurrentUserRole != UserRoles.NationalAdmin)
            return Forbid();

        var result = await BuildWilayaReportAsync(wilayaId);
        return result is null ? NotFound(new { detail = "Wilaya not found." }) : Ok(result);
    }

    // ── GET /api/admin/daira/{dairaId} ───────────────────────────────────────
    // Wilaya admin (own dairas) or national admin.

    [HttpGet("/api/admin/daira/{dairaId:int}")]
    public async Task<IActionResult> GetDaira(int dairaId)
    {
        switch (CurrentUserRole)
        {
            case UserRoles.WilayaAdmin:
            {
                // Enforce scope — daira must belong to admin's wilaya.
                var daira = await db.Dairas.FindAsync(dairaId);
                if (daira is null || daira.WilayaId != CurrentWilayaId)
                    return Forbid();
                break;
            }
            case UserRoles.NationalAdmin:
                break; // no scope restriction
            default:
                return Forbid();
        }

        var result = await BuildDairaReportAsync(dairaId);
        return result is null ? NotFound(new { detail = "Daira not found." }) : Ok(result);
    }

    // ── POST /api/admin/users ────────────────────────────────────────────────
    // Create a new admin account. Role hierarchy:
    //   national_admin  → can create wilaya_admin, daira_admin
    //   wilaya_admin    → can create daira_admin within their wilaya
    //   daira_admin     → cannot create admins

    [HttpPost("/api/admin/users")]
    public async Task<IActionResult> CreateAdmin([FromBody] CreateAdminRequest body)
    {
        // Role permission check
        if (!CanCreateRole(CurrentUserRole, body.Role))
            return Forbid();

        // Scope check: wilaya_admin can only create daira_admins in their wilaya
        if (CurrentUserRole == UserRoles.WilayaAdmin && body.Role == UserRoles.DairaAdmin)
        {
            if (!body.DairaId.HasValue)
                return BadRequest(new { detail = "daira_id is required for daira_admin." });
            var daira = await db.Dairas.FindAsync(body.DairaId.Value);
            if (daira is null || daira.WilayaId != CurrentWilayaId)
                return Forbid();
        }

        // Validate geographic fields per role
        var geoError = ValidateGeographicFields(body);
        if (geoError is not null)
            return BadRequest(new { detail = geoError });

        // Uniqueness check
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

        var user = new User
        {
            Name          = body.Name,
            Email         = body.Email,
            Phone         = body.Phone,
            Username      = body.Username,
            PasswordHash  = BCrypt.Net.BCrypt.HashPassword(body.Password),
            Role          = body.Role,
            CommuneId     = null,
            DairaId       = body.DairaId,
            WilayaId      = body.WilayaId,
            FailedLoginAttempts = 0,
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        logger.LogInformation("[Admin] {Creator} ({CreatorRole}) created admin {Username} ({Role})",
            CurrentUsername, CurrentUserRole, user.Username, user.Role);

        return StatusCode(201, new { success = true, user_id = user.Id.ToString(), message = "Admin account created." });
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
            .Where(u => u.Role == UserRoles.CommuneUser && u.CommuneId.HasValue)
            .Join(db.Communes, u => u.CommuneId, c => c.CommuneId, (u, c) => new { c.DairaId, u.Id })
            .Join(db.Dairas, x => x.DairaId, d => d.DairaId, (x, d) => new { d.WilayaId, x.Id })
            .GroupBy(x => x.WilayaId)
            .Select(g => new { WilayaId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.WilayaId, x => x.Count);

        var summaries = wilayas.Select(w =>
        {
            var admin = wilayaAdmins.FirstOrDefault(a => a.WilayaId == w.WilayaId);
            return new WilayaSummary(
                WilayaId:        w.WilayaId,
                WilayaNameFr:    w.WilayaFr ?? "",
                WilayaNameAr:    w.WilayaAr ?? "",
                WilayaAdmin:     admin is null ? null
                                   : new AdminInfo(admin.Id.ToString(), admin.Username, admin.Name, admin.Email, admin.Role),
                DairaCount:      dairaCounts.GetValueOrDefault(w.WilayaId),
                CommuneCount:    communeByWilaya.GetValueOrDefault(w.WilayaId),
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
            dairaReports.Add(await BuildDairaReportAsync(daira) ?? throw new InvalidOperationException());

        return new WilayaReport(
            WilayaId:     wilaya.WilayaId,
            WilayaNameFr: wilaya.WilayaFr ?? "",
            WilayaNameAr: wilaya.WilayaAr ?? "",
            WilayaAdmin:  wilayaAdmin,
            Dairas:       dairaReports
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

        // Batch-load all commune users for this daira in one query
        var communeIds = communes.Select(c => c.CommuneId).ToArray();
        var users = await db.Users
            .Where(u => u.Role == UserRoles.CommuneUser && u.CommuneId.HasValue
                     && communeIds.Contains(u.CommuneId!.Value))
            .Select(u => new { u.Id, u.Username, u.Name, u.Email, u.CommuneId })
            .ToListAsync();

        // Batch feature counts for all users in one UNION ALL query
        var userIds = users.Select(u => u.Id).ToArray();
        var counts = await GetUserFeatureCountsAsync(userIds);

        var communeReports = communes.Select(c =>
        {
            var communeUsers = users
                .Where(u => u.CommuneId == c.CommuneId)
                .Select(u => counts.TryGetValue(u.Id, out var s) ? s
                    : new UserFeatureStats(u.Id.ToString(), u.Username, u.Name, u.Email,
                        0, 0, 0, 0, 0, 0, 0, 0, 0))
                .ToList();

            return new CommuneReport(
                CommuneId:      c.CommuneId,
                CommuneNameFr:  c.CommuneFr,
                CommuneNameAr:  c.CommuneAr,
                Users:          communeUsers
            );
        }).ToList();

        return new DairaReport(
            DairaId:     daira.DairaId,
            DairaNameFr: daira.DairaFr,
            DairaNameAr: daira.DairaAr,
            DairaAdmin:  dairaAdmin,
            Communes:    communeReports
        );
    }

    /// <summary>
    /// Returns per-user feature counts for the given user IDs in one UNION ALL
    /// query — O(1) round-trips regardless of how many users are in scope.
    /// </summary>
    private async Task<Dictionary<Guid, UserFeatureStats>> GetUserFeatureCountsAsync(Guid[] userIds)
    {
        if (userIds.Length == 0) return new Dictionary<Guid, UserFeatureStats>();

        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT
                    u.id,
                    u.username,
                    u.name,
                    u.email,
                    COALESCE(SUM(CASE WHEN f.ft = 'area'            THEN 1 ELSE 0 END), 0),
                    COALESCE(SUM(CASE WHEN f.ft = 'district'        THEN 1 ELSE 0 END), 0),
                    COALESCE(SUM(CASE WHEN f.ft = 'city_center'     THEN 1 ELSE 0 END), 0),
                    COALESCE(SUM(CASE WHEN f.ft = 'road'            THEN 1 ELSE 0 END), 0),
                    COALESCE(SUM(CASE WHEN f.ft = 'house_entrance'  THEN 1 ELSE 0 END), 0),
                    COALESCE(SUM(CASE WHEN f.ft = 'public_building' THEN 1 ELSE 0 END), 0),
                    COALESCE(SUM(CASE WHEN f.ft = 'public_space'    THEN 1 ELSE 0 END), 0),
                    COALESCE(SUM(CASE WHEN f.ft = 'naming_panel'    THEN 1 ELSE 0 END), 0),
                    COUNT(f.id)
                FROM users u
                LEFT JOIN (
                    SELECT id, user_id, 'area'            AS ft FROM areas
                    UNION ALL SELECT id, user_id, 'district'        FROM districts
                    UNION ALL SELECT id, user_id, 'city_center'     FROM city_centers
                    UNION ALL SELECT id, user_id, 'road'            FROM roads
                    UNION ALL SELECT id, user_id, 'house_entrance'  FROM house_entrances
                    UNION ALL SELECT id, user_id, 'public_building' FROM public_buildings
                    UNION ALL SELECT id, user_id, 'public_space'    FROM public_spaces
                    UNION ALL SELECT id, user_id, 'naming_panel'    FROM naming_panels
                ) f ON f.user_id = u.id
                WHERE u.id = ANY(@ids)
                GROUP BY u.id, u.username, u.name, u.email
                """;

            var param = cmd.CreateParameter();
            param.ParameterName = "@ids";
            param.Value = userIds;
            cmd.Parameters.Add(param);

            var result = new Dictionary<Guid, UserFeatureStats>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var id = reader.GetGuid(0);
                result[id] = new UserFeatureStats(
                    UserId:         id.ToString(),
                    Username:       reader.GetString(1),
                    Name:           reader.GetString(2),
                    Email:          reader.GetString(3),
                    Areas:          reader.GetInt64(4),
                    Districts:      reader.GetInt64(5),
                    CityCenters:    reader.GetInt64(6),
                    Roads:          reader.GetInt64(7),
                    HouseEntrances: reader.GetInt64(8),
                    PublicBuildings:reader.GetInt64(9),
                    PublicSpaces:   reader.GetInt64(10),
                    NamingPanels:   reader.GetInt64(11),
                    Total:          reader.GetInt64(12)
                );
            }
            return result;
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if <paramref name="creatorRole"/> is allowed to create an
    /// account with <paramref name="targetRole"/>.
    /// </summary>
    private static bool CanCreateRole(string creatorRole, string targetRole) =>
        (creatorRole, targetRole) switch
        {
            (UserRoles.NationalAdmin, UserRoles.WilayaAdmin)   => true,
            (UserRoles.NationalAdmin, UserRoles.DairaAdmin)    => true,
            (UserRoles.WilayaAdmin,   UserRoles.DairaAdmin)    => true,
            _ => false,
        };

    private static string? ValidateGeographicFields(CreateAdminRequest body) =>
        body.Role switch
        {
            UserRoles.WilayaAdmin   when !body.WilayaId.HasValue => "wilaya_id is required for wilaya_admin.",
            UserRoles.DairaAdmin    when !body.DairaId.HasValue  => "daira_id is required for daira_admin.",
            UserRoles.NationalAdmin when body.WilayaId.HasValue || body.DairaId.HasValue
                => "national_admin accounts must not have a geographic restriction.",
            _ => null,
        };
}
