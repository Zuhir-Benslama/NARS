using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests.Service;

[Collection(PostgreSqlCollection.CollectionName)]
[Trait("Category", "Service")]
public class EntranceQueryServiceTests(NarsDatabaseFixture fixture) : ServiceTestBase(fixture)
{
    private Guid _userId;

    protected override async Task SeedAsync()
    {
        await SeedData.SeedBasicLocationsAsync(Db);
        var user = await SeedData.CreateUserAsync(Db, UserRoles.FieldWorker, communeId: 1, name: "Entrance Query Test User");
        _userId = user.Id;
    }

    private EntranceQueryService CreateService() => new(Fixture.CreateDbContextFactory());

    [Fact]
    public async Task GetUsedEntranceNumbersAsync_IgnoresNullIslandRows()
    {
        const string coords = """{"coordinates":[{"lat":36.4,"lng":2.9},{"lat":36.4,"lng":3.1}]}""";
        var roadId = await TestData.AddRoadAsync(Db, _userId, coords, registerInFeatureRegistry: true);
        var entranceService = new EntranceService(Fixture.CreateDbContextFactory());

        await entranceService.CreateEntranceAsync(
            roadId, _userId, "Ghost",
            """{"lat":0,"lng":0,"coordinates":[{"lat":0,"lng":0}],"side":"right","entranceNumber":2}""");
        await entranceService.CreateEntranceAsync(
            roadId, _userId, "Real",
            """{"lat":36.399,"lng":3.0,"side":"right","entranceNumber":4}""");

        var used = await CreateService().GetUsedEntranceNumbersAsync(_userId, roadId, "right");

        Assert.Equal(new[] { 4 }, used.Order());
    }
}
