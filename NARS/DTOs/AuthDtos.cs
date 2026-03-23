using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace NarsApi.DTOs;

public record SignUpRequest(
    [Required] string Name,
    [Required, EmailAddress] string Email,
    [Required] string Phone,
    [Required] string Username,
    [Required] string Password,
    [Required] int CommuneId
);

public record SignInRequest(
    [Required] string Username,
    [Required] string Password
);

public record UpdateUserRequest(
    [property: JsonPropertyName("username")] string? Username,
    [property: JsonPropertyName("email")]    string? Email,
    [property: JsonPropertyName("password")] string? Password
);
