namespace NarsApi.Infrastructure;

/// <summary>
/// Named rate limiter policy constants used with [EnableRateLimiting(...)].
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>Sliding-window limiter for sign-in and refresh endpoints.</summary>
    public const string Auth = "auth";

    /// <summary>Fixed-window limiter for the delete-all endpoint.</summary>
    public const string Clear = "clear";

    /// <summary>Sliding-window limiter for general API endpoints.</summary>
    public const string Api = "api";

    /// <summary>Fixed-window limiter for client-side error log submission.</summary>
    public const string Logs = "logs";
}
