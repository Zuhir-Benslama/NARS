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
