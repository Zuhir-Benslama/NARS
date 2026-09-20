namespace NarsApi.Services;

public static class SegmentationServiceExtensions
{
    /// <summary>
    /// Registers the nars-segma HTTP client.
    /// Add to Program.cs: builder.Services.AddSegmentationClient(builder.Configuration);
    /// Expects config keys:
    ///   Segmentation:BaseUrl   e.g. http://nars-segma:8000 (cluster DNS name)
    ///   Segmentation:InternalToken   shared secret, must match NARS_SEGMA_INTERNAL_TOKEN
    /// </summary>
    public static IServiceCollection AddSegmentationClient(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient<ISegmentationClient, SegmentationClient>(client =>
        {
            var baseUrl = configuration["Segmentation:BaseUrl"]
                ?? throw new InvalidOperationException("Segmentation:BaseUrl is not configured");
            client.BaseAddress = new Uri(baseUrl);

            var token = configuration["Segmentation:InternalToken"];
            if (!string.IsNullOrEmpty(token))
            {
                client.DefaultRequestHeaders.Add("X-Internal-Token", token);
            }

            // segma inference runs on CPU and can legitimately take tens of
            // seconds per 1024px window, so the HttpClient cap must exceed the
            // whole resilience pipeline below. A commune-scale z18 tile is 5x5
            // windows ~104s; budget 280s here (> the 240s total pipeline).
            client.Timeout = TimeSpan.FromSeconds(280); // large tiles + CPU inference can be slow
        })
        // The StandardResilienceHandler defaults are tuned for fast HTTP APIs
        // (10s per attempt, 30s total) — far too tight for a CPU-bound model.
        // A single segma request decomposes into several 1024px inference
        // windows, each potentially taking ~10-30s; killing an attempt at 10s
        // guarantees failure (and spurious retries) for any real tile.
        .AddStandardResilienceHandler(options =>
        {
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(180);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(240);
            // Sampling duration must be ≥ 2× the attempt timeout.
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(400);
            options.Retry.MaxRetryAttempts = 1;
            options.Retry.Delay = TimeSpan.FromSeconds(2);
        });

        return services;
    }
}
