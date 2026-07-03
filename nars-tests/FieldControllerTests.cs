using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NarsApi.Controllers;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using Xunit;

namespace NarsApi.Tests;

using static TestData;

public class FieldControllerTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime FixedNow = new(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private static (FieldController, AppDbContext, Mock<IFieldService>) CreateController(
        AppDbContext? db = null,
        int? communeId = 1,
        IFieldService? fieldService = null)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"FieldTest_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var context = db ?? new AppDbContext(opts);

        // Seed the field worker user so FindAsync in the controller succeeds
        context.Users.Add(new User
        {
            Id = UserId,
            Name = "Field Worker",
            Email = UniqueEmail("field"),
            Phone = DefaultPhone,
            Username = "fieldworker",
            PasswordHash = "hash",
            Role = UserRoles.FieldWorker,
            CommuneId = communeId,
        });
        context.SaveChanges();

        var timeProvider = Mock.Of<IDateTimeProvider>(x => x.UtcNow == FixedNow);
        var fieldSvc = fieldService ?? Mock.Of<IFieldService>();

        var ctrl = new FieldController(
            context,
            Mock.Of<ILogger<FieldController>>(),
            Options.Create(new FeatureDefaultsOptions()),
            timeProvider,
            fieldSvc)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = AuthTestHelper.CreateClaimsPrincipal(UserId, UserRoles.FieldWorker, communeId: communeId)
                }
            }
        };

        return (ctrl, context, Mock.Get(fieldSvc));
    }

    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement;

    private static Guid AddRoad(AppDbContext db, Guid ownerId, int? ownerCommuneId = 1)
    {
        var roadId = Guid.NewGuid();
        db.Roads.Add(new Road
        {
            Id = roadId,
            UserId = ownerId,
            Layer = FeatureTypes.RoadLayers.Street,
            Data = "{}",
            Label = "test road",
            UpdatedAt = FixedNow
        });
        db.Users.Add(new User
        {
            Id = ownerId,
            Name = "Owner",
            Email = UniqueEmail("owner"),
            Username = "owner",
            PasswordHash = "hash",
            Role = UserRoles.CommuneUser,
            CommuneId = ownerCommuneId,
        });
        db.SaveChanges();
        return roadId;
    }

    // ── POST /api/field/entrance/create ──

    [Fact]
    public async Task CreateEntrance_RoadOwnerCommuneNull_DoesNotForbid()
    {
        var (ctrl, db, _) = CreateController(communeId: 1);
        var ownerId = Guid.NewGuid();
        var roadId = AddRoad(db, ownerId, ownerCommuneId: null);

        var result = await ctrl.CreateEntranceFromInspection(new FieldEntranceCreateRequest(
            RoadId: roadId.ToString(),
            Data: Json("{}"),
            Label: "new entrance"
        ));

        Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, ((ObjectResult)result).StatusCode);
    }

    [Fact]
    public async Task CreateEntrance_RoadOwnerCommuneMismatch_Returns403()
    {
        var (ctrl, db, _) = CreateController(communeId: 1);
        var ownerId = Guid.NewGuid();
        var roadId = AddRoad(db, ownerId, ownerCommuneId: 2);

        var result = await ctrl.CreateEntranceFromInspection(new FieldEntranceCreateRequest(
            RoadId: roadId.ToString(),
            Data: Json("{}"),
            Label: "new entrance"
        ));

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task CreateEntrance_RoadOwnerCommuneMatch_Returns201()
    {
        var (ctrl, db, _) = CreateController(communeId: 1);
        var ownerId = Guid.NewGuid();
        var roadId = AddRoad(db, ownerId, ownerCommuneId: 1);

        var result = await ctrl.CreateEntranceFromInspection(new FieldEntranceCreateRequest(
            RoadId: roadId.ToString(),
            Data: Json("{}"),
            Label: "new entrance"
        ));

        Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, ((ObjectResult)result).StatusCode);
    }

    [Fact]
    public async Task CreateEntrance_OwnerLocked_StillAllowsCreation()
    {
        var (ctrl, db, _) = CreateController(communeId: 1);
        var ownerId = Guid.NewGuid();
        var roadId = AddRoad(db, ownerId, ownerCommuneId: 1);

        var owner = await db.Users.FindAsync(ownerId);
        owner!.LockedUntil = FixedNow.AddHours(1);
        await db.SaveChangesAsync();

        var result = await ctrl.CreateEntranceFromInspection(new FieldEntranceCreateRequest(
            RoadId: roadId.ToString(),
            Data: Json("{}"),
            Label: "new entrance"
        ));

        Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, ((ObjectResult)result).StatusCode);
    }

    [Fact]
    public async Task CreateEntrance_NullBody_Returns400()
    {
        var (ctrl, _, _) = CreateController();
        var result = await ctrl.CreateEntranceFromInspection(null!);
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task CreateEntrance_EmptyRoadId_Returns400()
    {
        var (ctrl, _, _) = CreateController();
        var result = await ctrl.CreateEntranceFromInspection(new FieldEntranceCreateRequest(
            RoadId: "",
            Data: Json("{}"),
            Label: "test"
        ));

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task CreateEntrance_RoadOwnerLockedExpired_DoesNotForbid()
    {
        var (ctrl, db, _) = CreateController(communeId: 1);
        var ownerId = Guid.NewGuid();
        var roadId = AddRoad(db, ownerId, ownerCommuneId: 1);

        // Lock expired
        var owner = await db.Users.FindAsync(ownerId);
        owner!.LockedUntil = FixedNow.AddHours(-1);
        await db.SaveChangesAsync();

        var result = await ctrl.CreateEntranceFromInspection(new FieldEntranceCreateRequest(
            RoadId: roadId.ToString(),
            Data: Json("{}"),
            Label: "new entrance"
        ));

        Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, ((ObjectResult)result).StatusCode);
    }
}
