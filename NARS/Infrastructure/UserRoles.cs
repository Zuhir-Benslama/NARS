namespace NarsApi.Infrastructure;

/// <summary>
/// String constants for user role values stored in the DB and JWT claims.
/// Centralised here to avoid magic strings scattered across controllers.
/// </summary>
public static class UserRoles
{
    public const string CommuneUser   = "commune_user";
    public const string DairaAdmin    = "daira_admin";
    public const string WilayaAdmin   = "wilaya_admin";
    public const string NationalAdmin = "national_admin";

    /// <summary>All admin roles — useful for policy declarations.</summary>
    public static readonly string[] AllAdminRoles =
        [DairaAdmin, WilayaAdmin, NationalAdmin];

    /// <summary>Returns true if the role string represents any admin level.</summary>
    public static bool IsAdmin(string? role) =>
        role is DairaAdmin or WilayaAdmin or NationalAdmin;
}
