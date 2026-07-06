namespace NarsApi.Services;

public record ScopeValidationResult(string? Error, bool IsAuthorizationFailure);

public interface IUserAuthorizationService
{
    bool CanCreateRole(string callerRole, string targetRole);
    Task<ScopeValidationResult> ValidateCreateUserScopeAsync(
        string callerRole, int? callerDairaId, int? callerWilayaId,
        string targetRole, int? communeId, int? dairaId, int? wilayaId,
        CancellationToken ct = default);
}
