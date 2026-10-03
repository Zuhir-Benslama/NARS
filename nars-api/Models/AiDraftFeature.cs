namespace NarsApi.Models;

/// <summary>
/// Maps to the ai_draft_features table. Geometry is stored as GeoJSON text in
/// a JSONB column, matching how production feature tables carry geometry in
/// their JSONB <c>data</c> column. The segmentation client already returns
/// GeoJSON, so no geometry type conversion is needed at write time.
/// </summary>
public sealed class AiDraftFeature
{
    // Draft feature-type keys (the segmentation client emits roads or
    // buildings; the districts generation phase emits district polygons).
    public const string TypeRoad = "road";
    public const string TypeBuilding = "building";
    public const string TypeDistrict = "district";

    // Status values.
    public const string StatusPending = "pending";
    public const string StatusAccepted = "accepted";
    public const string StatusRejected = "rejected";
    public const string StatusEdited = "edited";

    // Provenance values stored in the `source` column (VARCHAR(20), unconstrained).
    // Segmentation-produced drafts keep the column default; districts come from
    // the geometry partition in DistrictGenerationService, not from imagery, so
    // the review queue can tell the two apart at a glance.
    public const string SourceSegmentation = "ai_segmentation";
    public const string SourceDistrictPartition = "district_generation";

    public Guid Id { get; private set; }
    public string FeatureType { get; private set; } = null!; // "road" | "building" | "district"
    public string GeometryGeoJson { get; private set; } = null!;
    public string Source { get; private set; } = SourceSegmentation;
    public double Confidence { get; private set; }
    public string Status { get; private set; } = StatusPending; // pending | accepted | rejected | edited
    public int CommuneId { get; private set; }
    public Guid? ReviewedBy { get; }
    public DateTimeOffset? ReviewedAt { get; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string? SourceTileRef { get; private set; }

    private AiDraftFeature() { } // EF Core

    public static AiDraftFeature Create(
        string featureType,
        string geometryGeoJson,
        double confidence,
        int communeId,
        string? sourceTileRef,
        DateTimeOffset createdAt,
        string? source = null)
    {
        if (featureType is not (TypeRoad or TypeBuilding or TypeDistrict))
        {
            throw new ArgumentException($"Unknown feature type: {featureType}", nameof(featureType));
        }

        return new AiDraftFeature
        {
            Id = Guid.CreateVersion7(),
            FeatureType = featureType,
            GeometryGeoJson = geometryGeoJson,
            Confidence = confidence,
            CommuneId = communeId,
            SourceTileRef = sourceTileRef,
            Source = source ?? SourceSegmentation,
            CreatedAt = createdAt,
            Status = StatusPending,
        };
    }
}
