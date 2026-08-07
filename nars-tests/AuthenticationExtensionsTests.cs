using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NarsApi.Infrastructure;
using NarsApi.Services;
using Xunit;

namespace NarsApi.Tests;

/// <summary>
/// Verifies <see cref="AuthenticationExtensions.AddNarsJwtAuthentication"/> configuration:
/// token validation parameters, cookie-based token extraction, challenge/failure logging,
/// the Pages cookie scheme, and the JwtService registration round-trip.
/// </summary>
public class AuthenticationExtensionsTests
{
    private const string Issuer = "https://issuer.test";
    private const string Audience = "https://audience.test";

    private static ServiceProvider BuildProvider(string? issuer = null, string? audience = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddOptions<JwtOptions>().Configure(o =>
        {
            o.ExpiresInMinutes = 60;
            o.RefreshExpiresInDays = 30;
        });
        services.AddNarsJwtAuthentication(AuthTestHelper.TestJwtSecret, issuer, audience);
        return services.BuildServiceProvider();
    }

    private static JwtBearerOptions GetJwtOptions(IServiceProvider sp)
        => sp.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);

    private static AuthenticationScheme Scheme => new(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));

    private static ServiceProvider BuildLoggingProvider() =>
        new ServiceCollection().AddLogging().BuildServiceProvider();

    [Fact]
    public void WithIssuerAndAudience_SetsValidationParameters()
    {
        using var sp = BuildProvider(Issuer, Audience);
        var options = GetJwtOptions(sp);

        Assert.True(options.TokenValidationParameters.ValidateIssuer);
        Assert.True(options.TokenValidationParameters.ValidateAudience);
        Assert.Equal(Issuer, options.TokenValidationParameters.ValidIssuer);
        Assert.Equal(Audience, options.TokenValidationParameters.ValidAudience);
        Assert.True(options.TokenValidationParameters.ValidateIssuerSigningKey);
        Assert.Equal(TimeSpan.Zero, options.TokenValidationParameters.ClockSkew);
        Assert.False(options.MapInboundClaims);
    }

    [Fact]
    public void WithoutIssuerOrAudience_DisablesIssuerAudienceValidation()
    {
        using var sp = BuildProvider();
        var options = GetJwtOptions(sp);

        Assert.False(options.TokenValidationParameters.ValidateIssuer);
        Assert.False(options.TokenValidationParameters.ValidateAudience);
    }

    [Fact]
    public async Task OnMessageReceived_ReadsTokenFromHttpOnlyCookie()
    {
        using var sp = BuildProvider();
        var options = GetJwtOptions(sp);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Cookie = "access_token=my-access-token";
        var context = new MessageReceivedContext(httpContext, Scheme, options);

        await options.Events.OnMessageReceived(context);

        Assert.Equal("my-access-token", context.Token);
    }

    [Fact]
    public async Task OnMessageReceived_NoCookie_LeavesTokenNull()
    {
        using var sp = BuildProvider();
        var options = GetJwtOptions(sp);

        var context = new MessageReceivedContext(new DefaultHttpContext(), Scheme, options);

        await options.Events.OnMessageReceived(context);

        Assert.Null(context.Token);
    }

    [Fact]
    public async Task OnAuthenticationFailed_LogsWithoutThrowing()
    {
        using var sp = BuildProvider();
        using var logging = BuildLoggingProvider();
        var options = GetJwtOptions(sp);

        var context = new AuthenticationFailedContext(
            new DefaultHttpContext { RequestServices = logging }, Scheme, options)
        {
            Exception = new InvalidOperationException("token expired"),
        };

        await options.Events.OnAuthenticationFailed(context);
    }

    [Fact]
    public async Task OnChallenge_LogsWithoutThrowing()
    {
        using var sp = BuildProvider();
        using var logging = BuildLoggingProvider();
        var options = GetJwtOptions(sp);

        var context = new JwtBearerChallengeContext(
            new DefaultHttpContext { RequestServices = logging }, Scheme, options, new AuthenticationProperties());

        await options.Events.OnChallenge(context);
    }

    [Fact]
    public void PagesCookieScheme_IsConfiguredSecurely()
    {
        using var sp = BuildProvider();
        var options = sp.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get("Pages");

        Assert.True(options.Cookie.HttpOnly);
        Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
        Assert.Equal(SameSiteMode.Lax, options.Cookie.SameSite);
        Assert.Equal(TimeSpan.FromMinutes(15), options.ExpireTimeSpan);
        Assert.True(options.SlidingExpiration);
        Assert.Equal("/login", options.LoginPath);
    }

    [Fact]
    public void JwtService_RoundTrip_CreatesAndValidatesTokenWithClaims()
    {
        using var sp = BuildProvider(Issuer, Audience);
        var jwt = sp.GetRequiredService<IJwtService>();

        var userId = Guid.NewGuid();
        var token = jwt.CreateToken(
            userId, "alice", "Alice", "alice@test.com",
            communeId: 3, role: UserRoles.CommuneUser, dairaId: null, wilayaId: null);

        var principal = jwt.ValidateToken(token);

        Assert.NotNull(principal);
        Assert.Equal("alice", principal!.FindFirstValue(ClaimNames.Username));
        Assert.Equal(userId.ToString(), principal.FindFirstValue(ClaimNames.UserId));
        Assert.Equal("3", principal.FindFirstValue(ClaimNames.CommuneId));

        // ValidateToken uses MapInboundClaims=false (matching the JwtBearer pipeline
        // in AuthenticationExtensions), so claim types are kept verbatim.
        Assert.Equal(UserRoles.CommuneUser, principal.FindFirstValue(ClaimNames.Role));
        Assert.Null(principal.FindFirstValue(ClaimTypes.Role));
    }

    [Fact]
    public void JwtService_RejectsTamperedToken()
    {
        using var sp = BuildProvider(Issuer, Audience);
        var jwt = sp.GetRequiredService<IJwtService>();

        var token = jwt.CreateToken(
            Guid.NewGuid(), "alice", "Alice", "alice@test.com",
            communeId: null, role: UserRoles.CommuneUser, dairaId: null, wilayaId: null);

        var tampered = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');

        Assert.Null(jwt.ValidateToken(tampered));
    }

    [Fact]
    public void JwtService_IssuesOpaqueRefreshTokens()
    {
        using var sp = BuildProvider(Issuer, Audience);
        var jwt = sp.GetRequiredService<IJwtService>();

        var (raw1, hash1) = jwt.CreateRefreshToken();
        var (raw2, hash2) = jwt.CreateRefreshToken();

        Assert.False(string.IsNullOrEmpty(raw1));
        Assert.False(string.IsNullOrEmpty(raw2));
        Assert.NotEqual(raw1, raw2);
        Assert.NotEqual(hash1, hash2);
    }
}
