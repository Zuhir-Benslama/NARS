using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace NarsApi.Tests;

/// <summary>
/// Guards nars-api/wwwroot — the API's own copy of the SPA, served at /map —
/// from referencing bundle files that were never committed. A stale index.html
/// pointing at build assets missing locally is exactly the class of bug that
/// blank-screens the SPA after login: /map loads this copy while nginx serves
/// the nars-vite image, and a drift between them 404s the main bundle.
/// </summary>
public class WwwrootAssetSyncTests
{
    private static readonly Regex AssetRef = new(@"assets/([A-Za-z0-9._-]+)", RegexOptions.Compiled);

    private static string FindWwwroot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var wwwroot = Path.Combine(dir.FullName, "wwwroot");
            if (dir.EnumerateFiles("NarsApi.csproj", SearchOption.TopDirectoryOnly).Any()
                && Directory.Exists(wwwroot))
            {
                return wwwroot;
            }
        }

        throw new InvalidOperationException(
            "Could not locate nars-api/wwwroot from test base directory "
            + AppContext.BaseDirectory);
    }

    [Fact]
    public void Wwwroot_Index_ReferencesOnlyExistingBundles()
    {
        var wwwroot = FindWwwroot();
        var index = Path.Combine(wwwroot, "index.html");
        Assert.True(File.Exists(index), $"nars-api/wwwroot/index.html is missing at {index}");

        var assets = AssetRef.Matches(File.ReadAllText(index))
            .Select(m => m.Groups[1].Value)
            .Where(n => n.EndsWith(".js") || n.EndsWith(".css"))
            .ToHashSet();
        Assert.NotEmpty(assets);

        var missing = assets
            .Where(a => !File.Exists(Path.Combine(wwwroot, "assets", a)))
            .OrderBy(a => a)
            .ToList();
        Assert.Empty(missing);
    }
}
