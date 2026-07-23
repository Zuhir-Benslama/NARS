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
        "1234567890", "qwertyuiop", "asdfghjkl", "zxcvbnm", "iloveyou",
        "princess", "sunshine", "trustno1", "baseball", "football",
        "starwars", "whatever", "password!", "Passw0rd123", "changeme",
        "summer2023", "winter2024", "spring2025", "autumn2022", "test123!",
        "1q2w3e4r", "abc12345", "admin", "root", "toor",
        "passpass", "1qaz2wsx", "zaq1xsw2", "aa123456", "1234qwer",
        "admin2024", "password12", "P@ssword1", "Qwerty123!", "Abcd1234!",
        "N0tS3cur3!", "Summer2024!", "Winter2023!", "Welcome123!", "Changeme1!",
        "P@ssw0rd123", "Admin1234!", "Test@1234", "Qwer1234!", "Asdf1234!",
        "Zxcv1234!", "Pass@1234", "Hello123!", "Charli1!", "Daniel12!",
        "Michael1!", "Jessica1!", "Ashley12!", "Matthew1!", "Joshua12!",
        "Password123!", "P@ssw0rd!", "Welcome1234", "Spring2024!", "Summer2023!",
    ];

    public static string? Validate(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

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
