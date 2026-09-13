using System.Data;
using System.Data.Common;
using Moq;
using NarsApi.Infrastructure;
using Xunit;

namespace NarsApi.Tests;

/// <summary>
/// Unit tests for <see cref="DbConnectionExtensions.EnsureOpenAsync"/> using a
/// mocked <see cref="DbConnection"/> so no network is involved. Verifies the
/// open/close ownership contract of the returned <see cref="ConnectionHandle"/>.
/// </summary>
public class DbConnectionExtensionsTests
{
    [Fact]
    public async Task EnsureOpenAsync_ClosedConnection_OpensAndDisposeCloses()
    {
        var conn = new Mock<DbConnection>();
        conn.SetupSequence(c => c.State)
            .Returns(ConnectionState.Closed)   // read before opening
            .Returns(ConnectionState.Open);    // read at dispose time
        conn.Setup(c => c.OpenAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        conn.Setup(c => c.CloseAsync()).Returns(Task.CompletedTask);

        await using var handle = await conn.Object.EnsureOpenAsync();

        conn.Verify(c => c.OpenAsync(It.IsAny<CancellationToken>()), Times.Once);
        await handle.DisposeAsync();
        conn.Verify(c => c.CloseAsync(), Times.Once);
    }

    [Fact]
    public async Task EnsureOpenAsync_AlreadyOpenConnection_DoesNotReopenOrClose()
    {
        var conn = new Mock<DbConnection>();
        conn.SetupGet(c => c.State).Returns(ConnectionState.Open);

        await using var handle = await conn.Object.EnsureOpenAsync();

        conn.Verify(c => c.OpenAsync(It.IsAny<CancellationToken>()), Times.Never);
        await handle.DisposeAsync();
        conn.Verify(c => c.CloseAsync(), Times.Never);
    }

    [Fact]
    public async Task EnsureOpenAsync_PropagatesCancellation()
    {
        var conn = new Mock<DbConnection>();
        conn.SetupGet(c => c.State).Returns(ConnectionState.Closed);
        conn.Setup(c => c.OpenAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.FromCanceled(new CancellationToken(true)));

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            conn.Object.EnsureOpenAsync(new CancellationToken(true)));
    }
}
