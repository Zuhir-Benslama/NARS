using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NarsApi.Controllers;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests;

public class LogsControllerTests
{
    private static readonly LoggingOptions DefaultLogOptions = new() { MaxBatchSize = DefaultMaxBatchSize, MaxEntryLength = DefaultMaxEntryLength };

    private static IDateTimeProvider CreateFixedTime() =>
        Mock.Of<IDateTimeProvider>(p => p.UtcNow == FixedUtcNow);

    private static LogsController CreateController(
        IErrorLogService? errorLogService = null,
        LoggingOptions? logOptions = null,
        bool authenticated = false,
        Guid? userId = null)
    {
        var ctrl = new LogsController(
            errorLogService ?? Mock.Of<IErrorLogService>(),
            Mock.Of<ILogger<LogsController>>(),
            Options.Create(logOptions ?? DefaultLogOptions),
            CreateFixedTime());

        var claims = new List<Claim>();
        if (authenticated && userId.HasValue)
        {
            claims.Add(new Claim(ClaimNames.UserId, userId.Value.ToString()));
        }

        var identity = authenticated
            ? new ClaimsIdentity(claims, "TestAuth")
            : new ClaimsIdentity();

        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity),
                Connection = { RemoteIpAddress = System.Net.IPAddress.Loopback },
            }
        };

        if (authenticated)
        {
            ctrl.ControllerContext.HttpContext.Request.Headers.UserAgent = "TestAgent/1.0";
        }

        return ctrl;
    }

    // ── SubmitLogs ──────────────────────────────────────────────────────

    [Fact]
    public async Task SubmitLogs_NullBody_Returns400()
    {
        var ctrl = CreateController();

        var result = await ctrl.SubmitLogs(null!);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, obj.StatusCode);
    }

    [Fact]
    public async Task SubmitLogs_EmptyLogs_Returns400()
    {
        var ctrl = CreateController();
        var body = new LogBatch([]);

        var result = await ctrl.SubmitLogs(body);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, obj.StatusCode);
    }

    [Fact]
    public async Task SubmitLogs_ExceedsMaxBatch_Returns400()
    {
        var ctrl = CreateController(logOptions: new LoggingOptions { MaxBatchSize = 5, MaxEntryLength = DefaultMaxEntryLength });
        var entries = Enumerable.Range(0, 10)
            .Select(i => new LogEntry("error", null, $"msg{i}", null, null, null))
            .ToList();
        var body = new LogBatch(entries);

        var result = await ctrl.SubmitLogs(body);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, obj.StatusCode);
    }

    [Fact]
    public async Task SubmitLogs_ValidLogs_Returns204()
    {
        var mock = new Mock<IErrorLogService>();
        var ctrl = CreateController(errorLogService: mock.Object, authenticated: true, userId: Guid.NewGuid());
        var body = new LogBatch(
        [
            new("error", "E001", "Something broke", null, "/page", "GET"),
            new("warn", "W001", "Deprecated usage", null, null, null),
        ]);

        var result = await ctrl.SubmitLogs(body);

        Assert.IsType<NoContentResult>(result);
        mock.Verify(s => s.LogBatchAsync(
            It.Is<List<ErrorLog>>(l => l.Count == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitLogs_SkipsInvalidEntries_Returns204()
    {
        var mock = new Mock<IErrorLogService>();
        var ctrl = CreateController(errorLogService: mock.Object, authenticated: false);
        var body = new LogBatch(
        [
            new(null, null, "", null, null, null),          // empty message → skip
            new("bogus", null, "bad level", null, null, null), // invalid level → skip
            new("info", null, "valid entry", null, null, null),
        ]);

        var result = await ctrl.SubmitLogs(body);

        Assert.IsType<NoContentResult>(result);
        mock.Verify(s => s.LogBatchAsync(
            It.Is<List<ErrorLog>>(l => l.Count == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitLogs_AllInvalid_Returns400()
    {
        var ctrl = CreateController();
        var body = new LogBatch(
        [
            new(null, null, "", null, null, null),
            new("bogus", null, "", null, null, null),
        ]);

        var result = await ctrl.SubmitLogs(body);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, obj.StatusCode);
    }

    [Fact]
    public async Task SubmitLogs_SanitizesFields()
    {
        List<ErrorLog>? captured = null;
        var mock = new Mock<IErrorLogService>();
        mock.Setup(s => s.LogBatchAsync(It.IsAny<List<ErrorLog>>(), It.IsAny<CancellationToken>()))
            .Callback<List<ErrorLog>, CancellationToken>((entries, _) => captured = entries)
            .Returns(Task.CompletedTask);

        var ctrl = CreateController(errorLogService: mock.Object, authenticated: false);
        var body = new LogBatch(
        [
            new("error", "code\x00with\u0007control", "msg", "ctx\u0001value", "http://test.com/p\u0002q", "PO\u0003ST"),
        ]);

        await ctrl.SubmitLogs(body);

        Assert.NotNull(captured);
        var entry = Assert.Single(captured!);
        Assert.Equal("codewithcontrol", entry.Code);
        Assert.Equal("ctxvalue", entry.Context);
        Assert.Equal("http://test.com/pq", entry.Url);
        Assert.Equal("POST", entry.Method);
    }

    [Fact]
    public async Task SubmitLogs_MessageTooLong_RejectsEntry()
    {
        var ctrl = CreateController(logOptions: new LoggingOptions { MaxBatchSize = DefaultMaxBatchSize, MaxEntryLength = 50 });
        var body = new LogBatch(
        [
            new("error", null, new string('x', 51), null, null, null),
        ]);

        var result = await ctrl.SubmitLogs(body);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, obj.StatusCode);
    }

    [Fact]
    public async Task SubmitLogs_DefaultLevelIsError()
    {
        List<ErrorLog>? captured = null;
        var mock = new Mock<IErrorLogService>();
        mock.Setup(s => s.LogBatchAsync(It.IsAny<List<ErrorLog>>(), It.IsAny<CancellationToken>()))
            .Callback<List<ErrorLog>, CancellationToken>((entries, _) => captured = entries)
            .Returns(Task.CompletedTask);

        var ctrl = CreateController(errorLogService: mock.Object);
        var body = new LogBatch(
        [
            new(null, null, "no level specified", null, null, null),
        ]);

        await ctrl.SubmitLogs(body);

        Assert.NotNull(captured);
        Assert.Equal("error", captured![0].Level);
    }

    [Fact]
    public async Task SubmitLogs_PreservesOriginalLevelCasing()
    {
        List<ErrorLog>? captured = null;
        var mock = new Mock<IErrorLogService>();
        mock.Setup(s => s.LogBatchAsync(It.IsAny<List<ErrorLog>>(), It.IsAny<CancellationToken>()))
            .Callback<List<ErrorLog>, CancellationToken>((entries, _) => captured = entries)
            .Returns(Task.CompletedTask);

        var ctrl = CreateController(errorLogService: mock.Object);
        var body = new LogBatch(
        [
            new("ERROR", null, "upper case level", null, null, null),
        ]);

        await ctrl.SubmitLogs(body);

        Assert.NotNull(captured);
        Assert.Equal("ERROR", captured![0].Level);
    }
}
