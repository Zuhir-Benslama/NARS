using NarsApi.Models;
using Xunit;

namespace NarsApi.Tests;

public sealed class FeatureTypesTests
{
    [Theory]
    [InlineData(FeatureTypes.Area, FeatureTypes.AreaLayers.CentralUrban)]
    [InlineData(FeatureTypes.Road, FeatureTypes.RoadLayers.Boulevard)]
    [InlineData(FeatureTypes.District, FeatureTypes.DistrictLayers.IndustryZone)]
    [InlineData(FeatureTypes.HouseEntrance, FeatureTypes.HouseEntranceLayers.Main)]
    [InlineData(FeatureTypes.PublicBuilding, FeatureTypes.PublicBuildingLayers.School)]
    [InlineData(FeatureTypes.PublicSpace, FeatureTypes.PublicSpaceLayers.Square)]
    [InlineData(FeatureTypes.CityCenter, FeatureTypes.CityCenterLayers.Default)]
    [InlineData(FeatureTypes.NamingPanel, FeatureTypes.NamingPanelLayers.Default)]
    public void IsValidLayer_ValidTypeAndLayer_ReturnsTrue(string type, string layer)
    {
        Assert.True(FeatureTypes.IsValidLayer(type, layer));
    }

    [Theory]
    [InlineData(FeatureTypes.Area, FeatureTypes.RoadLayers.Avenue)]
    [InlineData(FeatureTypes.Road, FeatureTypes.AreaLayers.Scattered)]
    [InlineData(FeatureTypes.PublicBuilding, FeatureTypes.PublicSpaceLayers.Garden)]
    [InlineData(FeatureTypes.HouseEntrance, FeatureTypes.DistrictLayers.UrbanPole)]
    [InlineData(FeatureTypes.CityCenter, FeatureTypes.PublicBuildingLayers.Mosque)]
    [InlineData(FeatureTypes.NamingPanel, FeatureTypes.AreaLayers.CentralUrban)]
    public void IsValidLayer_TypeMismatchedLayer_ReturnsFalse(string type, string layer)
    {
        Assert.False(FeatureTypes.IsValidLayer(type, layer));
    }

    [Fact]
    public void IsValidLayer_UnknownType_ReturnsFalse()
    {
        Assert.False(FeatureTypes.IsValidLayer("not_a_type", "anything"));
        Assert.False(FeatureTypes.IsValidLayer("", ""));
    }

    [Fact]
    public void AllTypes_ContainsEveryTopLevelKey()
    {
        Assert.Equal(8, FeatureTypes.AllTypes.Count);
        var expected = new[]
        {
            FeatureTypes.Area, FeatureTypes.Road, FeatureTypes.District,
            FeatureTypes.HouseEntrance, FeatureTypes.PublicBuilding,
            FeatureTypes.PublicSpace, FeatureTypes.CityCenter, FeatureTypes.NamingPanel,
        };
        Assert.Equal(expected.OrderBy(t => t), FeatureTypes.AllTypes.OrderBy(t => t));
    }

    [Fact]
    public void LayerSets_AreDisjointAcrossTypes()
    {
        Assert.Empty(FeatureTypes.AreaLayers.All.Intersect(FeatureTypes.RoadLayers.All));
        Assert.Empty(FeatureTypes.DistrictLayers.All.Intersect(FeatureTypes.PublicBuildingLayers.All));
        Assert.Equal(2, FeatureTypes.AreaLayers.Urban.Count);
        Assert.Contains(FeatureTypes.AreaLayers.CentralUrban, FeatureTypes.AreaLayers.Urban);
        Assert.Contains(FeatureTypes.AreaLayers.SecondaryUrban, FeatureTypes.AreaLayers.Urban);
    }

    [Fact]
    public void RoadHierarchy_GroupingCoversAllLayers()
    {
        Assert.True(FeatureTypes.RoadLayers.All.SetEquals(
            FeatureTypes.RoadLayers.Primary
                .Concat(FeatureTypes.RoadLayers.Secondary)
                .Concat(FeatureTypes.RoadLayers.Tertiary)));
    }

    [Fact]
    public void PublicBuildingLayers_IncludesLegacyAndModernKeys()
    {
        Assert.Contains(FeatureTypes.PublicBuildingLayers.Default, FeatureTypes.PublicBuildingLayers.All);
        Assert.Contains(FeatureTypes.PublicBuildingLayers.School, FeatureTypes.PublicBuildingLayers.All);
        Assert.Contains(FeatureTypes.PublicBuildingLayers.Mosque, FeatureTypes.PublicBuildingLayers.All);
        Assert.Contains(FeatureTypes.PublicBuildingLayers.University, FeatureTypes.PublicBuildingLayers.All);
        Assert.Equal(44, FeatureTypes.PublicBuildingLayers.All.Count);
    }
}
