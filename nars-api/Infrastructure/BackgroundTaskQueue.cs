using System.Threading.Channels;
using Microsoft.Extensions.Options;
using NarsApi.Services;

namespace NarsApi.Infrastructure;

/// <summary>
/// Channel-based background task queue implementation.
/// </summary>
public class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly Channel<Func<IServiceProvider, CancellationToken, Task>> _queue;

    public BackgroundTaskQueue(IOptions<BackgroundTaskOptions> options)
    {
        var capacity = options.Value.Capacity;
        var channelOptions = new BoundedChannelOptions(capacity)
        {
            // Drop oldest when queue is full to avoid blocking the HTTP request path.
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = true,
        };
        _queue = Channel.CreateBounded<Func<IServiceProvider, CancellationToken, Task>>(channelOptions);
    }

    public ValueTask QueueBackgroundWorkItemAsync(Func<IServiceProvider, CancellationToken, Task> workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

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
    ILogger<BackgroundQueueProcessor> logger) : IHostedService, IAsyncDisposable
{
    private Task? _executingTask;
    private readonly CancellationTokenSource _shutdown = new();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _executingTask = ProcessQueueAsync(_shutdown.Token);
        if (_executingTask.IsCompleted)
        {
            return _executingTask;
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _shutdown.CancelAsync();
        if (_executingTask != null)
        {
            try
            {
                // Give in-flight task a grace period to complete before forcing shutdown.
                using var graceCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _executingTask.WaitAsync(graceCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Grace period or shutdown timeout elapsed — runtime will force-stop.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task ProcessQueueAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var workItem = await queue.DequeueAsync(ct);
                await using var scope = services.CreateAsyncScope();
                await workItem(scope.ServiceProvider, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Background task queue item failed");
            }
        }
    }
}
