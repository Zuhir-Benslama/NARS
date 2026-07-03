namespace NarsApi.Infrastructure;

public static class GeographicValidator
{
    public static string? Validate(string role, int? communeId, int? dairaId, int? wilayaId)
    {
        if (communeId.HasValue && communeId <= 0)
        {
            return "commune_id must be a positive integer.";
        }

        if (dairaId.HasValue && dairaId <= 0)
        {
            return "daira_id must be a positive integer.";
        }

        if (wilayaId.HasValue && wilayaId <= 0)
        {
            return "wilaya_id must be a positive integer.";
        }

        return role switch
        {
            UserRoles.CommuneUser when !communeId.HasValue => "commune_id is required for commune_user.",
            UserRoles.DairaAdmin when !dairaId.HasValue => "daira_id is required for daira_admin.",
            UserRoles.WilayaAdmin when !wilayaId.HasValue => "wilaya_id is required for wilaya_admin.",
            UserRoles.NationalAdmin => "national_admin accounts must be created directly in the database.",
            UserRoles.FieldWorker => null,
            _ => null,
        };
    }
}
