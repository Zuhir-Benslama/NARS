using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

/// <summary>
/// Outcome of <see cref="IInspectionService.SubmitInspectionAsync"/>. Follows the
/// result-object pattern used by the other services (FieldService was the only
/// one that still threw <see cref="ArgumentException"/> for invalid client
/// input, which the global exception handler had to translate into a 400).
/// </summary>
public enum InspectionMalformedField
{
    Type,
    Status,
}

/// <summary>Structured result so controllers map malformed input to a 400.</summary>
public sealed record SubmitInspectionResult(Guid? InspectionId, InspectionMalformedField? Malformed)
{
    public bool IsSuccess => InspectionId.HasValue;

    public static SubmitInspectionResult Success(Guid inspectionId) => new(inspectionId, null);

    public static SubmitInspectionResult Failure(InspectionMalformedField malformed)
        => new(null, malformed);
}

/// <summary>Reads and records field inspections for features.</summary>
public interface IInspectionService
{
    Task<List<FieldInspectionResponse>> GetInspectionsAsync(Guid featureId, int skip, int take, CancellationToken ct = default);
    Task<SubmitInspectionResult> SubmitInspectionAsync(Guid featureId, Guid userId, string type, string status, string data, CancellationToken ct = default);
}

public sealed class InspectionService(IDbContextFactory<AppDbContext> dbFactory) : IInspectionService
{
    /// <summary>Feature types a field worker may inspect.</summary>
    public static readonly IReadOnlyList<string> ValidInspectionTypes =
        [FeatureTypes.Road, FeatureTypes.HouseEntrance, FeatureTypes.NamingPanel];

    /// <summary>Status values a field inspection may carry.</summary>
    public static readonly IReadOnlyList<string> ValidInspectionStatuses =
        ["good", "issue"];

    public async Task<List<FieldInspectionResponse>> GetInspectionsAsync(Guid featureId, int skip, int take, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Inspections
            .Where(i => i.FeatureId == featureId)
            .OrderByDescending(i => i.CreatedAt)
            .ThenByDescending(i => i.Id)
            .Skip(skip)
            .Take(take)
            .Select(i => new FieldInspectionResponse(
                Id: i.Id.ToString(),
                FeatureId: i.FeatureId.ToString(),
                Type: i.Type,
                Data: JsonHelper.DeserializeSafe(i.Data),
                Status: i.Status,
                CreatedAt: i.CreatedAt
            ))
            .ToListAsync(ct);
    }

    public async Task<SubmitInspectionResult> SubmitInspectionAsync(
        Guid featureId, Guid userId, string type, string status, string data, CancellationToken ct = default)
    {
        if (!ValidInspectionTypes.Contains(type))
        {
            return SubmitInspectionResult.Failure(InspectionMalformedField.Type);
        }

        if (!ValidInspectionStatuses.Contains(status))
        {
            return SubmitInspectionResult.Failure(InspectionMalformedField.Status);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var inspection = new Inspection
        {
            Id = Guid.CreateVersion7(),
            FeatureId = featureId,
            UserId = userId,
            Type = type,
            Data = data,
            Status = status,
        };

        db.Add(inspection);
        await db.SaveChangesAsync(ct);
        return SubmitInspectionResult.Success(inspection.Id);
    }
}
