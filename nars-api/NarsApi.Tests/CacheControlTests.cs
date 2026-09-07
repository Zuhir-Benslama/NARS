using NarsApi.Infrastructure;
using Xunit;

namespace NarsApi.Tests;

public class CacheControlTests
{
    // Synthetic Vite-style hashed names — the test exercises the fingerprint
    // regex, so stable fake hashes are used instead of real build outputs
    // (which change on every frontend rebuild and would need constant churn).

    [Theory]
    [InlineData("index.html", "no-store, no-cache, must-revalidate")]
    [InlineData("login.html", "no-store, no-cache, must-revalidate")]
    [InlineData("assets/index-abcd1234.js", "public, max-age=31536000, immutable")]
    [InlineData("assets/AdminDashboard-efgh5678.css", "public, max-age=31536000, immutable")]
    [InlineData("assets/rolldown-runtime-ijkl9012.js", "public, max-age=31536000, immutable")]
    [InlineData("assets/vendor-geoman-mnop3456.css", "public, max-age=31536000, immutable")]
    [InlineData("assets/InterVariable-pqrst6789.woff2", "public, max-age=31536000, immutable")]
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
    [InlineData("fonts/InterVariable.woff2")]
    public void UnHashedAssets_AreRevalidated(string fileName)
    {
        Assert.Equal("public, no-cache", PipelineExtensions.CacheControlForStaticAsset(fileName));
    }

    [Theory]
    [InlineData("favicon.ico")]
    [InlineData("NARS.jpg")]
    [InlineData("tiles.svg")]
    [InlineData("assets/index-abcd1234.js.map")]
    public void OtherAssets_WriteNoCacheControlHeader(string fileName)
    {
        Assert.Equal(string.Empty, PipelineExtensions.CacheControlForStaticAsset(fileName));
    }
}
