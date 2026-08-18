namespace NarsApi.Services;

/// <summary>
/// Simple in-memory background task queue using System.Threading.Channels.
/// Tasks are processed by a hosted service in FIFO order with error logging.
/// This replaces fire-and-forget _ = Task.Run() patterns that silently
/// swallow exceptions and risk ObjectDisposedException during shutdown.
/// </summary>
public interface IBackgroundTaskQueue
{
    /// <summary>
    /// Queues a background work item. Returns <c>true</c> if accepted,
    /// <c>false</c> if the queue was full and the item was dropped.
    /// </summary>
    ValueTask<bool> QueueBackgroundWorkItemAsync(Func<IServiceProvider, CancellationToken, Task> workItem);
    ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken ct);
}
