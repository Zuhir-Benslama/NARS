using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NarsApi.Infrastructure;
using NarsApi.Data;
using NarsApi.Models;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests;

/// <summary>
/// Verifies <see cref="AuthenticationExtensions.AddNarsJwtAuthentication"/> configuration:
/// token validation parameters, cookie-based token extraction, challenge/failure logging,
/// and the JwtService registration round-trip.
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

    private static (ServiceProvider Provider, Mock<ILogger> Logger) BuildLoggingProvider()
    {
        var logger = new Mock<ILogger>();
        var factory = new Mock<ILoggerFactory>();
        factory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(logger.Object);
        var provider = new ServiceCollection()
            .AddSingleton(factory.Object)
            .BuildServiceProvider();
        return (provider, logger);
    }

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
        httpContext.Request.Headers.Cookie = $"{CookieNames.AccessToken}=my-access-token";
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
        var (logging, logger) = BuildLoggingProvider();
        using (logging)
        {
            var options = GetJwtOptions(sp);

            var context = new AuthenticationFailedContext(
                new DefaultHttpContext { RequestServices = logging }, Scheme, options)
            {
                Exception = new InvalidOperationException("token expired"),
            };

            await options.Events.OnAuthenticationFailed(context);

            logger.Verify(l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("[Auth] Authentication failed", StringComparison.Ordinal)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }
    }

    [Fact]
    public async Task OnChallenge_LogsWithoutThrowing()
    {
        using var sp = BuildProvider();
        var (logging, logger) = BuildLoggingProvider();
        using (logging)
        {
            var options = GetJwtOptions(sp);

            var context = new JwtBearerChallengeContext(
                new DefaultHttpContext { RequestServices = logging }, Scheme, options, new AuthenticationProperties());

            await options.Events.OnChallenge(context);

            logger.Verify(l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("Challenging", StringComparison.Ordinal)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }
    }

    [Fact]
    public async Task JwtBearer_IsTheOnlyRegisteredAuthenticationScheme()
    {
        using var sp = BuildProvider();
        var schemeProvider = sp.GetRequiredService<IAuthenticationSchemeProvider>();

        var schemes = await schemeProvider.GetAllSchemesAsync();

        // The "Pages" cookie scheme was dead config (PagesController uses
        // [AllowAnonymous] + manual JwtService validation) and has been removed.
        // JWT bearer must be the only registered scheme.
        var scheme = Assert.Single(schemes);
        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, scheme.Name);
    }

    [Fact]
    public void JwtService_RoundTrip_CreatesAndValidatesTokenWithClaims()
    {
        using var sp = BuildProvider(Issuer, Audience);
        var jwt = sp.GetRequiredService<IJwtService>();

        var userId = Guid.NewGuid();
        var token = jwt.CreateToken(
            userId, "alice", "Alice", "alice@test.com",
            communeId: 3, securityStamp: "test-security-stamp",
            role: UserRoles.CommuneUser, dairaId: null, wilayaId: null);

        var principal = jwt.ValidateToken(token);

        Assert.NotNull(principal);
        Assert.Equal("alice", principal!.FindFirstValue(ClaimNames.Username));
        Assert.Equal(userId.ToString(), principal.FindFirstValue(ClaimNames.UserId));
        Assert.Equal("3", principal.FindFirstValue(ClaimNames.CommuneId));
        Assert.Equal("test-security-stamp", principal.FindFirstValue(ClaimNames.SecurityStamp));

        // ValidateToken uses MapInboundClaims=false (matching the JwtBearer pipeline
        // in AuthenticationExtensions), so claim types are kept verbatim.
        Assert.Equal(UserRoles.CommuneUser, principal.FindFirstValue(ClaimNames.Role));
        Assert.Null(principal.FindFirstValue(ClaimTypes.Role));
    }

    [Fact]
    public void JwtBearerPipeline_PrincipalSupportsRoleBasedAuthorization()
    {
        // Regression test for [Authorize(Roles = ...)] returning 403 for every
        // user: the JwtBearer pipeline must map the verbatim "role" claim to the
        // principal's RoleClaimType (and "username" to NameClaimType).
        using var sp = BuildProvider(Issuer, Audience);
        var jwt = sp.GetRequiredService<IJwtService>();
        var options = GetJwtOptions(sp);

        var token = jwt.CreateToken(
            Guid.NewGuid(), "alice", "Alice", "alice@test.com",
            communeId: 3, securityStamp: "test-security-stamp",
            role: UserRoles.FieldWorker, dairaId: null, wilayaId: null);

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var principal = handler.ValidateToken(token, options.TokenValidationParameters, out _);

        Assert.True(principal.IsInRole(UserRoles.FieldWorker));
        Assert.False(principal.IsInRole(UserRoles.NationalAdmin));
        Assert.Equal("alice", principal.Identity?.Name);
    }

    // Tamper-rejection and refresh-token uniqueness are covered canonically in
    // JwtServiceTests; JwtService_RoundTrip above already proves the DI-registered
    // instance creates AND validates tokens.

    private static ClaimsPrincipal PrincipalWith(Guid userId, string stamp) =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimNames.UserId, userId.ToString()),
            new Claim(ClaimNames.SecurityStamp, stamp),
            new Claim(ClaimNames.Username, "alice"),
            new Claim(ClaimNames.Role, UserRoles.CommuneUser),
        }, "Bearer"));

    private static async Task<TokenValidatedContext> RunOnTokenValidatedAsync(
        JwtBearerOptions options, AppDbContext db, ClaimsPrincipal principal, string? cachedStamp = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton<ISecurityStampCache>(Mock.Of<ISecurityStampCache>(
            c => c.GetStampAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()) == Task.FromResult(cachedStamp)));
        await using var sp = services.BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = sp };
        var context = new TokenValidatedContext(httpContext, Scheme, options) { Principal = principal };
        await options.Events.OnTokenValidated(context);
        return context;
    }

    [Fact]
    public async Task OnTokenValidated_MatchingStamp_DoesNotFail()
    {
        using var db = CreateInMemoryDb("StampMatch");
        var user = await SeedData.CreateUserAsync(db, UserRoles.CommuneUser,
            username: "alice", securityStamp: "stamp-abc");
        using var sp = BuildProvider();
        var options = GetJwtOptions(sp);

        var ctx = await RunOnTokenValidatedAsync(options, db, PrincipalWith(user.Id, "stamp-abc"));

        Assert.Null(ctx.Result); // no failure recorded
    }

    [Fact]
    public async Task OnTokenValidated_MismatchedStamp_FailsAuthentication()
    {
        using var db = CreateInMemoryDb("StampMismatch");
        var user = await SeedData.CreateUserAsync(db, UserRoles.CommuneUser,
            username: "alice", securityStamp: "stamp-rotated");
        using var sp = BuildProvider();
        var options = GetJwtOptions(sp);

        var ctx = await RunOnTokenValidatedAsync(options, db, PrincipalWith(user.Id, "stamp-old"));

        Assert.NotNull(ctx.Result);
        Assert.NotNull(ctx.Result!.Failure);
        Assert.Equal("Session has been invalidated (security stamp rotated).", ctx.Result.Failure!.Message);
    }

    [Fact]
    public async Task OnTokenValidated_MissingIdentityClaims_FailsAuthentication()
    {
        using var db = CreateInMemoryDb("StampMissingClaims");
        using var sp = BuildProvider();
        var options = GetJwtOptions(sp);

        var noClaims = new ClaimsPrincipal(new ClaimsIdentity("Bearer"));
        var ctx = await RunOnTokenValidatedAsync(options, db, noClaims);

        Assert.NotNull(ctx.Result);
        Assert.NotNull(ctx.Result!.Failure);
        Assert.Equal("Token is missing identity claims.", ctx.Result.Failure!.Message);
    }

    [Fact]
    public async Task OnTokenValidated_UserDeleted_FailsAuthentication()
    {
        using var db = CreateInMemoryDb("StampDeletedUser");
        using var sp = BuildProvider();
        var options = GetJwtOptions(sp);

        var ctx = await RunOnTokenValidatedAsync(
            options, db, PrincipalWith(Guid.NewGuid(), "stamp-any"));

        Assert.NotNull(ctx.Result);
        Assert.NotNull(ctx.Result!.Failure);
        Assert.Equal("Session has been invalidated (security stamp rotated).", ctx.Result.Failure!.Message);
    }

    [Fact]
    public async Task OnTokenValidated_CacheHit_MatchingStamp_SucceedsWithoutDbLookup()
    {
        // Empty DB proves the cached stamp short-circuits the database query.
        using var db = CreateInMemoryDb("StampCacheHitMatch");
        using var sp = BuildProvider();
        var options = GetJwtOptions(sp);

        var ctx = await RunOnTokenValidatedAsync(
            options, db, PrincipalWith(Guid.NewGuid(), "stamp-abc"), cachedStamp: "stamp-abc");

        Assert.Null(ctx.Result);
    }

    [Fact]
    public async Task OnTokenValidated_CacheHit_MismatchedStamp_FailsAuthentication()
    {
        using var db = CreateInMemoryDb("StampCacheHitMismatch");
        using var sp = BuildProvider();
        var options = GetJwtOptions(sp);

        var ctx = await RunOnTokenValidatedAsync(
            options, db, PrincipalWith(Guid.NewGuid(), "stamp-old"), cachedStamp: "stamp-rotated");

        Assert.NotNull(ctx.Result);
        Assert.NotNull(ctx.Result!.Failure);
        Assert.Equal("Session has been invalidated (security stamp rotated).", ctx.Result.Failure!.Message);
    }
}
