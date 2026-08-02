using System.IO;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NarsApi.Controllers;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Services;
using Xunit;

namespace NarsApi.Tests;

public class PagesControllerTests
{
    private const string LoginTemplate = "<html><head><meta name=\"csrf-token\" content=\"\"></head><body><script>app();</script></body></html>";
    private const string IndexTemplate = "<html><head><meta name=\"csrf-token\" content=\"\"></head><body><script src=\"/app.js\"></script></body></html>";

    static PagesControllerTests()
    {
        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "wwwroot"));
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "login.html"), LoginTemplate);
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html"), IndexTemplate);
    }

    private sealed class ControllerHarness
    {
        public DefaultHttpContext HttpContext { get; } = CreateHttpContext();
        public Mock<IJwtService> Jwt { get; } = new();
        public Mock<IRefreshTokenService> Refresh { get; } = new();
        public Mock<IAntiforgery> Antiforgery { get; } = new();
        public PagesController Controller { get; }

        public ControllerHarness(string? accessCookie = null, string? refreshCookie = null, string? bearer = null)
        {
            var cookies = new List<string>();
            if (accessCookie is not null)
            {
                cookies.Add($"access_token={accessCookie}");
            }

            if (refreshCookie is not null)
            {
                cookies.Add($"refresh_token={refreshCookie}");
            }

            if (cookies.Count > 0)
            {
                HttpContext.Request.Headers.Cookie = string.Join("; ", cookies);
            }

            if (bearer is not null)
            {
                HttpContext.Request.Headers.Authorization = $"Bearer {bearer}";
            }

            Antiforgery
                .Setup(a => a.GetAndStoreTokens(It.IsAny<HttpContext>()))
                .Returns(new AntiforgeryTokenSet("req-token", "cookie-token", "__RequestVerificationToken", "X-CSRF-TOKEN"));
            Jwt.SetupGet(j => j.AccessTokenExpiresIn).Returns(TimeSpan.FromHours(1));

            var webHost = Mock.Of<IWebHostEnvironment>(e => e.EnvironmentName == Environments.Development);

            Controller = new PagesController(
                Jwt.Object,
                Antiforgery.Object,
                new MemoryCache(new MemoryCacheOptions()),
                webHost,
                Mock.Of<ILogger<PagesController>>(),
                Refresh.Object,
                Options.Create(new CacheOptions()),
                Mock.Of<IDateTimeProvider>(),
                webHost)
            {
                ControllerContext = new ControllerContext { HttpContext = HttpContext },
            };
        }

        public ControllerHarness WithValidPrincipal(string token, string username = "alice")
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, username), new Claim("sub", Guid.NewGuid().ToString())],
                authenticationType: "jwt"));
            Jwt.Setup(j => j.ValidateToken(token)).Returns(principal);
            return this;
        }
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = new ServiceCollection()
            .AddSingleton<IAuthenticationService>(Mock.Of<IAuthenticationService>())
            .BuildServiceProvider();
        return httpContext;
    }

    [Fact]
    public async Task Root_NotAuthenticated_RedirectsToLogin()
    {
        var h = new ControllerHarness();

        var result = await h.Controller.Root();

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/login", redirect.Url);
    }

    [Fact]
    public async Task Root_AuthenticatedViaAccessCookie_RedirectsToMap()
    {
        var h = new ControllerHarness(accessCookie: "valid-token").WithValidPrincipal("valid-token");

        var result = await h.Controller.Root();

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/map", redirect.Url);
    }

    [Fact]
    public async Task LoginPage_ReturnsHtmlWithCsrfTokenInjected()
    {
        var h = new ControllerHarness();
        h.HttpContext.Items["csp-nonce"] = "n1";

        var result = await h.Controller.LoginPage();

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal("text/html", content.ContentType);
        Assert.Contains("csrf-token\" content=\"req-token\"", content.Content);
        Assert.Contains("<script nonce=\"n1\">app();</script>", content.Content);
    }

    [Fact]
    public async Task LoginPage_MissingNonce_UsesEmptyNonce()
    {
        var h = new ControllerHarness();

        var result = await h.Controller.LoginPage();

        var content = Assert.IsType<ContentResult>(result);
        Assert.Contains("<script nonce=\"\">app();</script>", content.Content);
    }

    [Fact]
    public async Task MapPage_NotAuthenticated_RedirectsToLogin()
    {
        var h = new ControllerHarness();

        var result = await h.Controller.MapPage();

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/login", redirect.Url);
    }

    [Fact]
    public async Task MapPage_AuthenticatedViaAccessCookie_ServesPageWithCsrfAndNonce()
    {
        var h = new ControllerHarness(accessCookie: "valid-token").WithValidPrincipal("valid-token");
        h.HttpContext.Items["csp-nonce"] = "n2";

        var result = await h.Controller.MapPage();

        var content = Assert.IsType<ContentResult>(result);
        Assert.Contains("csrf-token\" content=\"req-token\"", content.Content);
        Assert.Contains("<script nonce=\"n2\" src=\"/app.js\">", content.Content);
    }

    [Fact]
    public async Task MapPage_ValidBearerHeader_SetsAccessCookieAndServesPage()
    {
        var h = new ControllerHarness(bearer: "bearer-token").WithValidPrincipal("bearer-token");

        var result = await h.Controller.MapPage();

        Assert.IsType<ContentResult>(result);
        var setCookie = string.Join(";", h.HttpContext.Response.Headers["Set-Cookie"].Where(v => v is not null));
        Assert.Contains("access_token=bearer-token", setCookie);
    }

    [Fact]
    public async Task MapPage_ValidRefreshToken_RotatesAndServesPage()
    {
        var h = new ControllerHarness(refreshCookie: "refresh-token");
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "alice")], "jwt"));
        h.Jwt.Setup(j => j.ValidateToken("new-access-token")).Returns(principal);
        h.Refresh
            .Setup(r => r.RotateRefreshTokenAsync("refresh-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshTokenResult(true, null, "alice", "new-refresh", "new-access-token", DateTime.UtcNow.AddDays(30)));

        var result = await h.Controller.MapPage();

        Assert.IsType<ContentResult>(result);
        var setCookie = string.Join(";", h.HttpContext.Response.Headers["Set-Cookie"].Where(v => v is not null));
        Assert.Contains("access_token=new-access-token", setCookie);
        Assert.Contains("refresh_token=new-refresh", setCookie);
    }

    [Fact]
    public async Task MapPage_RefreshFails_RedirectsToLogin()
    {
        var h = new ControllerHarness(refreshCookie: "refresh-token");
        h.Refresh
            .Setup(r => r.RotateRefreshTokenAsync("refresh-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshTokenResult(false, "Invalid or expired refresh token.", null, null, null, null));

        var result = await h.Controller.MapPage();

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/login", redirect.Url);
    }

    [Fact]
    public async Task MapPage_RefreshReturnsMissingTokens_RedirectsToLogin()
    {
        var h = new ControllerHarness(refreshCookie: "refresh-token");
        h.Refresh
            .Setup(r => r.RotateRefreshTokenAsync("refresh-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshTokenResult(true, null, "alice", null, null, null));

        var result = await h.Controller.MapPage();

        Assert.IsType<RedirectResult>(result);
    }

    [Fact]
    public async Task MapPage_RefreshThrows_RedirectsToLogin()
    {
        var h = new ControllerHarness(refreshCookie: "refresh-token");
        h.Refresh
            .Setup(r => r.RotateRefreshTokenAsync("refresh-token", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await h.Controller.MapPage();

        Assert.IsType<RedirectResult>(result);
    }

    [Fact]
    public async Task MapPage_InvalidAccessTokenCookie_FallsBackToRefresh()
    {
        var h = new ControllerHarness(accessCookie: "expired-token", refreshCookie: "refresh-token");
        h.Refresh
            .Setup(r => r.RotateRefreshTokenAsync("refresh-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshTokenResult(true, null, "alice", "new-refresh", "new-access-token", DateTime.UtcNow.AddDays(30)));

        var result = await h.Controller.MapPage();

        Assert.IsType<ContentResult>(result);
    }
}
