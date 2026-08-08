using NarsApi.Models;

namespace NarsApi.Services;

/// <summary>Outcome categories for user creation validation.</summary>
public enum UserCreationErrorCode
{
    None = 0,
    Duplicate,
    Invalid,
}

/// <summary>
/// Structured result for user creation so callers can map error codes to
/// HTTP statuses without relying on error-message string matching.
/// </summary>
public record UserCreationResult(User? User, UserCreationErrorCode Code, string? Error)
{
    public bool IsSuccess => Code == UserCreationErrorCode.None;

    public static UserCreationResult Success(User user) => new(user, UserCreationErrorCode.None, null);

    public static UserCreationResult Failure(UserCreationErrorCode code, string error)
        => new(null, code, error);
}

/// <summary>
/// Structured result for the consolidated managed-user creation flow
/// (<see cref="IUserCreationService.CreateUserAsync"/>), so controllers can map
/// authorization and validation failures to HTTP statuses consistently.
/// </summary>
public record ManagedUserCreationResult(
    bool IsSuccess, User? User, string? Error, int StatusCode, bool IsAuthorizationFailure)
{
    public static ManagedUserCreationResult Success(User user)
        => new(true, user, null, 201, false);

    public static ManagedUserCreationResult Failure(int statusCode, string error, bool isAuthorizationFailure = false)
        => new(false, null, error, statusCode, isAuthorizationFailure);
}

/// <summary>Outcome categories for the managed-user update operation.</summary>
public enum UserUpdateErrorCode
{
    None = 0,
    NotFound,
    Forbidden,
    PasswordRequired,
    InvalidPassword,
    EmailConflict,
    Invalid,
}

/// <summary>
/// Structured result for managed-user updates so the controller can map
/// error codes to HTTP statuses without string matching.
/// </summary>
public record UserUpdateResult(UserUpdateErrorCode Code, string? Detail)
{
    public bool IsSuccess => Code == UserUpdateErrorCode.None;

    public static UserUpdateResult Success() => new(UserUpdateErrorCode.None, null);

    public static UserUpdateResult Failure(UserUpdateErrorCode code, string? detail = null)
        => new(code, detail);
}
