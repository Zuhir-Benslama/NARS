using System.Globalization;
using System.Linq;

namespace NarsApi.Infrastructure;

/// <summary>
/// Shared password validation logic used by both registration and profile update.
/// </summary>
public static class PasswordValidator
{
    /// <summary>
    /// Validates password complexity. Returns an error message or null if valid.
    /// Requires: ≥8 chars, ≥1 uppercase, ≥1 digit, ≥1 non-alphanumeric.
    /// </summary>
    public static string? Validate(string password)
    {
        if (password.Length < 8)
            return "Password must be at least 8 characters.";
        if (!password.Any(c => char.IsUpper(c)))
            return "Password must contain at least one uppercase letter.";
        if (!password.Any(c => char.IsDigit(c)))
            return "Password must contain at least one digit.";
        if (!password.Any(c => !char.IsLetterOrDigit(c)))
            return "Password must contain at least one special character.";
        return null;
    }
}
