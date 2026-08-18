namespace NarsApi.Services;

/// <summary>
/// Strips control characters, HTML-encodes, and truncates log field values.
/// Prevents log injection via control chars and stored XSS in dashboards.
/// </summary>
public interface ILogSanitizer
{
    /// <summary>
    /// Sanitizes a log field value: strips control characters, HTML-encodes,
    /// and truncates to <paramref name="maxLen"/>.
    /// </summary>
    string Sanitize(string? value, int maxLen);
}
