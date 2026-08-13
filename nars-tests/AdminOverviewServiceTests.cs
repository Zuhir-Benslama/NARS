using Microsoft.EntityFrameworkCore;
using Moq;
using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests;

public class AdminOverviewServiceTests
{
    private static AppDbContext CreateDb() => CreateInMemoryDb("AdminOverviewTest");

    private static AdminOverviewService CreateService(AppDbContext db) =>
        new(db, Mock.Of<IFeatureStatsService>());

    [Fact]
    public async Task GetNationalOverviewAsync_DuplicateWilayaAdmins_PicksEarliestCreated()
    {
        using var db = CreateDb();
        db.Wilayas.Add(new Wilaya { WilayaId = 1, WilayaFr = "Wilaya", WilayaAr = "ولاية" });
        db.Users.AddRange(
            new User
            {
                Id = Guid.NewGuid(),
                Username = "admin1",
                Name = "Admin One",
                Email = "a1@test.com",
                Phone = DefaultPhone,
                PasswordHash = "hash",
                SecurityStamp = User.GenerateSecurityStamp(),
                Role = UserRoles.WilayaAdmin,
                WilayaId = 1,
                CreatedAt = FixedUtcNow,
            },
            new User
            {
                Id = Guid.NewGuid(),
                Username = "admin2",
                Name = "Admin Two",
                Email = "a2@test.com",
                Phone = DefaultPhone,
                PasswordHash = "hash",
                SecurityStamp = User.GenerateSecurityStamp(),
                Role = UserRoles.WilayaAdmin,
                WilayaId = 1,
                CreatedAt = FixedUtcNow.AddHours(1),
            });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var (items, total) = await service.GetNationalOverviewAsync();

        Assert.Equal(1, total);
        var summary = Assert.Single(items);
        Assert.NotNull(summary.WilayaAdmin);
        Assert.Equal("admin1", summary.WilayaAdmin!.Username);
    }

    [Fact]
    public async Task GetWilayaReportAsync_DuplicateDairaAdmins_PicksEarliestCreated()
    {
        using var db = CreateDb();
        db.Wilayas.Add(new Wilaya { WilayaId = 1, WilayaFr = "Wilaya", WilayaAr = "ولاية" });
        db.Dairas.Add(new Daira { DairaId = 1, WilayaId = 1, DairaFr = "Daira", DairaAr = "دائرة" });
        db.Users.AddRange(
            new User
            {
                Id = Guid.NewGuid(),
                Username = "da1",
                Name = "Daira Admin One",
                Email = "d1@test.com",
                Phone = DefaultPhone,
                PasswordHash = "hash",
                SecurityStamp = User.GenerateSecurityStamp(),
                Role = UserRoles.DairaAdmin,
                DairaId = 1,
                CreatedAt = FixedUtcNow,
            },
            new User
            {
                Id = Guid.NewGuid(),
                Username = "da2",
                Name = "Daira Admin Two",
                Email = "d2@test.com",
                Phone = DefaultPhone,
                PasswordHash = "hash",
                SecurityStamp = User.GenerateSecurityStamp(),
                Role = UserRoles.DairaAdmin,
                DairaId = 1,
                CreatedAt = FixedUtcNow.AddHours(1),
            });
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var report = await service.GetWilayaReportAsync(1);

        Assert.NotNull(report);
        var dairaReport = Assert.Single(report!.Dairas);
        Assert.NotNull(dairaReport.DairaAdmin);
        Assert.Equal("da1", dairaReport.DairaAdmin!.Username);
    }
}
