using NarsApi.DTOs;

namespace NarsApi.Services;

/// <summary>
/// Numbers house entrances around a special feature (cité, roundabout or
/// square). Returns null when the feature, layer or any requested entrance is
/// missing, unowned or out of scope for the feature's geometry.
/// </summary>
public interface ISpecialNumberingService
{
    Task<IReadOnlyList<NumberedEntrance>?> NumberAsync(
        Guid userId,
        string mode,
        Guid featureId,
        IReadOnlyList<Guid> entranceIds,
        (double Lat, double Lng)? startPoint,
        CancellationToken ct);
}
