using System.Buffers;
using System.Text.Encodings.Web;

namespace NarsApi.Services;

/// <summary>
/// High-performance log field sanitizer using stackalloc for small payloads
/// and ArrayPool for larger ones. Shared across all log-producing code paths.
/// </summary>
public sealed class LogSanitizer : ILogSanitizer
{
    private const int StackBufferCharLimit = 4096;

    public string Sanitize(string? value, int maxLen)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        var encoded = HtmlEncoder.Default.Encode(SanitizeControlCharacters(value, maxLen));
        return TruncateEncoded(encoded, maxLen);
    }

    /// <summary>
    /// Truncates an HTML-encoded string to <paramref name="maxLen"/> characters
    /// without splitting a multi-character HTML entity in half (e.g. <c>&amp;amp;</c>
    /// must never be cut to <c>&amp;am</c>). Every <c>&amp;</c> in an encoded string
    /// opens an entity terminated by <c>;</c> (the encoder escapes any literal
    /// <c>&amp;</c>), so backing off to the opening <c>&amp;</c> when the cut lands
    /// inside an entity keeps the stored value rendering correctly while never
    /// exceeding the column width.
    /// </summary>
    private static string TruncateEncoded(string encoded, int maxLen)
    {
        if (encoded.Length <= maxLen)
        {
            return encoded;
        }

        var end = maxLen;
        var lastAmp = encoded.LastIndexOf('&', end - 1);
        var lastSemi = encoded.LastIndexOf(';', end - 1);
        if (lastAmp >= 0 && lastAmp < end && lastSemi < lastAmp)
        {
            end = lastAmp;
        }

        return encoded[..end];
    }

    private static string SanitizeControlCharacters(string value, int maxLen)
    {
        var capacity = Math.Min(value.Length, maxLen);
        if (capacity <= StackBufferCharLimit)
        {
            Span<char> buffer = stackalloc char[capacity];
            var written = SanitizeInto(value, buffer);
            return new string(buffer[..written]);
        }

        var rented = ArrayPool<char>.Shared.Rent(capacity);
        try
        {
            var written = SanitizeInto(value, rented);
            return new string(rented.AsSpan(0, written));
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }
    }

    private static int SanitizeInto(string value, Span<char> buffer)
    {
        var written = 0;
        foreach (var c in value)
        {
            if (written == buffer.Length)
            {
                break;
            }

            if (c is '\n' or '\r' or '\t' || !char.IsControl(c))
            {
                buffer[written++] = c;
            }
        }

        return written;
    }
}
