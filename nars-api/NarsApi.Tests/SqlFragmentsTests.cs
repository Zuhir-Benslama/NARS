using NarsApi.Infrastructure;
using NarsApi.Models;
using Npgsql;
using Xunit;

namespace NarsApi.Tests;

public sealed class SqlFragmentsTests
{
    [Theory]
    [InlineData("plain search", "plain search")]
    [InlineData("100%", "100\\%")]
    [InlineData("a_b", "a\\_b")]
    [InlineData("50\\%", "50\\\\\\%")]
    [InlineData("back\\slash", "back\\\\slash")]
    public void EscapeLikeWildcards_Escapes_And_Preserves_Rest(string input, string expected)
    {
        Assert.Equal(expected, SqlFragments.EscapeLikeWildcards(input));
    }

    [Fact]
    public void UrbanAreaLayersSqlIn_ListsUrbanLayersAsQuotedSqlInValues()
    {
        var layers = FeatureTypes.AreaLayers.Urban.Select(l => $"'{l}'");
        Assert.Equal(string.Join(", ", layers), SqlFragments.UrbanAreaLayersSqlIn);
    }

    [Theory]
    [InlineData("f")]
    [InlineData("table1")]
    [InlineData("alias_2")]
    public void PolygonFromDataWithAlias_SubstitutesAlias(string alias)
    {
        var sql = SqlFragments.PolygonFromDataWithAlias(alias);
        Assert.Contains($"{alias}.data", sql);
        Assert.Contains("ST_MakeValid", sql);
    }

    [Theory]
    [InlineData("f")]
    [InlineData("roads_alias")]
    public void LineStringFromDataWithAlias_SubstitutesAlias(string alias)
    {
        var sql = SqlFragments.LineStringFromDataWithAlias(alias);
        Assert.Contains($"{alias}.data", sql);
        Assert.Contains("'LineString'", sql);
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("has;sql")]
    [InlineData("drop table x")]
    public void WithAlias_RejectsInjectionStyleAliases(string alias)
    {
        Assert.Throws<System.ArgumentException>(() => SqlFragments.PolygonFromDataWithAlias(alias));
        Assert.Throws<System.ArgumentException>(() => SqlFragments.LineStringFromDataWithAlias(alias));
    }

    [Fact]
    public void DefaultFragments_UseDefaultAliasF()
    {
        Assert.Equal(SqlFragments.PolygonFromDataWithAlias("f"), SqlFragments.PolygonFromData);
        Assert.Equal(SqlFragments.LineStringFromDataWithAlias("f"), SqlFragments.LineStringFromData);
    }

    [Fact]
    public void AddParam_SetsNameValueAndAddsToCommand()
    {
        using var cmd = new NpgsqlCommand();
        var param = SqlFragments.AddParam(cmd, "@uid", 42);
        Assert.Equal("@uid", param.ParameterName);
        Assert.Equal(42, param.Value);
        Assert.Same(param, cmd.Parameters["@uid"]);
    }

    [Fact]
    public void AddParam_WithNullValue_AddsNullParameter()
    {
        using var cmd = new NpgsqlCommand();
        var param = SqlFragments.AddParam(cmd, "@maybe", null!);
        Assert.Equal("@maybe", param.ParameterName);
        Assert.Same(param, cmd.Parameters["@maybe"]);
    }
}
