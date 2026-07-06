namespace NarsApi.Services;

public interface IValidationService
{
    Task<bool> CheckRoadConnectivityAsync(Guid userId, string wkt, double maxDistanceMeters, CancellationToken ct = default);
    Task<bool> CheckDistrictCoverageAsync(Guid userId, double toleranceMeters, CancellationToken ct = default);
    Task<bool> CheckDistrictOverlapAsync(Guid userId, string wkt, CancellationToken ct = default);
    Task<long> CountSiblingsInSameAreaAsync(Guid userId, string wkt, CancellationToken ct = default);
    Task<bool> CheckDistrictAdjacencyAsync(Guid userId, string wkt, CancellationToken ct = default);
    Task<bool> UserHasCentralUrbanAreaAsync(Guid userId, CancellationToken ct = default);
    Task<int> CountUserRoadsAsync(Guid userId, CancellationToken ct = default);
    Task<int> CountUserDistrictsAsync(Guid userId, CancellationToken ct = default);
    Task<int> CountUserUrbanAreasAsync(Guid userId, CancellationToken ct = default);
}
