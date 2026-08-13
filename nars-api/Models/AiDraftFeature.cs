namespace NarsApi.Models;

/// <summary>
/// Maps to the ai_draft_features table. Geometry is stored as GeoJSON text in
/// a JSONB column, matching how production feature tables carry geometry in
/// their JSONB <c>data</c> column. The segmentation client already returns
/// GeoJSON, so no geometry type conversion is needed at write time.
/// </summary>
public sealed class AiDraftFeature
{
    // Draft feature-type keys (the segmentation client can emit roads or buildings).
    public const string TypeRoad = "road";
    public const string TypeBuilding = "building";

    // Status values.
    public const string StatusPending = "pending";
    public const string StatusAccepted = "accepted";
    public const string StatusRejected = "rejected";
    public const string StatusEdited = "edited";

    public Guid Id { get; private set; }
    public string FeatureType { get; private set; } = null!; // "road" | "building"
    public string GeometryGeoJson { get; private set; } = null!;
    public string Source { get; private set; } = "ai_segmentation";
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
        DateTimeOffset createdAt)
    {
        if (featureType is not (TypeRoad or TypeBuilding))
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
            CreatedAt = createdAt,
            Status = StatusPending,
        };
    }
}
