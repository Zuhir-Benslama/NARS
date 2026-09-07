using Xunit;
using NarsApi.Infrastructure;
using static NarsApi.Tests.TestData;

namespace NarsApi.Tests;

public class CsrfValidationTests
{
    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    public void ShouldValidateCsrfRequest_SafeMethods_ReturnsFalse(string method) =>
        Assert.False(PipelineExtensions.ShouldValidateCsrfRequest(method, true, true, false, ApiFeaturesPath, out _));

    [Fact]
    public void ShouldValidateCsrfRequest_AuthenticatedApiPostInProduction_ReturnsTrue() =>
        Assert.True(PipelineExtensions.ShouldValidateCsrfRequest("POST", true, true, false, ApiFeaturesPath, out _));

    [Fact]
    public void ShouldValidateCsrfRequest_AuthenticatedApiDeleteInProduction_ReturnsTrue() =>
        Assert.True(PipelineExtensions.ShouldValidateCsrfRequest("DELETE", true, true, false, "/api/features/{id}", out _));

    [Fact]
    public void ShouldValidateCsrfRequest_AuthenticatedApiPutInProduction_ReturnsTrue() =>
        Assert.True(PipelineExtensions.ShouldValidateCsrfRequest("PUT", true, true, false, "/api/admin/users/{id}", out _));

    [Fact]
    public void ShouldValidateCsrfRequest_AuthenticatedApiPostInDevelopment_ReturnsFalse() =>
        Assert.False(PipelineExtensions.ShouldValidateCsrfRequest("POST", true, true, true, ApiFeaturesPath, out _));

    [Fact]
    public void ShouldValidateCsrfRequest_AuthenticatedLogsEndpoint_ReturnsFalse() =>
        Assert.False(PipelineExtensions.ShouldValidateCsrfRequest("POST", true, true, false, ApiLogsPath, out _));

    [Fact]
    public void ShouldValidateCsrfRequest_PagePostInDevelopment_ReturnsTrue() =>
        Assert.True(PipelineExtensions.ShouldValidateCsrfRequest("POST", true, false, true, "/some-page", out _));

    // ── Anonymous state-changing API requests (defense-in-depth guard) ───

    [Theory]
    [InlineData("/api/signin")]
    [InlineData("/api/refresh")]
    [InlineData("/api/admin/authorized-signup")]
    [InlineData("/api/signup")]
    public void ShouldValidateCsrfRequest_AnonymousAllowedPath_NotRejected(string path)
    {
        Assert.False(PipelineExtensions.ShouldValidateCsrfRequest("POST", false, true, false, path, out var rejected));
        Assert.False(rejected);
    }

    [Fact]
    public void ShouldValidateCsrfRequest_AnonymousApiPost_FlaggedForRejection()
    {
        Assert.False(PipelineExtensions.ShouldValidateCsrfRequest("POST", false, true, false, ApiFeaturesPath, out var rejected));
        Assert.True(rejected);
    }

    [Fact]
    public void ShouldValidateCsrfRequest_AnonymousNonApiPost_NotFlaggedForRejection()
    {
        Assert.False(PipelineExtensions.ShouldValidateCsrfRequest("POST", false, false, true, "/some-page", out var rejected));
        Assert.False(rejected);
    }

    [Fact]
    public void ShouldValidateCsrfRequest_AnonymousApiGet_NotFlaggedForRejection()
    {
        Assert.False(PipelineExtensions.ShouldValidateCsrfRequest("GET", false, true, false, ApiFeaturesPath, out var rejected));
        Assert.False(rejected);
    }

    [Fact]
    public void IsAnonymousMutatingApiPath_AllowlistedPaths()
    {
        Assert.True(PipelineExtensions.IsAnonymousMutatingApiPath("/api/signin"));
        Assert.True(PipelineExtensions.IsAnonymousMutatingApiPath("/api/admin/authorized-signup"));
        Assert.False(PipelineExtensions.IsAnonymousMutatingApiPath("/api/features"));
        Assert.False(PipelineExtensions.IsAnonymousMutatingApiPath("/api/features/123"));
    }

    // ── Origin validation (login CSRF defense) ──────────────────────────

    [Fact]
    public void IsForeignOrigin_AbsentOrigin_Allowed()
    {
        // Non-browser clients (curl, native apps) send no Origin header.
        Assert.False(PipelineExtensions.IsForeignOrigin(null, "https://api.nars.dz", ["https://nars.dz"]));
        Assert.False(PipelineExtensions.IsForeignOrigin("", "https://api.nars.dz", ["https://nars.dz"]));
    }

    [Fact]
    public void IsForeignOrigin_SameOriginAllowed()
        => Assert.False(PipelineExtensions.IsForeignOrigin(
            "https://api.nars.dz", "https://api.nars.dz", []));

    [Theory]
    [InlineData("https://nars.dz", "https://NARS.DZ/")]
    [InlineData("https://api.nars.dz", "https://api.nars.dz")]
    public void IsForeignOrigin_ExplicitlyAllowedOrigin_MatchesCaseAndSlashInsensitive(
        string allowed, string origin)
        => Assert.False(PipelineExtensions.IsForeignOrigin(origin, "https://other.test", [allowed]));

    [Fact]
    public void IsForeignOrigin_UnknownCrossSiteOrigin_Rejected()
    {
        // A cross-site form POST from an attacker page always carries the
        // attacker's Origin — this is the login CSRF vector being blocked.
        Assert.True(PipelineExtensions.IsForeignOrigin(
            "https://evil.example", "https://api.nars.dz", ["https://nars.dz"]));
    }

    [Fact]
    public void IsForeignOrigin_LookalikeOrigin_Rejected()
        => Assert.True(PipelineExtensions.IsForeignOrigin(
            "https://api.nars.dz.evil.example", "https://api.nars.dz", ["https://nars.dz"]));
}
