using System.ComponentModel.DataAnnotations;

namespace NarsApi.Infrastructure;

public class CacheOptions
{
    [Range(1, 168)] public int ReferenceDataDurationHours { get; set; } = 1;
    [Range(1, 168)] public int PageTemplateDurationHours { get; set; } = 1;
}

public class LocationsOptions
{
    [Range(1, 1000)] public int MaxSearchLength { get; set; } = 200;
}

public class JwtOptions
{
    [Range(1, 1440)] public int ExpiresInMinutes { get; set; } = 1440;
    [Range(1, 365)] public int RefreshExpiresInDays { get; set; } = 30;

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
    [Range(100, 100_000)] public int MaxEntryLength { get; set; } = 10_000;
}

public class HttpClientOptions
{
    [Range(1, 300)] public int TileProxyTimeoutSeconds { get; set; } = 15;
    [Range(1, 300)] public int SatelliteTimeoutSeconds { get; set; } = 30;
}

public class ValidationOptions
{
    [Range(10, 100_000)] public int MaxCoordinateCount { get; set; } = 10_000;
    [Range(0.1, 1000)] public double DistrictBoundaryToleranceMeters { get; set; } = 10.0;
    [Range(10, 180)] public double RoadTurnAngleDegrees { get; set; } = 90.0;
    [Range(1, 1000)] public double RoadConnectivityMeters { get; set; } = 20.0;
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

public class CspOptions
{
    public string DefaultSrc { get; set; } = "'self'";
    public string ScriptSrc { get; set; } = "'self' blob:";
    public string WorkerSrc { get; set; } = "'self' blob:";
    public string StyleSrc { get; set; } = "'self' https://cdn.jsdelivr.net https://unpkg.com 'nonce-' https://fonts.googleapis.com";
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
