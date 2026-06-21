using System.Data.Common;

namespace NarsApi.Infrastructure;

public readonly struct ConnectionHandle(DbConnection connection, bool wasOpen) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        if (!wasOpen && connection.State == System.Data.ConnectionState.Open)
        {
            await connection.CloseAsync();
        }
    }
}

public static class DbConnectionExtensions
{
    public static async Task<ConnectionHandle> EnsureOpenAsync(this DbConnection connection, CancellationToken cancellationToken = default)
    {
        var wasOpen = connection.State == System.Data.ConnectionState.Open;
        if (!wasOpen)
        {
            await connection.OpenAsync(cancellationToken);
        }

        return new ConnectionHandle(connection, wasOpen);
    }
}
