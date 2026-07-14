using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using NarsApi.Controllers;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Services;
using Xunit;

namespace NarsApi.Tests.Integration;

[Collection(PostgreSqlCollection.CollectionName)]
public class LocationsControllerIntegrationTests : IAsyncLifetime
{
    private readonly NarsDatabaseFixture _fixture;
    private AppDbContext _db = null!;

    public LocationsControllerIntegrationTests(NarsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _db = _fixture.CreateDbContext();
        await SeedData.SeedAdminLocationsAsync(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _fixture.CleanTablesAsync();
    }

    private LocationsController CreateController()
    {
        return new LocationsController(
            _db,
            Options.Create(new LocationsOptions()),
            Mock.Of<IBoundaryService>());
    }

    [Fact]
    public async Task SearchWilayas_ByName_ReturnsMatches()
    {
        var controller = CreateController();
        var result = await controller.GetWilayas(search: "Alger");

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<PagedResponse<WilayaItem>>(ok.Value);
        Assert.Equal(1, resp.Total);
        Assert.Single(resp.Items);
        Assert.Equal("Alger", resp.Items[0].NameFr);
    }

    [Fact]
    public async Task SearchWilayas_ByArabicName_ReturnsMatches()
    {
        var controller = CreateController();
        var result = await controller.GetWilayas(search: "الجزائر");

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<PagedResponse<WilayaItem>>(ok.Value);
        Assert.Equal(1, resp.Total);
        Assert.Single(resp.Items);
        Assert.Equal("الجزائر", resp.Items[0].NameAr);
    }

    [Fact]
    public async Task SearchWilayas_PartialName_ReturnsMatches()
    {
        var controller = CreateController();
        var result = await controller.GetWilayas(search: "Bli");

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<PagedResponse<WilayaItem>>(ok.Value);
        Assert.Equal(1, resp.Total);
        Assert.Single(resp.Items);
        Assert.Equal("Blida", resp.Items[0].NameFr);
    }

    [Fact]
    public async Task SearchWilayas_NoMatch_ReturnsEmpty()
    {
        var controller = CreateController();
        var result = await controller.GetWilayas(search: "Nonexistent");

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<PagedResponse<WilayaItem>>(ok.Value);
        Assert.Equal(0, resp.Total);
        Assert.Empty(resp.Items);
    }

    [Fact]
    public async Task SearchWilayas_EmptySearch_ReturnsAll()
    {
        var controller = CreateController();
        var result = await controller.GetWilayas(search: "");

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<PagedResponse<WilayaItem>>(ok.Value);
        Assert.Equal(2, resp.Total);
        Assert.Equal(2, resp.Items.Count);
    }
}
