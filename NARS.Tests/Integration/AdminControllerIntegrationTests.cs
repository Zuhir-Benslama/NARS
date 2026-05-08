using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using NarsApi.Controllers;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;
using Xunit;

namespace NarsApi.Tests.Integration;

[Collection("PostgreSQL Integration")]
public class AdminControllerIntegrationTests : IAsyncLifetime
{
    private readonly NarsDatabaseFixture _fixture;
    private readonly AppDbContext _db;
    private readonly AdminController _controller;

    public AdminControllerIntegrationTests(NarsDatabaseFixture fixture)
    {
        _fixture = fixture;
        _db = fixture.CreateDbContext();
        _controller = new AdminController(_db, Mock.Of<Microsoft.Extensions.Logging.ILogger<AdminController>>());
    }

    public async Task InitializeAsync()
    {
        await SeedReferenceDataAsync();
    }

    public async Task DisposeAsync()
    {
        await _fixture.CleanTablesAsync();
    }

    [Fact]
    public async Task CreateAdmin_DairaAdminToCommuneUser_InOwnDaira_Returns201()
    {
        var creator = await CreateUserAsync(UserRoles.DairaAdmin, dairaId: 10);
        SetAuthenticatedUser(creator);
        var request = BuildRequest(UserRoles.CommuneUser, communeId: 100);

        var result = await _controller.CreateAdmin(request);

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);

