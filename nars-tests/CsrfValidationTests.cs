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
}
