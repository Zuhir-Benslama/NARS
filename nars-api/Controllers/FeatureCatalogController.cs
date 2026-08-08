using Microsoft.AspNetCore.Mvc;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Services;

namespace NarsApi.Controllers;

[ApiController]
[Route("/api")]
[Tags("Feature Catalog")]
public class FeatureCatalogController(
    IFeatureStatsService featureStatsService,
    IWebHostEnvironment webHost) : NarsControllerBase(webHost)
{
    /// <summary>Returns the full catalog of feature types with their available layers.</summary>
    [HttpGet("feature-types")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetFeatureTypes() => Ok(FeatureTypeRegistry.GetCatalog());

    /// <summary>Loads features filtered by layer type with pagination.</summary>
    [HttpGet("features/by-layer/{layerType}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> LoadByLayer(string layerType, [FromQuery] int skip = 0, [FromQuery] int take = 100, CancellationToken cancellationToken = default)
    {
        (skip, take) = Pagination.Clamp(skip, take);

        var (features, totalCount) = await featureStatsService.LoadByLayerAsync(
            RequiredCurrentUserId, layerType, skip, take, cancellationToken);

        return Ok(new LoadFeaturesResponse<FeatureResult>(
            Features: features,
            Count: totalCount,
            Skip: skip,
            Take: take
        ));
    }
}
