namespace NarsApi.Services;

public record ScopeValidationResult(string? Error, bool IsAuthorizationFailure);

public interface IUserAuthorizationService
{
    /// <summary>Checks whether a caller role is allowed to create a target role.</summary>
    bool CanCreateRole(string callerRole, string targetRole);
    /// <summary>Validates the geographic scope when creating a lower-tier account.</summary>
    Task<ScopeValidationResult> ValidateCreateUserScopeAsync(
        string callerRole, int? callerDairaId, int? callerWilayaId,
        string targetRole, int? communeId, int? dairaId, int? wilayaId,
        CancellationToken ct = default);
}
