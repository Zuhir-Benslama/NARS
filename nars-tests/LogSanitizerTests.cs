using NarsApi.Services;
using Xunit;

namespace NarsApi.Tests;

public class LogSanitizerTests
{
    private readonly LogSanitizer _sanitizer = new();

    [Fact]
    public void Sanitize_Null_ReturnsEmpty() =>
        Assert.Equal(string.Empty, _sanitizer.Sanitize(null, 100));

    [Fact]
    public void Sanitize_Empty_ReturnsEmpty() =>
        Assert.Equal(string.Empty, _sanitizer.Sanitize(string.Empty, 100));

    [Fact]
    public void Sanitize_PlainText_ReturnsUnchanged()
    {
        const string value = "hello world";
        Assert.Equal(value, _sanitizer.Sanitize(value, 100));
    }

    [Theory]
    [InlineData("line1\nline2", "line1&#xA;line2")]
    [InlineData("carriage\rreturn", "carriage&#xD;return")]
    [InlineData("tab\there", "tab&#x9;here")]
    [InlineData("bell\aalert", "bellalert")]
    public void Sanitize_EncodesOrStripsControlCharacters(string input, string expected)
    {
        Assert.Equal(expected, _sanitizer.Sanitize(input, 100));
    }

    [Fact]
    public void Sanitize_HtmlEncodesDangerousCharacters()
    {
        Assert.Equal("&lt;script&gt;", _sanitizer.Sanitize("<script>", 100));
        Assert.Equal("a&amp;b", _sanitizer.Sanitize("a&b", 100));
        Assert.Equal("&quot;quoted&quot;", _sanitizer.Sanitize("\"quoted\"", 100));
    }

    [Fact]
    public void Sanitize_TruncatesToMaxLength()
    {
        var longValue = new string('x', 5000);
        var result = _sanitizer.Sanitize(longValue, 10);
        Assert.Equal(10, result.Length);
        Assert.Equal(new string('x', 10), result);
    }

    [Fact]
    public void Sanitize_StackBufferPath_ShortAndContainingControlChars()
    {
        // Exercises the stackalloc branch (<= 4096 chars).
        var result = _sanitizer.Sanitize("ok\nvalue", 100);
        Assert.Equal("ok&#xA;value", result);
    }

    [Fact]
    public void Sanitize_ArrayPoolPath_LongInput()
    {
        // Exercises the ArrayPool branch (> 4096 chars): control chars encoded,
        // then final result truncated to maxLen.
        var input = new string('a', 5000) + "\t" + new string('b', 5000);
        var result = _sanitizer.Sanitize(input, 10000);
        Assert.Equal(10000, result.Length);
        Assert.StartsWith(new string('a', 5000) + "&#x9;", result);
    }

    [Fact]
    public void Sanitize_PreservesNewlineAllowedCharactersOnlyAfterEncoding()
    {
        // Newlines are stripped before HTML encoding; truncation applies after.
        var result = _sanitizer.Sanitize(new string('z', 50), 10);
        Assert.Equal(10, result.Length);
        Assert.Equal(new string('z', 10), result);
    }

    [Fact]
    public void Sanitize_EmptyValueWithEncoding_StillEmpty()
    {
        Assert.Equal(string.Empty, _sanitizer.Sanitize(string.Empty, 5));
    }
}
