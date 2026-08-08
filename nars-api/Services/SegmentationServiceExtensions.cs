namespace NarsApi.Services;

public static class SegmentationServiceExtensions
{
    /// <summary>
    /// Registers the nars-roads HTTP client.
    /// Add to Program.cs: builder.Services.AddSegmentationClient(builder.Configuration);
    /// Expects config keys:
    ///   Segmentation:BaseUrl   e.g. http://nars-roads:8000 (cluster DNS name)
    ///   Segmentation:InternalToken   shared secret, must match NARS_ROADS_INTERNAL_TOKEN
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

            client.Timeout = TimeSpan.FromSeconds(60); // large tiles + CPU inference can be slow
        });

        return services;
    }
}
