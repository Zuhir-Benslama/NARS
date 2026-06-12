namespace NarsApi.Services;

/// <summary>
/// Retrieves commune boundary geometry as GeoJSON via raw ADO.NET.
/// Raw ADO.NET is required because ST_AsGeoJSON returns a text result that
/// EF Core's Npgsql mapper mis-handles under UseSnakeCaseNamingConvention().
/// </summary>
public interface IBoundaryService
{
    Task<string?> GetBoundaryGeoJsonAsync(int communeId, CancellationToken ct = default);
}
