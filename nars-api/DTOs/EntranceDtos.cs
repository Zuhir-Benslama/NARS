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
    [property: MinLength(1)]
    [property: MaxLength(1000)]
    List<Guid> EntranceIds
);

/// <summary>The authoritative number assigned to one entrance.</summary>
public record NumberedEntrance(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("side")] string Side,
    [property: JsonPropertyName("entranceNumber")] int EntranceNumber,
    [property: JsonPropertyName("label")] string Label
);

public record NumberEntrancesResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("entrances")] IReadOnlyList<NumberedEntrance> Entrances
);
