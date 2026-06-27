namespace NarsApi.Infrastructure;

public class CacheOptions
{
    public int ReferenceDataDurationHours { get; set; } = 1;
    public int PageTemplateDurationHours { get; set; } = 1;
}

public class LocationsOptions
{
    public int MaxSearchLength { get; set; } = 200;
}

public class JwtOptions
{
    public int ExpiresInMinutes { get; set; } = 1440;
    public int RefreshExpiresInDays { get; set; } = 30;
}

public class FeatureDefaultsOptions
{
    public int MaxFeatureDataSize { get; set; } = 524_288;
    public int MultipartBodyLengthLimit { get; set; } = 10_485_760;
    public int ValueLengthLimit { get; set; } = 1_048_576;
}

public class LoggingOptions
{
    public int MaxBatchSize { get; set; } = 50;
    public int MaxEntryLength { get; set; } = 10_000;
}

public class HttpClientOptions
{
    public int TileProxyTimeoutSeconds { get; set; } = 15;
    public int SatelliteTimeoutSeconds { get; set; } = 30;
}

public class ValidationOptions
{
    public int MaxCoordinateCount { get; set; } = 10_000;
    public double DistrictBoundaryToleranceMeters { get; set; } = 10.0;
    public double RoadTurnAngleDegrees { get; set; } = 90.0;
    public double RoadConnectivityMeters { get; set; } = 20.0;
}

public class AccountLockoutOptions
{
    public int MaxFailedAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 30;
}

public class OpenTelemetryOptions
{
    public string OtlpEndpoint { get; set; } = "http://otel-collector.observability:4317";
}
