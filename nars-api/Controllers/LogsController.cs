using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;

namespace NarsApi.Controllers;

[ApiController]
[Route("/api")]
[Tags("Logs")]
[EnableRateLimiting(RateLimitPolicies.Logs)]
public class LogsController(
    IErrorLogService errorLogService,
    ILogger<LogsController> logger,
    IOptions<LoggingOptions> logOptions,
    IDateTimeProvider timeProvider,
    ILogSanitizer sanitizer,
    IWebHostEnvironment webHost) : NarsControllerBase(webHost)
{
    private int MaxBatchSize => logOptions.Value.MaxBatchSize;
    private int MaxEntryLength => logOptions.Value.MaxEntryLength;

    private static readonly HashSet<string> AllowedLevels = ["error", "warn", "info", "debug", "trace"];
    private const int MaxUrlLength = 2048;
    private const int MaxMethodLength = 10;
    private const int MaxUserAgentLength = 500;
    private const int MaxCodeLength = 50; // must match ErrorLog.Code [MaxLength(50)]

    /// <summary>Accepts client-side error logs for server-side storage and analysis.</summary>
    [HttpPost("logs")]
    [Authorize]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SubmitLogs([FromBody] LogBatch body, CancellationToken cancellationToken = default)
    {
        if (body.Logs is null || body.Logs.Count == 0)
        {
            return Problem(detail: "No log entries provided.", statusCode: 400);
        }

        if (body.Logs.Count > MaxBatchSize)
        {
            return Problem(detail: $"Batch size exceeds maximum of {MaxBatchSize}.", statusCode: 400);
        }

        var userId = CurrentUserId;

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.FirstOrDefault();

        var now = timeProvider.UtcNow;
        var entries = new List<ErrorLog>(body.Logs.Count);

        var skipped = 0;
        foreach (var entry in body.Logs)
        {
            if (entry is null || string.IsNullOrEmpty(entry.Message))
            {
                skipped++;
                continue;
            }

            if (entry.Message.Length > MaxEntryLength)
            {
                skipped++;
                continue;
            }

            var level = entry.Level ?? "error";
            if (!AllowedLevels.Contains(level, StringComparer.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }

            entries.Add(new ErrorLog
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                Level = level,
                Code = sanitizer.Sanitize(entry.Code ?? "", MaxCodeLength),
                Message = sanitizer.Sanitize(entry.Message, MaxEntryLength),
                Context = string.IsNullOrEmpty(entry.Context) ? null : sanitizer.Sanitize(entry.Context, MaxEntryLength),
                Url = sanitizer.Sanitize(entry.Url ?? "", MaxUrlLength),
                Method = sanitizer.Sanitize(entry.Method ?? "", MaxMethodLength),
                IpAddress = ipAddress,
                UserAgent = sanitizer.Sanitize(userAgent ?? "", MaxUserAgentLength),
                CreatedAt = now,
            });
        }

        if (entries.Count == 0)
        {
            return Problem(detail: $"All {body.Logs.Count} log entries were invalid.", statusCode: 400);
        }

        if (skipped > 0)
        {
            logger.LogWarning("Skipped {Skipped}/{Total} invalid log entries", skipped, body.Logs.Count);
        }

        await errorLogService.LogBatchAsync(entries, cancellationToken);
        return NoContent();
    }
}