        var createdUser = await _db.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
        Assert.NotNull(createdUser);
        Assert.Equal(UserRoles.CommuneUser, createdUser.Role);
        Assert.Equal(100, createdUser.CommuneId);
    }

    [Fact]
    public async Task CreateAdmin_WilayaAdminToDairaAdmin_InOwnWilaya_Returns201()
    {
        var creator = await CreateUserAsync(UserRoles.WilayaAdmin, wilayaId: 1);
        SetAuthenticatedUser(creator);
        var request = BuildRequest(UserRoles.DairaAdmin, dairaId: 10);

        var result = await _controller.CreateAdmin(request);

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);

        var createdUser = await _db.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
        Assert.NotNull(createdUser);
        Assert.Equal(UserRoles.DairaAdmin, createdUser.Role);
        Assert.Equal(10, createdUser.DairaId);
    }

    [Fact]
    public async Task CreateAdmin_NationalAdminToWilayaAdmin_Returns201()
    {
        var creator = await CreateUserAsync(UserRoles.NationalAdmin);
        SetAuthenticatedUser(creator);
        var request = BuildRequest(UserRoles.WilayaAdmin, wilayaId: 2);

        var result = await _controller.CreateAdmin(request);

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);

        var createdUser = await _db.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
        Assert.NotNull(createdUser);
        Assert.Equal(UserRoles.WilayaAdmin, createdUser.Role);
        Assert.Equal(2, createdUser.WilayaId);
    }

    [Fact]
    public async Task CreateAdmin_DairaAdminToCommuneUser_OutsideOwnDaira_ReturnsForbid()
    {
        var creator = await CreateUserAsync(UserRoles.DairaAdmin, dairaId: 10);
        SetAuthenticatedUser(creator);
        var request = BuildRequest(UserRoles.CommuneUser, communeId: 101);

        var result = await _controller.CreateAdmin(request);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task CreateAdmin_WilayaAdminToDairaAdmin_OutsideOwnWilaya_ReturnsForbid()
    {
        var creator = await CreateUserAsync(UserRoles.WilayaAdmin, wilayaId: 1);
        SetAuthenticatedUser(creator);
        var request = BuildRequest(UserRoles.DairaAdmin, dairaId: 11);

        var result = await _controller.CreateAdmin(request);

        Assert.IsType<ForbidResult>(result);
    }

    [Theory]
    [MemberData(nameof(DisallowedRolePairs))]
    public async Task CreateAdmin_DisallowedRolePair_ReturnsForbid(string creatorRole, string targetRole)
    {
        var creator = await CreateUserAsync(
            creatorRole,
            dairaId: creatorRole == UserRoles.DairaAdmin ? 10 : null,
            wilayaId: creatorRole == UserRoles.WilayaAdmin ? 1 : null,
            communeId: creatorRole == UserRoles.CommuneUser ? 100 : null);
        SetAuthenticatedUser(creator);
        var request = BuildRequest(targetRole, communeId: 100, dairaId: 10, wilayaId: 1);

        var result = await _controller.CreateAdmin(request);

        Assert.IsType<ForbidResult>(result);
    }

    public static IEnumerable<object[]> DisallowedRolePairs()
    {
        yield return [UserRoles.NationalAdmin, UserRoles.NationalAdmin];
        yield return [UserRoles.NationalAdmin, UserRoles.DairaAdmin];
        yield return [UserRoles.NationalAdmin, UserRoles.CommuneUser];
        yield return [UserRoles.WilayaAdmin, UserRoles.WilayaAdmin];
        yield return [UserRoles.WilayaAdmin, UserRoles.CommuneUser];
        yield return [UserRoles.WilayaAdmin, UserRoles.NationalAdmin];
        yield return [UserRoles.DairaAdmin, UserRoles.DairaAdmin];
        yield return [UserRoles.DairaAdmin, UserRoles.WilayaAdmin];
        yield return [UserRoles.DairaAdmin, UserRoles.NationalAdmin];
        yield return [UserRoles.CommuneUser, UserRoles.CommuneUser];
        yield return [UserRoles.CommuneUser, UserRoles.DairaAdmin];
        yield return [UserRoles.CommuneUser, UserRoles.WilayaAdmin];
        yield return [UserRoles.CommuneUser, UserRoles.NationalAdmin];
    }

    private static CreateAdminRequest BuildRequest(
        string role,
        int? communeId = null,
        int? dairaId = null,
        int? wilayaId = null)
    {
        var suffix = Guid.NewGuid().ToString("N");
        return new CreateAdminRequest(
            Name: $"Role Test {suffix[..8]}",
            Email: $"role-{suffix}@test.com",
            Phone: "0555001234",
            Username: $"role_{suffix[..12]}",
            Password: "Str0ng!Pass",
            Role: role,
            CommuneId: communeId,
            DairaId: dairaId,
            WilayaId: wilayaId);
    }

    private async Task<User> CreateUserAsync(
        string role,
        int? communeId = null,
        int? dairaId = null,
        int? wilayaId = null)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = $"Creator {suffix[..8]}",
            Email = $"creator-{suffix}@test.com",
            Phone = "0555000000",
            Username = $"creator_{suffix[..12]}",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Str0ng!Pass"),
            Role = role,
            CommuneId = communeId,
            DairaId = dairaId,
            WilayaId = wilayaId,
        };

        await _db.Users.AddAsync(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private void SetAuthenticatedUser(User user)
    {
        var claims = new List<Claim>
        {
            new("user_id", user.Id.ToString()),
            new("username", user.Username),
            new("role", user.Role),
        };

        if (user.CommuneId.HasValue) claims.Add(new Claim("commune_id", user.CommuneId.Value.ToString()));
        if (user.DairaId.HasValue) claims.Add(new Claim("daira_id", user.DairaId.Value.ToString()));
        if (user.WilayaId.HasValue) claims.Add(new Claim("wilaya_id", user.WilayaId.Value.ToString()));

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
        };
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    private async Task SeedReferenceDataAsync()
    {
        if (!await _db.Wilayas.AnyAsync(w => w.WilayaId == 1))
        {
            await _db.Wilayas.AddAsync(new Wilaya
            {
                WilayaId = 1,
                WilayaFr = "Alger",
                WilayaAr = "Alger",
                WilayaLatitude = 36.75,
                WilayaLongitude = 3.05,
            });
        }

        if (!await _db.Wilayas.AnyAsync(w => w.WilayaId == 2))
        {
            await _db.Wilayas.AddAsync(new Wilaya
            {
                WilayaId = 2,
                WilayaFr = "Blida",
                WilayaAr = "Blida",
                WilayaLatitude = 36.47,
                WilayaLongitude = 2.83,
            });
        }

        if (!await _db.Dairas.AnyAsync(d => d.DairaId == 10))
        {
            await _db.Dairas.AddAsync(new Daira
            {
                DairaId = 10,
                WilayaId = 1,
                DairaFr = "Draria",
                DairaAr = "Draria",
                DairaLatitude = 36.72,
                DairaLongitude = 2.96,
            });
        }

        if (!await _db.Dairas.AnyAsync(d => d.DairaId == 11))
        {
            await _db.Dairas.AddAsync(new Daira
            {
                DairaId = 11,
                WilayaId = 2,
                DairaFr = "Blida Centre",
                DairaAr = "Blida Centre",
                DairaLatitude = 36.47,
                DairaLongitude = 2.82,
            });
        }

        if (!await _db.Communes.AnyAsync(c => c.CommuneId == 100))
        {
            await _db.Communes.AddAsync(new Commune
            {
                CommuneId = 100,
                DairaId = 10,
                CommuneCode = 1001,
                CommuneFr = "Draria Centre",
                CommuneAr = "Draria Centre",
                CommuneLatitude = 36.72,
                CommuneLongitude = 2.96,
            });
        }

        if (!await _db.Communes.AnyAsync(c => c.CommuneId == 101))
        {
            await _db.Communes.AddAsync(new Commune
            {
                CommuneId = 101,
                DairaId = 11,
                CommuneCode = 2001,
                CommuneFr = "Blida Centre",
                CommuneAr = "Blida Centre",
                CommuneLatitude = 36.47,
                CommuneLongitude = 2.82,
            });
        }

        await _db.SaveChangesAsync();
    }
}
