using System.Linq;

namespace NarsApi.Infrastructure;

/// <summary>
/// Shared password validation logic used by both registration and profile update.
/// Requires: ≥8 chars, ≥1 uppercase, ≥1 digit, ≥1 non-alphanumeric, not a common password.
/// </summary>
public static class PasswordValidator
{
    private static readonly HashSet<string> CommonPasswords =
    [
        "password", "password1", "password123", "12345678", "123456789",
        "qwerty123", "qwerty1", "admin123", "letmein", "welcome",
        "monkey123", "dragon123", "master123", "passw0rd", "P@ssw0rd",
        "Passw0rd!", "Password1", "Aa123456", "Abc123!", "Test1234",
        "Abc123!@", "Xyz789#!", "Qwerty!1", "Letmein1!", "Welcome@1",
    ];

    public static string? Validate(string password)
    {
        if (password.Length < 8)
        {
            return "Password must be at least 8 characters.";
        }

        if (!password.Any(c => char.IsUpper(c)))
        {
            return "Password must contain at least one uppercase letter.";
        }

        if (!password.Any(c => char.IsDigit(c)))
        {
            return "Password must contain at least one digit.";
        }

        if (!password.Any(c => !char.IsLetterOrDigit(c)))
        {
            return "Password must contain at least one special character.";
        }

        if (CommonPasswords.Contains(password, StringComparer.OrdinalIgnoreCase))
        {
            return "Password is too common. Choose a more complex password.";
        }

        return null;
    }
}
