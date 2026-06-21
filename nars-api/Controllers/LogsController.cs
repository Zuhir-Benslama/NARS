using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
public class LogsController(AppDbContext db, IConfiguration config, IDateTimeProvider timeProvider) : ControllerBase
{
    private int MaxBatchSize => int.TryParse(config["Logging:MaxBatchSize"], out var v) ? v : 50;
    private int MaxEntryLength => int.TryParse(config["Logging:MaxEntryLength"], out var v) ? v : 10_000;

    private static readonly HashSet<string> AllowedLevels = ["error", "warn", "info", "debug", "trace"];

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
            ? GetUserId()
            : (Guid?)null;

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.FirstOrDefault();

        var now = timeProvider.UtcNow;
        var entries = new List<ErrorLog>(body.Logs.Count);

        foreach (var entry in body.Logs)
        {
            if (string.IsNullOrEmpty(entry.Message))
            {
                continue;
            }

            if (entry.Message.Length > MaxEntryLength)
            {
                continue;
            }

            var level = (entry.Level ?? "error").ToLowerInvariant();
            if (!AllowedLevels.Contains(level))
            {
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
                Url = entry.Url?[..Math.Min(entry.Url.Length, 2048)],
                Method = entry.Method?[..Math.Min(entry.Method.Length, 10)],
                IpAddress = ipAddress,
                UserAgent = userAgent?[..Math.Min(userAgent.Length, 500)],
                CreatedAt = now,
            });
        }

        if (entries.Count == 0)
        {
            return NoContent();
        }

        db.ErrorLogs.AddRange(entries);
        await db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private Guid GetUserId()
    {
        var claim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        return claim is not null && Guid.TryParse(claim.Value, out var id) ? id : Guid.Empty;
    }
}
