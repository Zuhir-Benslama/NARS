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
/// Request body for creating any account from the public login page.
/// The authorizing admin's credentials are included so no browser session
/// is required.
///
/// Required geographic field per target role:
///   commune_user   → commune_id  (must belong to admin's daira)
///   daira_admin    → daira_id    (must belong to admin's wilaya)
///   wilaya_admin   → wilaya_id   (any; national_admin only)
///   national_admin → not creatable via API
/// </summary>
public record AuthorizedAdminSignupRequest(
    // ── Authorizing admin ────────────────────────────────────────────────────
    [property: JsonPropertyName("admin_username")]
    [Required(AllowEmptyStrings = false)] string AdminUsername,
    [property: JsonPropertyName("admin_password")]
    [Required(AllowEmptyStrings = false)] string AdminPassword,
    // ── New account details ──────────────────────────────────────────────────
    [property: JsonPropertyName("name")]
    [Required(AllowEmptyStrings = false)] string Name,
    [property: JsonPropertyName("email")]
    [Required(AllowEmptyStrings = false), EmailAddress] string Email,
    [property: JsonPropertyName("phone")]
    [Required(AllowEmptyStrings = false)] string Phone,
    [property: JsonPropertyName("username")]
    [Required(AllowEmptyStrings = false)] string Username,
    [property: JsonPropertyName("password")]
    [Required(AllowEmptyStrings = false)] string Password,
    [property: JsonPropertyName("role")]
    [Required(AllowEmptyStrings = false)] string Role,
    // ── Geographic anchor (one required depending on role) ───────────────────
    [property: JsonPropertyName("commune_id")] int? CommuneId,
    [property: JsonPropertyName("daira_id")] int? DairaId,
    [property: JsonPropertyName("wilaya_id")] int? WilayaId
);
public record SignInRequest(
    [Required(AllowEmptyStrings = false)] string Username,
    [Required(AllowEmptyStrings = false)] string Password
);

public record UpdateUserRequest(
    [property: JsonPropertyName("username")]
    [MaxLength(100)] string? Username,
    [property: JsonPropertyName("email")]
    [EmailAddress, MaxLength(255)] string? Email,
    [property: JsonPropertyName("password")]
    [MaxLength(128)] string? Password
);

// ─── RESPONSE DTOs ────────────────────────────────────────────────────────────

public record CommuneInfo(
    [property: JsonPropertyName("id")] int? Id,
    [property: JsonPropertyName("name_fr")] string? NameFr,
    [property: JsonPropertyName("name_ar")] string? NameAr,
    [property: JsonPropertyName("latitude")] double? Latitude,
    [property: JsonPropertyName("longitude")] double? Longitude
);

public record UserInfo(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("commune")] CommuneInfo? Commune = null
);

public record UserInfoWithLocation(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("wilaya")] CommuneInfo? Wilaya = null,
    [property: JsonPropertyName("daira")] CommuneInfo? Daira = null,
    [property: JsonPropertyName("commune")] CommuneInfo? Commune = null
);

public record SignInResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("user")] UserInfo User
);

public record RefreshResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("token_type")] string TokenType
);

public record CreateAdminResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("message")] string Message
);



public record RefreshTokenResult(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("detail")] string? Detail,
    [property: JsonPropertyName("username")] string? Username,
    [property: JsonIgnore] string? NewRawToken,
    [property: JsonIgnore] string? NewAccessToken,
    [property: JsonIgnore] DateTime? RefreshExpiry
);
