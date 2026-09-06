using NarsApi.Data;
using NarsApi.Models;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests;

public class InspectionServiceTests
{
    private static async Task<Inspection> SeedInspectionAsync(AppDbContext db, Guid featureId, string status, int hourOffset)
    {
        var inspection = new Inspection
        {
            Id = Guid.NewGuid(),
            FeatureId = featureId,
            UserId = UserId,
            Type = FeatureTypes.Road,
            Data = """{"key": "value"}""",
            Status = status,
            CreatedAt = FixedUtcNow.AddHours(hourOffset),
        };
        db.Inspections.Add(inspection);
        await db.SaveChangesAsync();
        return inspection;
    }

    // ── GetInspectionsAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetInspectionsAsync_ReturnsNewestFirst()
    {
        var (db, factory) = CreateInMemoryDbPair("InspectionServiceGetNewest");
        await using (db)
        {
            var featureId = Guid.NewGuid();
            var older = await SeedInspectionAsync(db, featureId, "issue", -2);
            var newer = await SeedInspectionAsync(db, featureId, "good", -1);
            _ = await SeedInspectionAsync(db, Guid.NewGuid(), "good", 0);
            var svc = new InspectionService(factory);

            var result = await svc.GetInspectionsAsync(featureId, 0, 100);

            Assert.Equal(2, result.Count);
            Assert.Equal(newer.Id.ToString(), result[0].Id);
            Assert.Equal("good", result[0].Status);
            Assert.Equal(older.Id.ToString(), result[1].Id);
            Assert.Equal("issue", result[1].Status);
            Assert.Equal(featureId.ToString(), result[0].FeatureId);
            Assert.Equal(FeatureTypes.Road, result[0].Type);
        }
    }

    [Fact]
    public async Task GetInspectionsAsync_NoInspections_ReturnsEmptyList()
    {
        var (db, factory) = CreateInMemoryDbPair("InspectionServiceGetEmpty");
        await using (db)
        {
            var svc = new InspectionService(factory);

            var result = await svc.GetInspectionsAsync(Guid.NewGuid(), 0, 100);

            Assert.Empty(result);
        }
    }

    [Fact]
    public async Task GetInspectionsAsync_AppliesSkipAndTake()
    {
        var (db, factory) = CreateInMemoryDbPair("InspectionServiceGetPaged");
        await using (db)
        {
            var featureId = Guid.NewGuid();
            await SeedInspectionAsync(db, featureId, "good", 0);
            await SeedInspectionAsync(db, featureId, "issue", -1);
            await SeedInspectionAsync(db, featureId, "good", -2);
            var svc = new InspectionService(factory);

            var result = await svc.GetInspectionsAsync(featureId, 1, 1);

            Assert.Single(result);
            Assert.Equal("issue", result[0].Status);
        }
    }

    // ── SubmitInspectionAsync ───────────────────────────────────────────

    [Fact]
    public async Task SubmitInspectionAsync_ValidRequest_ReturnsSuccessAndPersists()
    {
        var (db, factory) = CreateInMemoryDbPair("InspectionServiceSubmitValid");
        await using (db)
        {
            var featureId = Guid.NewGuid();
            var svc = new InspectionService(factory);

            var result = await svc.SubmitInspectionAsync(featureId, UserId, FeatureTypes.Road, "good", """{"key": "value"}""");

            var inspectionId = Assert.IsType<Guid>(result.InspectionId);
            Assert.True(result.IsSuccess);
            Assert.Equal(inspectionId, result.InspectionId!.Value);

            var stored = await db.Inspections.FindAsync(inspectionId);
            Assert.NotNull(stored);
            Assert.Equal(featureId, stored!.FeatureId);
            Assert.Equal(UserId, stored.UserId);
            Assert.Equal(FeatureTypes.Road, stored.Type);
            Assert.Equal("good", stored.Status);
            Assert.Equal("""{"key": "value"}""", stored.Data);
        }
    }

    [Fact]
    public async Task SubmitInspectionAsync_InvalidType_ReturnsTypeFailureAndPersistsNothing()
    {
        var (db, factory) = CreateInMemoryDbPair("InspectionServiceSubmitBadType");
        await using (db)
        {
            var svc = new InspectionService(factory);

            var result = await svc.SubmitInspectionAsync(Guid.NewGuid(), UserId, FeatureTypes.Area, "good", "{}");

            Assert.False(result.IsSuccess);
            Assert.Equal(InspectionMalformedField.Type, result.Malformed);
            Assert.Empty(db.Inspections);
        }
    }

    [Fact]
    public async Task SubmitInspectionAsync_InvalidStatus_ReturnsStatusFailureAndPersistsNothing()
    {
        var (db, factory) = CreateInMemoryDbPair("InspectionServiceSubmitBadStatus");
        await using (db)
        {
            var svc = new InspectionService(factory);

            var result = await svc.SubmitInspectionAsync(Guid.NewGuid(), UserId, FeatureTypes.Road, "invalid", "{}");

            Assert.False(result.IsSuccess);
            Assert.Equal(InspectionMalformedField.Status, result.Malformed);
            Assert.Empty(db.Inspections);
        }
    }
}
