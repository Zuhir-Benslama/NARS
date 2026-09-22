using System.ComponentModel.DataAnnotations;

namespace NarsApi.Infrastructure;

public class CacheOptions
{
    [Range(1, 168)] public int PageTemplateDurationHours { get; set; } = 1;
}

public class LocationsOptions
{
    [Range(1, 1000)] public int MaxSearchLength { get; set; } = 200;
}

public class JwtOptions
{
    // Defaults are deliberately conservative (matching appsettings.json) so a
    // missing/unbound Jwt section fails safe with short-lived tokens instead
    // of silently minting day-long access tokens.
    [Range(1, 1440)] public int ExpiresInMinutes { get; set; } = 60;
    [Range(1, 365)] public int RefreshExpiresInDays { get; set; } = 7;

    /// <summary>
    /// How long a replayed (already-rotated) refresh token is tolerated before
    /// the whole token family is revoked as theft. The legitimate client only
    /// ever holds the newest token, so a retry of an old one within this window
    /// is presumed to be a benign double-submit (double-click, two tabs, UA
    /// retry) rather than an attacker's replay.
    /// </summary>
    [Range(0, 300)] public int RefreshReplayGraceSeconds { get; set; } = 10;

    /// <summary>
    /// JWT signing algorithm. Allowlisted to the symmetric HS* family that the
    /// signing key (a raw secret) can support; a misconfiguration fails fast at
    /// startup via DataAnnotations validation instead of being silently ignored.
    /// </summary>
    [RegularExpression("^(HS256|HS384|HS512)$",
        ErrorMessage = "Jwt:Algorithm must be HS256, HS384 or HS512.")]
    public string Algorithm { get; set; } = "HS256";
}

public class FeatureDefaultsOptions
{
    [Range(1024, 10_485_760)] public int MaxFeatureDataSize { get; set; } = 524_288;
    [Range(1024, 104_857_600)] public int MultipartBodyLengthLimit { get; set; } = 10_485_760;
    [Range(1024, 10_485_760)] public int ValueLengthLimit { get; set; } = 1_048_576;
}

public class LoggingOptions
{
    [Range(1, 1000)] public int MaxBatchSize { get; set; } = 50;

    // Must not exceed the error_logs column width (varchar(4096)) or the
    // LogEntry DTO [MaxLength(4096)] annotations — all three are aligned here.
    [Range(100, 100_000)] public int MaxEntryLength { get; set; } = 4_096;
}

public class ValidationOptions
{
    [Range(10, 100_000)] public int MaxCoordinateCount { get; set; } = 10_000;
    [Range(0.1, 1000)] public double DistrictBoundaryToleranceMeters { get; set; } = 10.0;
    [Range(10, 180)] public double RoadTurnAngleDegrees { get; set; } = 135.0;
    [Range(1, 1000)] public double RoadConnectivityMeters { get; set; } = 20.0;
}

/// <summary>
/// Cadastre-limitation rules applied when an AI road draft is accepted or
/// generated. The length/confidence limits mirror the NARS_SEGMA_ROAD_* limits
/// that used to be enforced inside the segmentation service, so a convention
/// change is a config bump. Roads shorter than <see cref="MinRoadLengthM"/> are
/// removed once a network exists (a bootstrap pass keeps every seed so the
/// first roads can be created); the weld pass then re-connects longer dangling
/// fragments onto the network. segma's own min-length floor is relaxed to 0
/// (the network is the sole judge).
/// </summary>
public class RoadRulesOptions
{
    /// <summary>
    /// Minimum geodesic length for a road. Once any road network exists,
    /// candidates below this are dropped regardless of proximity; during a
    /// bootstrap (no roads at all) every seed is kept so generation can start.
    /// </summary>
    [Range(0.0, 100_000.0)] public double MinRoadLengthM { get; set; } = 10.0;

    [Range(0.0, 1.0)] public double MinConfidence { get; set; } = 0.0;
    [Range(0, 100_000)] public int MaxFeaturesPerTile { get; set; } = 0;

    /// <summary>
    /// A candidate road with no road of the commune's network within this
    /// edge-to-edge distance is considered isolated. Used only by the
    /// minimum-length rule: short + isolated = removed, short + connected = kept.
    /// </summary>
    [Range(0.0, 1000.0)] public double RoadIsolationMeters { get; set; } = 20.0;

    /// <summary>
    /// Distance a generated road vertex may sit outside an urban-area polygon
    /// and still be accepted (the imagery bbox / model can bulge a hair past
    /// the drawn boundary). Roads beyond this are dropped as out-of-area.
    /// </summary>
    [Range(0.0, 1000.0)] public double InsideToleranceMeters { get; set; } = 30.0;

    /// <summary>
    /// Radius around a generated road's corridor in which mapped roads count as
    /// the existing network for the connectivity rule. Endpoint connectivity is
    /// only enforced against this local set; when a commune has roads mapped
    /// only far away (a legacy/demo road, or a mapped district kilometres away)
    /// the generated roads seed the network for this locality, exactly like the
    /// no-roads-at-all case.
    /// </summary>
    [Range(100, 50_000)] public double RoadNetworkSearchMeters { get; set; } = 3000.0;

