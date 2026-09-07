using Microsoft.Extensions.Logging;
using Moq;
using NarsApi.Services;
using Xunit;

namespace NarsApi.Tests;

/// <summary>
/// Verifies the payload-handling logic of <see cref="StampEvictionListener"/>:
/// valid user ids evict the cache entry, malformed payloads are ignored
/// without throwing (the listener must survive a poisoned notification).
/// </summary>
public class StampEvictionListenerTests
{
    private readonly Mock<ISecurityStampCache> _cache = new();
    private readonly Mock<ILogger> _logger = new();

    [Fact]
    public void EvictFromPayload_ValidUserId_EvictsStamp()
    {
        var userId = Guid.NewGuid();

        StampEvictionListener.EvictFromPayload(
            userId.ToString(), _cache.Object, _logger.Object);

        _cache.Verify(c => c.EvictStamp(userId), Times.Once);
        VerifyWarning(Times.Never());
    }

    [Fact]
    public void EvictFromPayload_WhitespaceAroundId_EvictsStamp()
    {
        var userId = Guid.NewGuid();

        StampEvictionListener.EvictFromPayload(
            $"  {userId}\n", _cache.Object, _logger.Object);

        _cache.Verify(c => c.EvictStamp(userId), Times.Once);
        VerifyWarning(Times.Never());
    }

    [Fact]
    public void EvictFromPayload_MalformedPayload_DoesNotThrowOrEvict()
    {
        StampEvictionListener.EvictFromPayload(
            "not-a-guid", _cache.Object, _logger.Object);

        _cache.Verify(c => c.EvictStamp(It.IsAny<Guid>()), Times.Never);
        // The malformed payload must be visible in logs, not swallowed silently.
        VerifyWarning(Times.Once(), "not-a-guid");
    }

    [Fact]
    public void EvictFromPayload_EmptyPayload_DoesNotThrowOrEvict()
    {
        StampEvictionListener.EvictFromPayload(
            string.Empty, _cache.Object, _logger.Object);

        _cache.Verify(c => c.EvictStamp(It.IsAny<Guid>()), Times.Never);
        VerifyWarning(Times.Never());
    }

    private void VerifyWarning(Times times, string? containing = null)
    {
        var expected = containing;
        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) =>
                    expected == null || state.ToString()!.Contains(expected)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            times);
    }
}
