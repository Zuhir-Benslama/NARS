using System.Threading.Channels;

namespace NarsApi.Infrastructure;

/// <summary>
/// Simple in-memory background task queue using System.Threading.Channels.
/// Tasks are processed by a hosted service in FIFO order with error logging.
/// This replaces fire-and-forget _ = Task.Run() patterns that silently
/// swallow exceptions and risk ObjectDisposedException during shutdown.
/// </summary>
public interface IBackgroundTaskQueue
{
    ValueTask QueueBackgroundWorkItemAsync(Func<IServiceProvider, CancellationToken, Task> workItem);
    // Channel.Reader.ReadAsync never returns null — it throws OperationCanceledException
    // on cancellation. The return type is non-nullable to make this contract explicit.
    ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken ct);
}

/// <summary>
/// Channel-based background task queue implementation.
/// </summary>
public class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly Channel<Func<IServiceProvider, CancellationToken, Task>> _queue;

    public BackgroundTaskQueue(int capacity = 100)
    {
        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = true,
        };
        _queue = Channel.CreateBounded<Func<IServiceProvider, CancellationToken, Task>>(options);
    }

    public ValueTask QueueBackgroundWorkItemAsync(Func<IServiceProvider, CancellationToken, Task> workItem)
    {
        if (workItem is null) throw new ArgumentNullException(nameof(workItem));
        return _queue.Writer.WriteAsync(workItem);
    }

    public async ValueTask<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken ct)
    {
        var workItem = await _queue.Reader.ReadAsync(ct);
        return workItem;
    }
}

/// <summary>
/// Hosted service that processes background tasks from the queue.
/// Runs continuously while the application is alive.
/// </summary>
public class BackgroundQueueProcessor(
    IBackgroundTaskQueue queue,
    IServiceProvider services,
    ILogger<BackgroundQueueProcessor> logger) : IHostedService
{
    private Task? _executingTask;
    private readonly CancellationTokenSource _shutdown = new();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _executingTask = ProcessQueueAsync(_shutdown.Token);
        if (_executingTask.IsCompleted)
            return _executingTask;
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _shutdown.Cancel();
        if (_executingTask != null)
            await Task.WhenAny(_executingTask, Task.Delay(Timeout.Infinite, cancellationToken));
    }

    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var workItem = await queue.DequeueAsync(ct);
                if (workItem is null) continue;

                await workItem(services, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Graceful shutdown — stop processing
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background task queue item failed");
            }
        }
    }
}
