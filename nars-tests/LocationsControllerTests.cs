using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using NarsApi.Controllers;
using NarsApi.Infrastructure;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Models;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests;

public class LocationsControllerTests
{
    private static LocationsController CreateController(
        ILocationSearchService? searchService = null,
        IBoundaryService? boundaryService = null,
        ILocationQueryService? locationQuery = null,
        IWebHostEnvironment? environment = null) =>
        new(
            Options.Create(new LocationsOptions()),
            boundaryService ?? Mock.Of<IBoundaryService>(),
            locationQuery ?? Mock.Of<ILocationQueryService>(),
            searchService ?? Mock.Of<ILocationSearchService>(),
            environment ?? Mock.Of<IWebHostEnvironment>(e => e.EnvironmentName == "Development"));

    private static ILocationQueryService CreateLocationQueryMock(AppDbContext db)
    {
        var mock = new Mock<ILocationQueryService>();
        mock.Setup(s => s.GetCommuneByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int communeId, CancellationToken _) =>
                db.Communes.FirstOrDefault(c => c.CommuneId == communeId));
        return mock.Object;
    }

    private static (AppDbContext db, ILocationSearchService search) CreateDbWithSearch(string name)
    {
        var (db, factory) = CreateInMemoryDbPair($"LocationsTest_{name}");
        return (db, new LocationSearchService(factory));
    }

    private static async Task SeedWilayas(AppDbContext db)
    {
        db.Wilayas.AddRange(
            new Models.Wilaya { WilayaId = 1, WilayaFr = "Alger", WilayaAr = "الجزائر" },
            new Models.Wilaya { WilayaId = 2, WilayaFr = "Oran", WilayaAr = "وهران" },
            new Models.Wilaya { WilayaId = 3, WilayaFr = "Constantine", WilayaAr = "قسنطينة" }
        );
        await db.SaveChangesAsync();
    }

    private static async Task SeedDairas(AppDbContext db, int wilayaId)
    {
        // Use unique IDs per wilaya
        var offset = wilayaId * 100;
        db.Dairas.AddRange(
            new Models.Daira { DairaId = offset + 1, WilayaId = wilayaId, DairaFr = "Daira A", DairaAr = "دائرة أ" },
            new Models.Daira { DairaId = offset + 2, WilayaId = wilayaId, DairaFr = "Daira B", DairaAr = "دائرة ب" }
        );
        await db.SaveChangesAsync();
    }

    private static async Task SeedCommunes(AppDbContext db, int dairaId)
    {
        // Use unique IDs per daira
        var offset = dairaId * 100;
        db.Communes.AddRange(
            new Models.Commune { CommuneId = offset + 1, DairaId = dairaId, CommuneFr = "Commune X", CommuneAr = "بلدية X" },
            new Models.Commune { CommuneId = offset + 2, DairaId = dairaId, CommuneFr = "Commune Y", CommuneAr = "بلدية Y" }
        );
        await db.SaveChangesAsync();
    }

    // ── GET /api/wilayas ──────────────────────────────────────────────────

    [Fact]
    public async Task GetWilayas_NoSearch_ReturnsAllWilayas()
    {
        var (db, search) = CreateDbWithSearch(nameof(GetWilayas_NoSearch_ReturnsAllWilayas));
        using (db)
        {
            await SeedWilayas(db);
            var ctrl = CreateController(searchService: search);

            var result = await ctrl.GetWilayas(search: "", skip: 0, take: 100);

            var ok = Assert.IsType<OkObjectResult>(result);
            var resp = Assert.IsType<PagedResponse<WilayaItem>>(ok.Value);
            Assert.Equal(3, resp.Total);
            Assert.Equal(3, resp.Items.Count);
        }
    }

    [Fact]
    public async Task GetWilayas_SearchTooLong_Returns400()
    {
        var ctrl = CreateController();

        var result = await ctrl.GetWilayas(search: new string('x', 201), skip: 0, take: 100);

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task GetWilayas_TakeClampedTo500()
    {
        var (db, search) = CreateDbWithSearch(nameof(GetWilayas_TakeClampedTo500));
        using (db)
        {
            await SeedWilayas(db);
            var ctrl = CreateController(searchService: search);

            var result = await ctrl.GetWilayas(search: "", skip: 0, take: 1000);

            var ok = Assert.IsType<OkObjectResult>(result);
            var resp = Assert.IsType<PagedResponse<WilayaItem>>(ok.Value);
            Assert.Equal(3, resp.Items.Count);
            Assert.Equal(500, resp.Take); // clamped to max page size
        }
    }

    [Fact]
    public async Task GetWilayas_NoSearchSkip0Take500_QueriesDbDirectly()
    {
        var (db, search) = CreateDbWithSearch(nameof(GetWilayas_NoSearchSkip0Take500_QueriesDbDirectly));
        using (db)
        {
            await SeedWilayas(db);
            var ctrl = CreateController(searchService: search);

            var result1 = await ctrl.GetWilayas(search: "", skip: 0, take: 500);
            var ok1 = Assert.IsType<OkObjectResult>(result1);
            var resp1 = Assert.IsType<PagedResponse<WilayaItem>>(ok1.Value);
            Assert.Equal(3, resp1.Total);

            // Add a new wilaya to DB (should appear — no caching)
            db.Wilayas.Add(new Models.Wilaya { WilayaId = 4, WilayaFr = "New" });
            await db.SaveChangesAsync();

            var result2 = await ctrl.GetWilayas(search: "", skip: 0, take: 500);
            var ok2 = Assert.IsType<OkObjectResult>(result2);
            var resp2 = Assert.IsType<PagedResponse<WilayaItem>>(ok2.Value);
            Assert.Equal(4, resp2.Total); // No caching — queries DB directly
        }
    }

    // ── GET /api/dairas ───────────────────────────────────────────────────

    [Fact]
    public async Task GetDairas_NoWilayaId_Returns400()
    {
        var ctrl = CreateController();

        var result = await ctrl.GetDairas(wilaya_id: 0);

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task GetDairas_ValidWilaya_ReturnsDairas()
    {
        var (db, search) = CreateDbWithSearch(nameof(GetDairas_ValidWilaya_ReturnsDairas));
        using (db)
        {
            await SeedDairas(db, wilayaId: 1);
            await SeedDairas(db, wilayaId: 2);
            var ctrl = CreateController(searchService: search);

            var result = await ctrl.GetDairas(wilaya_id: 1);

            var ok = Assert.IsType<OkObjectResult>(result);
            var resp = Assert.IsType<PagedResponse<DairaItem>>(ok.Value);
            Assert.Equal(2, resp.Total);
        }
    }

    // ── GET /api/communes ─────────────────────────────────────────────────

    [Fact]
    public async Task GetCommunes_NoDairaId_Returns400()
    {
        var ctrl = CreateController();

        var result = await ctrl.GetCommunes(daira_id: null);

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task GetCommunes_DairaIdZero_Returns400()
    {
        var ctrl = CreateController();

        var result = await ctrl.GetCommunes(daira_id: 0);

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task GetCommunes_ValidDaira_ReturnsCommunes()
    {
        var (db, search) = CreateDbWithSearch(nameof(GetCommunes_ValidDaira_ReturnsCommunes));
        using (db)
        {
            await SeedCommunes(db, dairaId: 10);
            var ctrl = CreateController(searchService: search);

            var result = await ctrl.GetCommunes(daira_id: 10);

            var ok = Assert.IsType<OkObjectResult>(result);
            var resp = Assert.IsType<PagedResponse<CommuneItem>>(ok.Value);
            Assert.Equal(2, resp.Total);
        }
    }

    // ── GET /api/commune/{id}/boundary ────────────────────────────────────

    [Fact]
    public async Task GetCommuneBoundary_Found_Returns200()
    {
        var (db, _) = CreateDbWithSearch(nameof(GetCommuneBoundary_Found_Returns200));
        using (db)
        {
            await SeedCommunes(db, dairaId: 10);

            var boundaryMock = new Mock<IBoundaryService>();
            boundaryMock.Setup(b => b.GetBoundaryGeoJsonAsync(1001, It.IsAny<CancellationToken>()))
                .ReturnsAsync("{\"type\":\"Polygon\"}");

            var locationQueryMock = CreateLocationQueryMock(db);
            var ctrl = CreateController(boundaryService: boundaryMock.Object, locationQuery: locationQueryMock);

            var result = await ctrl.GetCommuneBoundary(1001);

            Assert.IsType<OkObjectResult>(result);
        }
    }

    [Fact]
    public async Task GetCommuneBoundary_NotFound_Returns404()
    {
        var boundaryMock = new Mock<IBoundaryService>();
        boundaryMock.Setup(b => b.GetBoundaryGeoJsonAsync(TestData.NonExistentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var ctrl = CreateController(boundaryService: boundaryMock.Object);

        var result = await ctrl.GetCommuneBoundary(TestData.NonExistentId);

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, objResult.StatusCode);
    }

    // ── GET /api/commune/{id}/boundary-debug ──────────────────────────────

    [Fact]
    public async Task DebugCommuneBoundary_NonDevEnv_Returns404()
    {
        var ctrl = CreateController(environment: Mock.Of<IWebHostEnvironment>(e => e.EnvironmentName == "Production"));

        var result = await ctrl.DebugCommuneBoundary(100);

        Assert.IsType<NotFoundResult>(result);
    }

    // ── Edge cases ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetWilayas_NegativeSkip_ClampsToZero()
    {
        var (db, search) = CreateDbWithSearch(nameof(GetWilayas_NegativeSkip_ClampsToZero));
        using (db)
        {
            await SeedWilayas(db);
            var ctrl = CreateController(searchService: search);

            var result = await ctrl.GetWilayas(skip: -1, take: 10);

            var ok = Assert.IsType<OkObjectResult>(result);
            var response = Assert.IsType<PagedResponse<WilayaItem>>(ok.Value);
            Assert.Equal(3, response.Items.Count);
            Assert.Equal(3, response.Total);
            Assert.Equal(0, response.Skip);
        }
    }

    [Fact]
    public async Task GetWilayas_SmallTake_ReturnsOk()
    {
        var (db, search) = CreateDbWithSearch(nameof(GetWilayas_SmallTake_ReturnsOk));
        using (db)
        {
            await SeedWilayas(db);
            var ctrl = CreateController(searchService: search);

            var result = await ctrl.GetWilayas(skip: 0, take: 1);

            var ok = Assert.IsType<OkObjectResult>(result);
            var response = Assert.IsType<PagedResponse<WilayaItem>>(ok.Value);
            var item = Assert.Single(response.Items);
            Assert.Equal(3, response.Total);
            Assert.Equal("Alger", item.NameFr);
        }
    }

}
