namespace NarsApi.Models;

/// <summary>
/// Maps to the ai_draft_features table. Geometry is stored as GeoJSON text in
/// a JSONB column, matching how production feature tables carry geometry in
/// their JSONB <c>data</c> column. The segmentation client already returns
/// GeoJSON, so no geometry type conversion is needed at write time.
/// </summary>
public sealed class AiDraftFeature
{
    public Guid Id { get; private set; }
    public string FeatureType { get; private set; } = null!; // "road" | "building"
    public string GeometryGeoJson { get; private set; } = null!;
    public string Source { get; private set; } = "ai_segmentation";
    public double Confidence { get; private set; }
    public string Status { get; private set; } = "pending"; // pending | accepted | rejected | edited
    public int CommuneId { get; private set; }
    public Guid? ReviewedBy { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
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
        if (featureType is not ("road" or "building"))
        {
            throw new ArgumentException($"Unknown feature type: {featureType}", nameof(featureType));
        }

        return new AiDraftFeature
        {
            Id = Guid.NewGuid(),
            FeatureType = featureType,
            GeometryGeoJson = geometryGeoJson,
            Confidence = confidence,
            CommuneId = communeId,
            SourceTileRef = sourceTileRef,
            CreatedAt = createdAt,
            Status = "pending",
        };
    }

    public void MarkAccepted(Guid reviewedBy, DateTimeOffset reviewedAt)
    {
        Status = "accepted";
        ReviewedBy = reviewedBy;
        ReviewedAt = reviewedAt;
    }

    public void MarkRejected(Guid reviewedBy, DateTimeOffset reviewedAt)
    {
        Status = "rejected";
        ReviewedBy = reviewedBy;
        ReviewedAt = reviewedAt;
    }
}
