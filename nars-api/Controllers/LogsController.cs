using System.Buffers;
using System.Text.Encodings.Web;
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
    IWebHostEnvironment webHost) : NarsControllerBase(webHost)
{
    private int MaxBatchSize => logOptions.Value.MaxBatchSize;
    private int MaxEntryLength => logOptions.Value.MaxEntryLength;

    private static readonly HashSet<string> AllowedLevels = ["error", "warn", "info", "debug", "trace"];
    private const int MaxUrlLength = 2048;
    private const int MaxMethodLength = 10;
    private const int MaxUserAgentLength = 500;

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

        var userId = CurrentUserId;

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
                Code = SanitizeLogField(entry.Code ?? "", 100),
                Message = SanitizeLogMessage(entry.Message, MaxEntryLength),
                Context = string.IsNullOrEmpty(entry.Context) ? null : SanitizeLogMessage(entry.Context, MaxEntryLength),
                Url = SanitizeLogField(entry.Url ?? "", MaxUrlLength),
                Method = SanitizeLogField(entry.Method ?? "", MaxMethodLength),
                IpAddress = ipAddress,
                UserAgent = SanitizeLogField(userAgent ?? "", MaxUserAgentLength),
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

    /// <summary>
    /// Strips control characters (except \n, \r, \t) and truncates to maxLen.
    /// Prevents log injection via control chars.
    /// </summary>
    private const int StackBufferCharLimit = 4096;

    private static string SanitizeLogField(string value, int maxLen)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        // Output can never exceed maxLen, so cap the buffer at maxLen. Large
        // payloads fall back to the heap to avoid overflowing the thread stack.
        var capacity = Math.Min(value.Length, maxLen);
        if (capacity <= StackBufferCharLimit)
        {
            Span<char> buffer = stackalloc char[capacity];
            var written = SanitizeInto(value, buffer);
            return new string(buffer[..written]);
        }

        var rented = ArrayPool<char>.Shared.Rent(capacity);
        try
        {
            var written = SanitizeInto(value, rented);
            return new string(rented.AsSpan(0, written));
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }
    }

    private static int SanitizeInto(string value, Span<char> buffer)
    {
        var written = 0;
        foreach (var c in value)
        {
            if (written == buffer.Length)
            {
                break;
            }

            if (c is '\n' or '\r' or '\t' || !char.IsControl(c))
            {
                buffer[written++] = c;
            }
        }

        return written;
    }

    /// <summary>
    /// Sanitizes free-text log fields (Message, Context) and HTML-encodes them so a
    /// log viewer that renders values as raw HTML cannot execute script (stored XSS).
    /// </summary>
    private static string SanitizeLogMessage(string value, int maxLen) =>
        HtmlEncoder.Default.Encode(SanitizeLogField(value, maxLen));
}
