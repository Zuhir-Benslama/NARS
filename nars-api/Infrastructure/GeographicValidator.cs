namespace NarsApi.Infrastructure;

public static class GeographicValidator
{
    public static string? Validate(string role, int? communeId, int? dairaId, int? wilayaId) =>
        role switch
        {
            UserRoles.CommuneUser when !communeId.HasValue => "commune_id is required for commune_user.",
            UserRoles.DairaAdmin when !dairaId.HasValue => "daira_id is required for daira_admin.",
            UserRoles.WilayaAdmin when !wilayaId.HasValue => "wilaya_id is required for wilaya_admin.",
            UserRoles.NationalAdmin => "national_admin accounts must be created directly in the database.",
            UserRoles.FieldWorker => null,
            _ => null,
        };
}
