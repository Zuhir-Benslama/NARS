using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using NarsApi.Infrastructure;
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

    private static string? ExtractNonce(string? header)
    {
        if (header is null) return null;
        var match = Regex.Match(header, "'nonce-([^']+)'");
        return match.Success ? match.Groups[1].Value : null;
    }

    [Theory]
    [InlineData("/login")]
    [InlineData("/map")]
    public async Task PageRoutes_GetCspHeaderWithNonce_AndNoUnsafeInline(string path)
    {
        var ctx = CreateContext(path);

        await RunMiddlewareAsync(ctx);

        var header = CspHeader(ctx);
        Assert.False(string.IsNullOrWhiteSpace(header));
        Assert.Contains("script-src", header, StringComparison.Ordinal);
        Assert.DoesNotContain("'unsafe-inline'", header, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(ExtractNonce(header)));
    }

    [Theory]
    [InlineData("/login")]
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
    [InlineData("/login")]
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
    [InlineData("/api/auth/signin")]
    [InlineData("/api/features")]
    [InlineData("/api/logs")]
    public async Task ApiRoutes_DoNotGetCspHeader(string path)
    {
        var ctx = CreateContext(path);

        await RunMiddlewareAsync(ctx);

        Assert.True(string.IsNullOrEmpty(CspHeader(ctx)));
        Assert.Null(ctx.Items["csp-nonce"]);
    }
}
