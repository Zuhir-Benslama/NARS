using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using NarsApi.Infrastructure;
using Xunit;

namespace NarsApi.Tests;

public class JsonHelperTests
{
    [Fact]
    public void DeserializeSafe_ValidJson_ReturnsNode()
    {
        var node = JsonHelper.DeserializeSafe("{\"a\":1}");
        Assert.NotNull(node);
        Assert.Equal(1, node!["a"]!.GetValue<int>());
    }

    [Fact]
    public void DeserializeSafe_ValidJsonArray_ReturnsNode()
    {
        var node = JsonHelper.DeserializeSafe("[1,2,3]");
        Assert.NotNull(node);
        Assert.True(node!.AsArray().Count == 3);
    }

    [Fact]
    public void DeserializeSafe_InvalidJson_ReturnsEmptyObject()
    {
        var node = JsonHelper.DeserializeSafe("not json {{{");
        Assert.NotNull(node);
        Assert.Equal(JsonValueKind.Object, node!.GetValueKind());
        var obj = node.AsObject();
        Assert.Empty(obj);
    }

    [Fact]
    public void DeserializeSafe_InvalidJsonWithLogger_ReturnsEmptyObjectAndNoThrow()
    {
        var node = JsonHelper.DeserializeSafe("!!!", NullLogger.Instance);
        Assert.NotNull(node);
        Assert.Empty(node!.AsObject());
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("true")]
    public void DeserializeSafe_EdgeValues_NoThrow(string json)
    {
        _ = JsonHelper.DeserializeSafe(json);
    }
}
