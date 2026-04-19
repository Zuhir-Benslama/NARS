using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace NarsApi.DTOs;

// ─── STANDARD API RESPONSE ENVELOPE ──────────────────────────────────────────

/// <summary>
/// Standardized API response envelope for consistent client-side error handling.
/// All controllers should use this shape for success/error responses.
/// </summary>
public record ApiResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("message")] string? Message = null,
    [property: JsonPropertyName("detail")] string? Detail = null
)
{
    public static ApiResponse Ok(string? message = null) => new(true, message);
    public static ApiResponse Error(string detail, string? message = null) => new(false, message, detail);
}

/// <summary>
/// Standardized paginated list response with total count for client-side pagination.
/// </summary>
public record PagedResponse<T>(
    [property: JsonPropertyName("items")] IReadOnlyList<T> Items,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("skip")] int Skip,
    [property: JsonPropertyName("take")] int Take,
    [property: JsonPropertyName("success")] bool Success = true
);

// ─── AUTH DTOs ───────────────────────────────────────────────────────────────

/// <summary>
/// Request body for user registration.
/// </summary>
public record SignUpRequest(
    [Required] string Name,
    [Required, EmailAddress] string Email,
    [Required] string Phone,
    [Required] string Username,
    [Required] string Password,
    [Required] int CommuneId
);

/// <summary>
/// Request body for user login.
/// </summary>
public record SignInRequest(
    [Required] string Username,
    [Required] string Password
);

/// <summary>
/// Request body for updating user account details.
/// </summary>
public record UpdateUserRequest(
    [property: JsonPropertyName("username")] string? Username,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("password")] string? Password
);
