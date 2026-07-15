using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using NarsApi.Controllers;
using NarsApi.Infrastructure;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests;

public class LocationsControllerTests
{
    private static LocationsController CreateController(
        AppDbContext db,
        IBoundaryService? boundaryService = null) =>
        new(
            db,
            Options.Create(new LocationsOptions()),
            boundaryService ?? Mock.Of<IBoundaryService>(),
            Mock.Of<ILocationQueryService>());

    private static AppDbContext CreateDb(string name) => CreateInMemoryDb($"LocationsTest_{name}");

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
        var db = CreateDb(nameof(GetWilayas_NoSearch_ReturnsAllWilayas));
        await SeedWilayas(db);
        var ctrl = CreateController(db);

        var result = await ctrl.GetWilayas(search: "", skip: 0, take: 100);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<PagedResponse<WilayaItem>>(ok.Value);
        Assert.Equal(3, resp.Total);
        Assert.Equal(3, resp.Items.Count);
    }

    [Fact]
    public async Task GetWilayas_SearchTooLong_Returns400()
    {
        var db = CreateDb(nameof(GetWilayas_SearchTooLong_Returns400));
        await SeedWilayas(db);
        var ctrl = CreateController(db);

        var result = await ctrl.GetWilayas(search: new string('x', 201), skip: 0, take: 100);

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task GetWilayas_TakeClampedTo500()
    {
        var db = CreateDb(nameof(GetWilayas_TakeClampedTo500));
        await SeedWilayas(db);
        var ctrl = CreateController(db);

        var result = await ctrl.GetWilayas(search: "", skip: 0, take: 1000);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<PagedResponse<WilayaItem>>(ok.Value);
        Assert.Equal(3, resp.Items.Count);
        Assert.Equal(500, resp.Take); // clamped to max page size
    }

    [Fact]
    public async Task GetWilayas_NoSearchSkip0Take500_QueriesDbDirectly()
    {
        var db = CreateDb(nameof(GetWilayas_NoSearchSkip0Take500_QueriesDbDirectly));
        await SeedWilayas(db);
        var ctrl = CreateController(db);

        var result1 = await ctrl.GetWilayas(search: "", skip: 0, take: 500);
        var ok1 = Assert.IsType<OkObjectResult>(result1);
        var resp1 = Assert.IsType<PagedResponse<WilayaItem>>(ok1.Value);
        Assert.Equal(3, resp1.Total);

        // Add a new wilaya to DB (should NOT appear because cached)
        db.Wilayas.Add(new Models.Wilaya { WilayaId = 4, WilayaFr = "New" });
        await db.SaveChangesAsync();

        var result2 = await ctrl.GetWilayas(search: "", skip: 0, take: 500);
        var ok2 = Assert.IsType<OkObjectResult>(result2);
        var resp2 = Assert.IsType<PagedResponse<WilayaItem>>(ok2.Value);
        Assert.Equal(4, resp2.Total); // No caching — queries DB directly
    }

    // ── GET /api/dairas ───────────────────────────────────────────────────

    [Fact]
    public async Task GetDairas_NoWilayaId_Returns400()
    {
        var db = CreateDb(nameof(GetDairas_NoWilayaId_Returns400));
        var ctrl = CreateController(db);

        var result = await ctrl.GetDairas(wilaya_id: 0);

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task GetDairas_ValidWilaya_ReturnsDairas()
    {
        var db = CreateDb(nameof(GetDairas_ValidWilaya_ReturnsDairas));
        await SeedDairas(db, wilayaId: 1);
        await SeedDairas(db, wilayaId: 2);
        var ctrl = CreateController(db);

        var result = await ctrl.GetDairas(wilaya_id: 1);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<PagedResponse<DairaItem>>(ok.Value);
        Assert.Equal(2, resp.Total);
    }

    // ── GET /api/communes ─────────────────────────────────────────────────

    [Fact]
    public async Task GetCommunes_NoDairaId_Returns400()
    {
        var db = CreateDb(nameof(GetCommunes_NoDairaId_Returns400));
        var ctrl = CreateController(db);

        var result = await ctrl.GetCommunes(daira_id: null);

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task GetCommunes_DairaIdZero_Returns400()
    {
        var db = CreateDb(nameof(GetCommunes_DairaIdZero_Returns400));
        var ctrl = CreateController(db);

        var result = await ctrl.GetCommunes(daira_id: 0);

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task GetCommunes_ValidDaira_ReturnsCommunes()
    {
        var db = CreateDb(nameof(GetCommunes_ValidDaira_ReturnsCommunes));
        await SeedCommunes(db, dairaId: 10);
        var ctrl = CreateController(db);

        var result = await ctrl.GetCommunes(daira_id: 10);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<PagedResponse<CommuneItem>>(ok.Value);
        Assert.Equal(2, resp.Total);
    }

    // ── GET /api/commune/{id}/boundary ────────────────────────────────────

    [Fact]
    public async Task GetCommuneBoundary_Found_Returns200()
    {
        var db = CreateDb(nameof(GetCommuneBoundary_Found_Returns200));
        await SeedCommunes(db, dairaId: 10);

        var boundaryMock = new Mock<IBoundaryService>();
        boundaryMock.Setup(b => b.GetBoundaryGeoJsonAsync(1001, It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"type\":\"Polygon\"}");

        var ctrl = CreateController(db, boundaryService: boundaryMock.Object);

        var result = await ctrl.GetCommuneBoundary(1001);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetCommuneBoundary_NotFound_Returns404()
    {
        var db = CreateDb(nameof(GetCommuneBoundary_NotFound_Returns404));
        var boundaryMock = new Mock<IBoundaryService>();
        boundaryMock.Setup(b => b.GetBoundaryGeoJsonAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var ctrl = CreateController(db, boundaryService: boundaryMock.Object);

        var result = await ctrl.GetCommuneBoundary(999);

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, objResult.StatusCode);
    }

    // ── GET /api/commune/{id}/boundary-debug ──────────────────────────────

    [Fact]
    public async Task DebugCommuneBoundary_NonDevEnv_Returns404()
    {
        var db = CreateDb(nameof(DebugCommuneBoundary_NonDevEnv_Returns404));
        var ctrl = CreateController(db);
        var env = Mock.Of<Microsoft.Extensions.Hosting.IHostEnvironment>(e => e.EnvironmentName == "Production");

        var result = await ctrl.DebugCommuneBoundary(100, env);

        Assert.IsType<NotFoundResult>(result);
    }

    // ── Edge cases ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetWilayas_NegativeSkip_ReturnsOk()
    {
        var db = CreateDb(nameof(GetWilayas_NegativeSkip_ReturnsOk));
        var ctrl = CreateController(db);

        var result = await ctrl.GetWilayas(skip: -1, take: 10);

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task GetWilayas_SmallTake_ReturnsOk()
    {
        var db = CreateDb(nameof(GetWilayas_SmallTake_ReturnsOk));
        await SeedWilayas(db);
        var ctrl = CreateController(db);

        var result = await ctrl.GetWilayas(skip: 0, take: 1);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

}
