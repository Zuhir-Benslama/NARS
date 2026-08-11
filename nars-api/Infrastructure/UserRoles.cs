namespace NarsApi.Infrastructure;

/// <summary>
/// String constants for user role values stored in the DB and JWT claims.
/// Centralised here to avoid magic strings scattered across controllers.
/// </summary>
public static class UserRoles
{
    public const string CommuneUser = "commune_user";
    public const string DairaAdmin = "daira_admin";
    public const string WilayaAdmin = "wilaya_admin";
    public const string NationalAdmin = "national_admin";
    public const string FieldWorker = "field_worker";

    /// <summary>Roles allowed to view any daira's report.</summary>
    public const string WilayaOrNationalAdmin = WilayaAdmin + "," + NationalAdmin;

    /// <summary>Any admin level — for [Authorize(Roles = ...)] gates open to all admins.</summary>
    public const string AnyAdmin = DairaAdmin + "," + WilayaAdmin + "," + NationalAdmin;

    /// <summary>Roles allowed to create/manage lower-tier accounts (AdminUserController).</summary>
    public const string UserManagementRoles = NationalAdmin + "," + WilayaAdmin + "," + DairaAdmin + "," + CommuneUser;

    /// <summary>All admin roles — useful for policy declarations.</summary>
    public static readonly string[] AllAdminRoles =
        [DairaAdmin, WilayaAdmin, NationalAdmin];

    /// <summary>Returns true if the role string represents any admin level.</summary>
    public static bool IsAdmin(string? role) =>
        role is DairaAdmin or WilayaAdmin or NationalAdmin;

    /// <summary>
    /// Roles allowed to review (accept/reject) AI-suggested draft features.
    /// Field workers and commune users operate in a commune scope; all admins
    /// can review from any scope.
    /// </summary>
    public static bool IsDraftReviewer(string? role) =>
        role is FieldWorker or CommuneUser or DairaAdmin or WilayaAdmin or NationalAdmin;

    /// <summary>
    /// Roles that operate within a commune scope.
    /// commune_user draws features; field_worker inspects them.
    /// </summary>
    public static bool IsCommuneScoped(string? role) =>
        role is CommuneUser or FieldWorker;
}
