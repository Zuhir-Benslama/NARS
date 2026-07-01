using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace NarsApi.DTOs;

// ─── ADMIN USER CREATION ─────────────────────────────────────────────────────

/// <summary>
/// Request body for creating a new admin account.
/// Only admin users of higher or equal level may call this endpoint.
/// </summary>
public record CreateAdminRequest(
    [param: Required][property: JsonRequired][property: JsonPropertyName("name")] string Name,
    [param: Required][property: JsonRequired][property: JsonPropertyName("email")] string Email,
    [param: Required][property: JsonRequired][property: JsonPropertyName("phone")] string Phone,
    [param: Required][property: JsonRequired][property: JsonPropertyName("username")] string Username,
    [param: Required][property: JsonRequired][property: JsonPropertyName("password")] string Password,
    [param: Required][property: JsonRequired][property: JsonPropertyName("role")] string Role,
    /// <summary>Required when role = commune_user.</summary>
    [property: JsonPropertyName("commune_id")] int? CommuneId,
    /// <summary>Required when role = daira_admin.</summary>
    [property: JsonPropertyName("daira_id")] int? DairaId,
    /// <summary>Required when role = wilaya_admin.</summary>
    [property: JsonPropertyName("wilaya_id")] int? WilayaId
);

// ─── ADMIN USER UPDATE ───────────────────────────────────────────────────────

/// <summary>
/// Request body for updating an existing admin account.
/// All fields are optional — only provided fields will be updated.
/// </summary>
public record UpdateAdminRequest(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("phone")] string? Phone,
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("commune_id")] int? CommuneId,
    [property: JsonPropertyName("daira_id")] int? DairaId,
    [property: JsonPropertyName("wilaya_id")] int? WilayaId,
    [property: JsonPropertyName("password")] string? Password
);

// ─── ADMIN USER SUMMARY ──────────────────────────────────────────────────────

/// <summary>
/// Slim user record returned in GET /api/admin/users list.
/// </summary>
public record AdminUserSummary(
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("phone")] string Phone,
    [property: JsonPropertyName("commune_id")] int? CommuneId,
    [property: JsonPropertyName("daira_id")] int? DairaId,
    [property: JsonPropertyName("wilaya_id")] int? WilayaId
);

// ─── MONITORING HIERARCHY ─────────────────────────────────────────────────────

/// <summary>Per-feature-type count summary for one commune user.</summary>
public record UserFeatureStats(
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("areas")] long Areas,
    [property: JsonPropertyName("districts")] long Districts,
    [property: JsonPropertyName("city_centers")] long CityCenters,
    [property: JsonPropertyName("roads")] long Roads,
    [property: JsonPropertyName("house_entrances")] long HouseEntrances,
    [property: JsonPropertyName("public_buildings")] long PublicBuildings,
    [property: JsonPropertyName("public_spaces")] long PublicSpaces,
    [property: JsonPropertyName("naming_panels")] long NamingPanels,
    [property: JsonPropertyName("total")] long Total
);

/// <summary>Slim info for an admin account (no password hash, no sensitive fields).</summary>
public record AdminInfo(
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("role")] string Role
);

/// <summary>A commune with all its commune users and their feature stats.</summary>
public record CommuneReport(
    [property: JsonPropertyName("commune_id")] int CommuneId,
    [property: JsonPropertyName("commune_name_fr")] string CommuneNameFr,
    [property: JsonPropertyName("commune_name_ar")] string CommuneNameAr,
    [property: JsonPropertyName("users")] IReadOnlyList<UserFeatureStats> Users
);

/// <summary>A daira with its daira admin and its communes' reports.</summary>
public record DairaReport(
    [property: JsonPropertyName("daira_id")] int DairaId,
    [property: JsonPropertyName("daira_name_fr")] string DairaNameFr,
    [property: JsonPropertyName("daira_name_ar")] string DairaNameAr,
    [property: JsonPropertyName("daira_admin")] AdminInfo? DairaAdmin,
    [property: JsonPropertyName("communes")] IReadOnlyList<CommuneReport> Communes
);

/// <summary>A wilaya with its wilaya admin and its dairas' reports.</summary>
public record WilayaReport(
    [property: JsonPropertyName("wilaya_id")] int WilayaId,
    [property: JsonPropertyName("wilaya_name_fr")] string WilayaNameFr,
    [property: JsonPropertyName("wilaya_name_ar")] string WilayaNameAr,
    [property: JsonPropertyName("wilaya_admin")] AdminInfo? WilayaAdmin,
    [property: JsonPropertyName("dairas")] IReadOnlyList<DairaReport> Dairas
);

/// <summary>
/// Summary row returned in the national admin top-level view.
/// Intentionally shallow — drill into a wilaya with GET /api/admin/wilaya/{id}.
/// </summary>
public record WilayaSummary(
    [property: JsonPropertyName("wilaya_id")] int WilayaId,
    [property: JsonPropertyName("wilaya_name_fr")] string WilayaNameFr,
    [property: JsonPropertyName("wilaya_name_ar")] string WilayaNameAr,
    [property: JsonPropertyName("wilaya_admin")] AdminInfo? WilayaAdmin,
    [property: JsonPropertyName("daira_count")] int DairaCount,
    [property: JsonPropertyName("commune_count")] int CommuneCount,
    [property: JsonPropertyName("commune_user_count")] int CommuneUserCount
);
