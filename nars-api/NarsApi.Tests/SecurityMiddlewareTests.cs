using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using NarsApi.Infrastructure;
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests;

public class SecurityMiddlewareTests
{
    private static readonly CspOptions DefaultCsp = new();

    private static HttpContext CreateContext(string path)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = new PathString(path);
        return ctx;
    }

    private static Task RunMiddlewareAsync(HttpContext ctx)
        => PipelineExtensions.ApplyCspMiddlewareAsync(ctx, _ => Task.CompletedTask, DefaultCsp);

    private static string? CspHeader(HttpContext ctx)
        => ctx.Response.Headers["Content-Security-Policy"].ToString();

    private static Dictionary<string, string> ParseCspDirectives(string? header)
    {
        if (string.IsNullOrWhiteSpace(header)) return new Dictionary<string, string>();
        return header.Split(';')
            .Select(directive => directive.Trim())
            .Where(directive => directive.Length > 0)
            .Select(directive => directive.Split([' '], 2))
            .ToDictionary(
                parts => parts[0],
                parts => parts.Length > 1 ? parts[1] : string.Empty);
    }

    private static string? ExtractNonce(string? header)
    {
        if (header is null) return null;
        var match = Regex.Match(header, "'nonce-([^']+)'");
        return match.Success ? match.Groups[1].Value : null;
    }

    [Theory]
    [InlineData(LoginPath)]
    [InlineData("/map")]
    public async Task PageRoutes_ScriptsAreNonceLocked_StylesAllowRuntimeInjection(string path)
    {
        var ctx = CreateContext(path);

        await RunMiddlewareAsync(ctx);

        var header = CspHeader(ctx);
        Assert.False(string.IsNullOrWhiteSpace(header));

        var directives = ParseCspDirectives(header);
        Assert.True(directives.ContainsKey("script-src"), "script-src directive must be present");

        // Scripts are the XSS boundary: nonce-locked, never 'unsafe-inline'.
        Assert.Contains("'nonce-", directives["script-src"], StringComparison.Ordinal);
        Assert.DoesNotContain("'unsafe-inline'", directives["script-src"], StringComparison.Ordinal);

        // Styles must allow runtime injection (Vue/maplibre add <style> nodes and
        // inline style attributes at runtime; the SPA shell also ships an inline
        // FOUC-prevention <style>). A nonce in style-src would invalidate
        // 'unsafe-inline' (CSP3), so the header must NOT pair them.
        Assert.True(directives.ContainsKey("style-src"), "style-src directive must be present");
        Assert.Contains("'unsafe-inline'", directives["style-src"], StringComparison.Ordinal);
        Assert.DoesNotContain("'nonce-", directives["style-src"], StringComparison.Ordinal);

        Assert.False(string.IsNullOrEmpty(ExtractNonce(header)));
    }

    [Theory]
    [InlineData(LoginPath)]
    [InlineData("/map")]
    public async Task PageRoutes_HeaderNonceMatchesContextNonce(string path)
    {
        var ctx = CreateContext(path);

        await RunMiddlewareAsync(ctx);

        var headerNonce = ExtractNonce(CspHeader(ctx));
        var ctxNonce = ctx.Items["csp-nonce"] as string;
        Assert.False(string.IsNullOrEmpty(headerNonce));
        Assert.Equal(headerNonce, ctxNonce);
    }

    [Theory]
    [InlineData(LoginPath)]
    [InlineData("/map")]
    public async Task PageRoutes_GetDefenseInDepthHeaders(string path)
    {
        var ctx = CreateContext(path);

        await RunMiddlewareAsync(ctx);

        Assert.Equal("nosniff", ctx.Response.Headers.XContentTypeOptions.ToString());
        Assert.Equal("DENY", ctx.Response.Headers.XFrameOptions.ToString());
        Assert.Equal("strict-origin-when-cross-origin", ctx.Response.Headers["Referrer-Policy"].ToString());
    }

    [Theory]
    [InlineData(ApiAuthSignInPath)]
    [InlineData(ApiFeaturesPath)]
    [InlineData(ApiLogsPath)]
    public async Task ApiRoutes_DoNotGetCspHeader_ButGetNosniff(string path)
    {
        var ctx = CreateContext(path);

        await RunMiddlewareAsync(ctx);

        Assert.True(string.IsNullOrEmpty(CspHeader(ctx)));
        Assert.Null(ctx.Items["csp-nonce"]);
        Assert.Equal("nosniff", ctx.Response.Headers.XContentTypeOptions.ToString());
    }
}
