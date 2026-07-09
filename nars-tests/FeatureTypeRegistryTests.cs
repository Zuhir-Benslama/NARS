using static NarsApi.Tests.TestData;
using Xunit;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Tests;

public class FeatureTypeRegistryTests
{
    [Fact]
    public void GetAllTypes_ReturnsAllExpectedTypes()
    {
        var types = FeatureTypeRegistry.GetAllTypes();

        Assert.Contains(FeatureTypes.Area, types);
        Assert.Contains(FeatureTypes.Road, types);
        Assert.Contains(FeatureTypes.District, types);
        Assert.Contains(FeatureTypes.HouseEntrance, types);
        Assert.Contains(FeatureTypes.PublicBuilding, types);
        Assert.Contains(FeatureTypes.PublicSpace, types);
        Assert.Contains(FeatureTypes.CityCenter, types);
        Assert.Contains(FeatureTypes.NamingPanel, types);
        Assert.Equal(8, types.Count);
    }

    [Fact]
    public void GetDescriptor_KnownType_ReturnsDescriptor()
    {
        var descriptor = FeatureTypeRegistry.GetDescriptor(FeatureTypes.Area);
        Assert.NotNull(descriptor);
        Assert.Equal(FeatureTypes.Area, descriptor.Type);
        Assert.Equal(typeof(Area), descriptor.EntityType);
    }

    [Fact]
    public void GetDescriptor_UnknownType_ReturnsNull()
    {
        var descriptor = FeatureTypeRegistry.GetDescriptor("nonexistent");
        Assert.Null(descriptor);
    }

    [Fact]
    public void CreateEntity_ValidType_ReturnsEntity()
    {
        var id = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var entity = FeatureTypeRegistry.CreateEntity(
            FeatureTypes.Road, id, userId, "street", "Test Road", "{}", FixedUtcNow);

        Assert.NotNull(entity);
        Assert.IsType<Road>(entity);
        Assert.Equal(id, entity.Id);
        Assert.Equal(userId, entity.UserId);
        Assert.Equal("street", entity.Layer);
        Assert.Equal("Test Road", entity.Label);
        Assert.Equal("{}", entity.Data);
        Assert.True(entity.CreatedAt > DateTime.MinValue);
    }

    [Fact]
    public void CreateEntity_HouseEntrance_ReturnsHouseEntrance()
    {
        var entity = FeatureTypeRegistry.CreateEntity(
            FeatureTypes.HouseEntrance, Guid.NewGuid(), Guid.NewGuid(),
            "main_entrance", "Test Entrance", "{}", FixedUtcNow);

        Assert.NotNull(entity);
        Assert.IsType<HouseEntrance>(entity);
    }

    [Fact]
    public void CreateEntity_UnknownType_ReturnsNull()
    {
        var entity = FeatureTypeRegistry.CreateEntity(
            "unknown", Guid.NewGuid(), Guid.NewGuid(), "default", "Test", "{}", FixedUtcNow);
        Assert.Null(entity);
    }

    [Theory]
    [InlineData(FeatureTypes.Area)]
    [InlineData(FeatureTypes.District)]
    [InlineData(FeatureTypes.CityCenter)]
    [InlineData(FeatureTypes.Road)]
    [InlineData(FeatureTypes.HouseEntrance)]
    [InlineData(FeatureTypes.PublicBuilding)]
    [InlineData(FeatureTypes.PublicSpace)]
    [InlineData(FeatureTypes.NamingPanel)]
    public void CreateEntity_AllTypes_ReturnCorrectEntityType(string type)
    {
        var entity = FeatureTypeRegistry.CreateEntity(
            type, Guid.NewGuid(), Guid.NewGuid(), "default", "Test", "{}", FixedUtcNow);

        Assert.NotNull(entity);

        var expectedType = type switch
        {
            FeatureTypes.Area => typeof(Area),
            FeatureTypes.District => typeof(District),
            FeatureTypes.CityCenter => typeof(CityCenter),
            FeatureTypes.Road => typeof(Road),
            FeatureTypes.HouseEntrance => typeof(HouseEntrance),
            FeatureTypes.PublicBuilding => typeof(PublicBuilding),
            FeatureTypes.PublicSpace => typeof(PublicSpace),
            FeatureTypes.NamingPanel => typeof(NamingPanel),
            _ => throw new InvalidOperationException(),
        };

        Assert.Equal(expectedType, entity.GetType());
    }
}