    /// <summary>
    /// Maximum distance a dangling road endpoint may be from the commune's road
    /// network and still be welded onto it after generation. Endpoints within
    /// <see cref="ValidationOptions.RoadConnectivityMeters"/> are snapped during
    /// rule evaluation; this wider radius lets the weld pass pull the rest in —
    /// preferring a straight continuation of the road's final bearing, falling
    /// back to a nearest-point projection — so isolated fragments become one
    /// connected graph instead of dead ends.
    /// </summary>
    [Range(0.0, 1000.0)] public double RoadWeldRadiusM { get; set; } = 50.0;
}

/// <summary>
/// Cadastre-limitation rules applied when an AI building draft is accepted.
/// Mirrors the NARS_SEGMA_BUILDING_* limits enforced by the segmentation
/// service. Defaults are no-ops (keep every draft).
/// </summary>
public class BuildingRulesOptions
{
    [Range(0.0, 1.0)] public double MinConfidence { get; set; } = 0.0;
    [Range(0, 100_000)] public int MaxFeaturesPerTile { get; set; } = 0;
}

/// <summary>
/// Inference parameters forwarded to the nars-segma service on each /segment
/// request. The model binarizes its probability map at this threshold before
/// vectorizing: the roads (SpaceNet) model fire around 0.4-0.98 at z18 and its
/// end-of-street details sit below 0.5, so 0.4 recovers the faint tail that
/// the default 0.5 clips. Buildings keep 0.5 because their polygons are dense
/// and unambiguous in the same imagery.
/// </summary>
public class SegmentationOptions
{
    [Range(0.0, 1.0)] public double RoadThreshold { get; set; } = 0.3;
    [Range(0.0, 1.0)] public double BuildingThreshold { get; set; } = 0.5;
}

public class AccountLockoutOptions
{
    [Range(1, 100)] public int MaxFailedAttempts { get; set; } = 5;
    [Range(1, 1440)] public int LockoutMinutes { get; set; } = 30;
}

public class OpenTelemetryOptions
{
    /// <summary>
    /// OTLP collector endpoint. Configured in production via appsettings.json or
    /// OTEL_EXPORTER_OTLP_ENDPOINT; left empty in dev so no exporter is registered
    /// (no repeated connection-failure logs). No hard-coded cluster-internal default.
    /// </summary>
    public string OtlpEndpoint { get; set; } = string.Empty;
}

public class BackgroundTaskOptions
{
    [Range(1, 10_000)] public int Capacity { get; set; } = 100;
    [Range(1, 60)] public int GracePeriodSeconds { get; set; } = 5;
}

public class RefreshTokenPruningOptions
{
    [Range(1, 720)] public int IntervalHours { get; set; } = 24;
}

public class AdminSignupOptions
{
    [Required] public string SignupToken { get; set; } = string.Empty;
}

public class CorsOptions
{
    // Single allowlist shared by the CORS policy (CorsCompressionExtensions)
    // and the CSRF origin-rejection middleware (PipelineExtensions) so both
    // always enforce the same origins. Defaults are dev-only localhost, and
    // AddNarsCors fails fast when a non-development deployment still resolves
    // to these defaults.
    public string[] AllowedOrigins { get; set; } =
    [
        "http://localhost:5000", "http://localhost:5001",
        "https://localhost:7000", "https://localhost:7001",
    ];
}

public class CspOptions
{
    public string DefaultSrc { get; set; } = "'self'";
    public string ScriptSrc { get; set; } = "'self' blob:";
    public string WorkerSrc { get; set; } = "'self' blob:";
    public string StyleSrc { get; set; } = "'self' https://cdn.jsdelivr.net https://unpkg.com 'unsafe-inline' https://fonts.googleapis.com";
    public string ImgSrc { get; set; } = "'self' data: blob: https://*.tile.openstreetmap.org https://*.basemaps.cartocdn.com https://*.arcgisonline.com";
    public string FontSrc { get; set; } = "'self' https://cdn.jsdelivr.net https://fonts.gstatic.com";
    public string ConnectSrc { get; set; } = "'self' https: data: https://*.arcgisonline.com https://*.basemaps.cartocdn.com";
    public string FrameAncestors { get; set; } = "'none'";
    public string BaseUri { get; set; } = "'self'";
    public string FormAction { get; set; } = "'self'";
}

public class ProxyOptions
{
    /// <summary>
    /// Maximum number of forwarded header entries to trust per request. Set to the
    /// number of trusted proxy hops in front of the API (1 for ingress-nginx → API).
    /// </summary>
    [Range(1, 10)] public int ForwardLimit { get; set; } = 1;

    /// <summary>
    /// CIDR networks whose forwarded headers (X-Forwarded-For / X-Forwarded-Proto)
    /// are trusted. Must list the ingress/proxy pod networks in front of the API;
    /// defaults to the kind cluster pod CIDR (10.244.0.0/16). Override per cluster
    /// (e.g. EKS 10.0.0.0/16) — see nars-infra/k8s/ingress-api.yaml.
    /// </summary>
    public List<string> KnownNetworks { get; set; } = ["10.244.0.0/16"];
}
