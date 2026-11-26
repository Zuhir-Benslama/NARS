using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace NarsApi.DTOs;

/// <summary>
/// Request to atomically assign a dense, collision-free sequence of entrance
/// numbers to an ordered list of house-entrance features on a single road.
///
/// The <paramref name="EntranceIds"/> must be ordered by the caller (e.g. by
/// distance along the road); the server assigns numbers in that order, starting
/// at the next free odd (left) / even (right) number and continuing by 2.
/// Serving the whole batch in one locked transaction is what makes concurrent
/// numbering of the same road safe.
/// </summary>
public record NumberEntrancesRequest(
    [property: JsonPropertyName("roadId")]
    [property: JsonRequired]
    Guid RoadId,
    [property: JsonPropertyName("entranceIds")]
    [property: JsonRequired]
    [MinLength(1)]
    [MaxLength(NumberEntrancesRequest.MaxBatchSize)]
    List<Guid> EntranceIds
)
{
    /// <summary>
    /// Upper bound on one numbering batch. The service runs one UPDATE per
    /// entrance inside a single locked transaction, so a large batch holds the
    /// road lock (and its serialization anchor) for too long.
    /// </summary>
    public const int MaxBatchSize = 200;
}

/// <summary>
/// Request to number main entrances around one special feature (cité,
/// roundabout or square). The server recomputes the authoritative counter-
/// clockwise order and inside/outside zone from the feature geometry, so the
/// caller only supplies the candidate entrance IDs.
/// </summary>
public record SpecialNumberingRequest(
    [property: JsonPropertyName("mode")]
    [property: JsonRequired]
    string Mode,
    [property: JsonPropertyName("featureId")]
    [property: JsonRequired]
    Guid FeatureId,
    [property: JsonPropertyName("entranceIds")]
    [property: JsonRequired]
    [MinLength(1)]
    [MaxLength(SpecialNumberingRequest.MaxBatchSize)]
    List<Guid> EntranceIds,
    [property: JsonPropertyName("startPoint")]
    CoordDto? StartPoint = null
)
{
    public const int MaxBatchSize = 200;

    /// <summary>Default width of the band outside a roundabout/square.</summary>
    public const double DefaultOutsideBandM = 100.0;
}

/// <summary>The authoritative number assigned to one entrance.</summary>
public record NumberedEntrance(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("side")] string Side,
    [property: JsonPropertyName("entranceNumber")] int EntranceNumber,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("numberSuffix")] string? NumberSuffix = null
);

public record NumberEntrancesResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("entrances")] IReadOnlyList<NumberedEntrance> Entrances
);
