namespace NarsApi.Services;

public interface IValidationService
{
    /// <summary>Checks whether a new road connects to at least one existing road within the given distance.</summary>
    Task<bool> CheckRoadConnectivityAsync(Guid userId, string wkt, double maxDistanceMeters, CancellationToken ct = default);
    /// <summary>Checks whether the user's districts fully cover all urban areas within tolerance.</summary>
    Task<bool> CheckDistrictCoverageAsync(Guid userId, double toleranceMeters, CancellationToken ct = default);
    /// <summary>Checks whether a new district polygon overlaps any existing district.</summary>
    Task<bool> CheckDistrictOverlapAsync(Guid userId, string wkt, CancellationToken ct = default);
    /// <summary>Counts districts already in the same urban area as a new district.</summary>
    Task<long> CountSiblingsInSameAreaAsync(Guid userId, string wkt, CancellationToken ct = default);
    /// <summary>Checks whether a new district shares a boundary with at least one existing district.</summary>
    Task<bool> CheckDistrictAdjacencyAsync(Guid userId, string wkt, CancellationToken ct = default);
    /// <summary>Returns true if the user has created a central urban area.</summary>
    Task<bool> UserHasCentralUrbanAreaAsync(Guid userId, CancellationToken ct = default);
    /// <summary>Counts the user's road features.</summary>
    Task<int> CountUserRoadsAsync(Guid userId, CancellationToken ct = default);
    /// <summary>Counts the user's district features.</summary>
    Task<int> CountUserDistrictsAsync(Guid userId, CancellationToken ct = default);
    /// <summary>Counts the user's urban area features.</summary>
    Task<int> CountUserUrbanAreasAsync(Guid userId, CancellationToken ct = default);
}
