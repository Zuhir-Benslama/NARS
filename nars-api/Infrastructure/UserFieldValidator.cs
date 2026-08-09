using System.ComponentModel.DataAnnotations;

namespace NarsApi.Infrastructure;

/// <summary>
/// Shared validation for user profile fields that DataAnnotations cannot
/// enforce reliably: attributes on nullable record positional parameters are
/// dropped by the C# compiler, so [EmailAddress]/[MaxLength] on optional
/// update fields never run. Validate explicitly here.
/// </summary>
public static class UserFieldValidator
{
    public const int MaxEmailLength = 255;
    public const int MaxUsernameLength = 100;
    public const int MaxNameLength = 255;
    public const int MaxPhoneLength = 50;

    private static readonly EmailAddressAttribute EmailAttribute = new();

    /// <summary>Returns an error message if the email is invalid, else null.</summary>
    public static string? ValidateEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        if (email.Length > MaxEmailLength)
        {
            return $"Email must be at most {MaxEmailLength} characters.";
        }

        if (!EmailAttribute.IsValid(email))
        {
            return "Email is not a valid email address.";
        }

        return null;
    }

    /// <summary>Returns an error message if the string exceeds the given limit, else null.</summary>
    public static string? ValidateMaxLength(string? value, int maxLength, string fieldName)
    {
        if (value is null || value.Length <= maxLength)
        {
            return null;
        }

        return $"{fieldName} must be at most {maxLength} characters.";
    }
}
