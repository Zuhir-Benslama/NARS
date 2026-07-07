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
public class LogsController(IErrorLogService errorLogService, ILogger<LogsController> logger, IOptions<LoggingOptions> logOptions, IDateTimeProvider timeProvider) : ControllerBase
{
    private int MaxBatchSize => logOptions.Value.MaxBatchSize;
    private int MaxEntryLength => logOptions.Value.MaxEntryLength;

    private static readonly HashSet<string> AllowedLevels = ["error", "warn", "info", "debug", "trace"];
    private const int MaxUrlLength = 2048;
    private const int MaxMethodLength = 10;
    private const int MaxUserAgentLength = 500;

    /// <summary>Accepts client-side error logs for server-side storage and analysis.</summary>
    [HttpPost("logs")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SubmitLogs([FromBody] LogBatch body, CancellationToken cancellationToken = default)
    {
        if (body is null)
        {
            return Problem(detail: "Request body is required.", statusCode: 400);
        }

        if (body.Logs is null || body.Logs.Count == 0)
        {
            return Problem(detail: "No log entries provided.", statusCode: 400);
        }

        if (body.Logs.Count > MaxBatchSize)
        {
            return Problem(detail: $"Batch size exceeds maximum of {MaxBatchSize}.", statusCode: 400);
        }

        var userId = User.Identity?.IsAuthenticated == true
            ? GetUserIdOrNull()
            : null;

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.FirstOrDefault();

        var now = timeProvider.UtcNow;
        var entries = new List<ErrorLog>(body.Logs.Count);

        var skipped = 0;
        foreach (var entry in body.Logs)
        {
            if (string.IsNullOrEmpty(entry.Message))
            {
                skipped++;
                continue;
            }

            if (entry.Message.Length > MaxEntryLength)
            {
                skipped++;
                continue;
            }

            var level = (entry.Level ?? "error").ToLowerInvariant();
            if (!AllowedLevels.Contains(level))
            {
                skipped++;
                continue;
            }

            entries.Add(new ErrorLog
            {
                Id = Guid.CreateVersion7(),
                UserId = userId,
                Level = level,
                Code = entry.Code ?? "",
                Message = entry.Message[..Math.Min(entry.Message.Length, MaxEntryLength)],
                Context = string.IsNullOrEmpty(entry.Context) ? null : entry.Context,
                Url = entry.Url?[..Math.Min(entry.Url.Length, MaxUrlLength)],
                Method = entry.Method?[..Math.Min(entry.Method.Length, MaxMethodLength)],
                IpAddress = ipAddress,
                UserAgent = userAgent?[..Math.Min(userAgent.Length, MaxUserAgentLength)],
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

    private Guid? GetUserIdOrNull()
    {
        var claim = User.FindFirst(ClaimNames.UserId);
        return claim is not null && Guid.TryParse(claim.Value, out var id) ? id : null;
    }
}
