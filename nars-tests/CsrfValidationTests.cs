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
    public void ShouldValidateCsrf_SafeMethods_ReturnsFalse(string method) =>
        Assert.False(PipelineExtensions.ShouldValidateCsrf(method, true, true, false, ApiFeaturesPath));

    [Fact]
    public void ShouldValidateCsrf_AnonymousPost_ReturnsFalse() =>
        Assert.False(PipelineExtensions.ShouldValidateCsrf("POST", false, true, false, ApiFeaturesPath));

    [Fact]
    public void ShouldValidateCsrf_ApiPostInProduction_ReturnsTrue() =>
        Assert.True(PipelineExtensions.ShouldValidateCsrf("POST", true, true, false, ApiFeaturesPath));

    [Fact]
    public void ShouldValidateCsrf_ApiDeleteInProduction_ReturnsTrue() =>
        Assert.True(PipelineExtensions.ShouldValidateCsrf("DELETE", true, true, false, "/api/features/{id}"));

    [Fact]
    public void ShouldValidateCsrf_ApiPutInProduction_ReturnsTrue() =>
        Assert.True(PipelineExtensions.ShouldValidateCsrf("PUT", true, true, false, "/api/admin/users/{id}"));

    [Fact]
    public void ShouldValidateCsrf_ApiPostInDevelopment_ReturnsFalse() =>
        Assert.False(PipelineExtensions.ShouldValidateCsrf("POST", true, true, true, ApiFeaturesPath));

    [Fact]
    public void ShouldValidateCsrf_LogsEndpoint_ReturnsFalse() =>
        Assert.False(PipelineExtensions.ShouldValidateCsrf("POST", true, true, false, ApiLogsPath));

    [Fact]
    public void ShouldValidateCsrf_PagePostInDevelopment_ReturnsTrue() =>
        Assert.True(PipelineExtensions.ShouldValidateCsrf("POST", true, false, true, "/some-page"));

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
