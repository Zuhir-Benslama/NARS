using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests;

public class CommuneScopeServiceTests
{
    private static CommuneScopeService CreateService(AppDbContext db) => new(db);

    private static async Task SeedLocationsAsync(AppDbContext db)
    {
        // Wilaya 1 -> daira 10 -> commune 100; wilaya 2 -> daira 11 -> commune 101.
        await SeedData.SeedAdminLocationsAsync(db);
    }

    [Theory]
    [InlineData(CommuneId100)]
    [InlineData(CommuneId101)]
    [InlineData(NonExistentId)]
    public async Task CanAccessCommuneAsync_NationalAdmin_CanAccessAnyCommune(int communeId)
    {
        using var db = CreateInMemoryDb("ScopeNational");
        await SeedLocationsAsync(db);
        var svc = CreateService(db);

        var allowed = await svc.CanAccessCommuneAsync(UserRoles.NationalAdmin, null, null, null, communeId);

        Assert.True(allowed);
    }

    [Theory]
    [InlineData(CommuneId100, true)]
    [InlineData(CommuneId101, false)]
    public async Task CanAccessCommuneAsync_CommuneScopedRoles_OnlyTheirOwnCommune(int communeId, bool expected)
    {
        using var db = CreateInMemoryDb("ScopeCommune");
        await SeedLocationsAsync(db);
        var svc = CreateService(db);

        foreach (var role in new[] { UserRoles.CommuneUser, UserRoles.FieldWorker })
        {
            var allowed = await svc.CanAccessCommuneAsync(role, CommuneId100, null, null, communeId);
            Assert.Equal(expected, allowed);
        }
    }

    [Fact]
    public async Task CanAccessCommuneAsync_CommuneScopedRole_NullCommuneClaim_RejectsAll()
    {
        using var db = CreateInMemoryDb("ScopeCommuneNullClaim");
        await SeedLocationsAsync(db);
        var svc = CreateService(db);

        var allowed = await svc.CanAccessCommuneAsync(UserRoles.CommuneUser, null, null, null, CommuneId100);

        Assert.False(allowed);
    }

    [Theory]
    [InlineData(CommuneId100, true)]
    [InlineData(CommuneId101, false)]
    public async Task CanAccessCommuneAsync_DairaAdmin_OnlyCommunesInOwnDaira(int communeId, bool expected)
    {
        using var db = CreateInMemoryDb("ScopeDaira");
        await SeedLocationsAsync(db);
        var svc = CreateService(db);

        var allowed = await svc.CanAccessCommuneAsync(UserRoles.DairaAdmin, null, DairaId10, null, communeId);

        Assert.Equal(expected, allowed);
    }

    [Fact]
    public async Task CanAccessCommuneAsync_DairaAdmin_NoDairaClaim_RejectsAll()
    {
        using var db = CreateInMemoryDb("ScopeDairaNullClaim");
        await SeedLocationsAsync(db);
        var svc = CreateService(db);

        var allowed = await svc.CanAccessCommuneAsync(UserRoles.DairaAdmin, null, null, null, CommuneId100);

        Assert.False(allowed);
    }

    [Theory]
    [InlineData(CommuneId100, true)]
    [InlineData(CommuneId101, false)]
    public async Task CanAccessCommuneAsync_WilayaAdmin_OnlyCommunesInOwnWilaya(int communeId, bool expected)
    {
        using var db = CreateInMemoryDb("ScopeWilaya");
        await SeedLocationsAsync(db);
        var svc = CreateService(db);

        var allowed = await svc.CanAccessCommuneAsync(UserRoles.WilayaAdmin, null, null, WilayaId1, communeId);

        Assert.Equal(expected, allowed);
    }

    [Fact]
    public async Task CanAccessCommuneAsync_WilayaAdmin_CommuneInNestedDaira_Allowed()
    {
        using var db = CreateInMemoryDb("ScopeWilayaNested");
        await SeedLocationsAsync(db);
        var svc = CreateService(db);

        // commune 100 sits in daira 10, which belongs to wilaya 1.
        var allowed = await svc.CanAccessCommuneAsync(UserRoles.WilayaAdmin, null, null, WilayaId1, CommuneId100);

        Assert.True(allowed);
    }

    [Fact]
    public async Task CanAccessCommuneAsync_UnknownRole_RejectsAll()
    {
        using var db = CreateInMemoryDb("ScopeUnknownRole");
        await SeedLocationsAsync(db);
        var svc = CreateService(db);

        var allowed = await svc.CanAccessCommuneAsync("some_role", CommuneId100, null, null, CommuneId100);

        Assert.False(allowed);
    }
}
