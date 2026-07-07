using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace NarsApi.DTOs;

public record CoordDto(
    [property: JsonPropertyName("lat")] double Lat,
    [property: JsonPropertyName("lng")] double Lng
);

public record ValidateRoadRequest(
    [Required][property: JsonPropertyName("coordinates")]
    List<CoordDto> Coordinates
);

public record ValidateRoadResponse(
    [property: JsonPropertyName("valid")] bool Valid,
    [property: JsonPropertyName("error")] string? Error
);

public record ValidateDistrictRequest(
    [Required][property: JsonPropertyName("coordinates")]
    List<CoordDto> Coordinates,
    [property: JsonPropertyName("districtTypeKey")]
    string? DistrictTypeKey
);

public record ValidateDistrictResponse(
    [property: JsonPropertyName("valid")] bool Valid,
    [property: JsonPropertyName("error")] string? Error
);

public record DistrictCoverageResponse(
    [property: JsonPropertyName("covered")] bool Covered,
    [property: JsonPropertyName("message")] string Message
);
