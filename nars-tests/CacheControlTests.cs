using NarsApi.Infrastructure;
using Xunit;

namespace NarsApi.Tests;

public class CacheControlTests
{
    [Theory]
    [InlineData("index.html", "no-store, no-cache, must-revalidate")]
    [InlineData("login.html", "no-store, no-cache, must-revalidate")]
    [InlineData("assets/index-Dp0zqR50.js", "public, max-age=31536000, immutable")]
    [InlineData("assets/AdminDashboard-B0M4F_US.css", "public, max-age=31536000, immutable")]
    [InlineData("assets/rolldown-runtime-QTnfLwEv.js", "public, max-age=31536000, immutable")]
    [InlineData("assets/vendor-geoman-3EH5LiXU.css", "public, max-age=31536000, immutable")]
    public void ContentFingerprintedBundles_AreImmutable(string fileName, string expected)
    {
        Assert.Equal(expected, PipelineExtensions.CacheControlForStaticAsset(fileName));
    }

    [Theory]
    [InlineData("login.css")]
    [InlineData("app.js")]
    [InlineData("app.mjs")]
    [InlineData("app.css")]
    [InlineData("assets/index.js")]
    public void UnHashedAssets_AreRevalidated(string fileName)
    {
        Assert.Equal("public, no-cache", PipelineExtensions.CacheControlForStaticAsset(fileName));
    }

    [Theory]
    [InlineData("favicon.ico")]
    [InlineData("NARS.jpg")]
    [InlineData("tiles.svg")]
    [InlineData("assets/index-Dp0zqR50.js.map")]
    public void OtherAssets_WriteNoCacheControlHeader(string fileName)
    {
        Assert.Equal(string.Empty, PipelineExtensions.CacheControlForStaticAsset(fileName));
    }
}
