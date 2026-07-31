using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NarsApi.Infrastructure;
using Xunit;

namespace NarsApi.Tests;

public class BackgroundTaskQueueTests
{
    private static BackgroundTaskQueue CreateQueue(int capacity = 10) => new(
            Options.Create(new BackgroundTaskOptions { Capacity = capacity, GracePeriodSeconds = 5 }),
            Mock.Of<ILogger<BackgroundTaskQueue>>());

    [Fact]
    public async Task DequeueAsync_ReturnsQueuedItem()
    {
        var queue = CreateQueue();
        Func<IServiceProvider, CancellationToken, Task> workItem = (_, _) => Task.CompletedTask;

        await queue.QueueBackgroundWorkItemAsync(workItem);
        var dequeued = await queue.DequeueAsync(CancellationToken.None);

        Assert.Same(workItem, dequeued);
    }

    private static readonly int[] expected = [0, 1, 2];

    [Fact]
    public async Task DequeueAsync_FIFO_Order()
    {
        var queue = CreateQueue();
        var order = new List<int>();

        for (var i = 0; i < 3; i++)
        {
            var captured = i;
            await queue.QueueBackgroundWorkItemAsync((_, _) =>
            {
                order.Add(captured);
                return Task.CompletedTask;
            });
        }

        for (var i = 0; i < 3; i++)
        {
            var item = await queue.DequeueAsync(CancellationToken.None);
            await item(Mock.Of<IServiceProvider>(), CancellationToken.None);
        }

        Assert.Equal(expected, order);
    }

    [Fact]
    public async Task QueueBackgroundWorkItemAsync_Null_Throws()
    {
        var queue = CreateQueue();
        await Assert.ThrowsAnyAsync<ArgumentNullException>(
            async () => await queue.QueueBackgroundWorkItemAsync(null!));
    }

    [Fact]
    public async Task QueueBackgroundWorkItemAsync_Full_DropsOldest()
    {
        var queue = CreateQueue(capacity: 2);
        var executed = new List<int>();

        Func<IServiceProvider, CancellationToken, Task>[] workItems = [
            (_, _) => { executed.Add(1); return Task.CompletedTask; },
            (_, _) => { executed.Add(2); return Task.CompletedTask; },
            (_, _) => { executed.Add(3); return Task.CompletedTask; },
        ];

        // Fill the queue to capacity.
        await queue.QueueBackgroundWorkItemAsync(workItems[0]);
        await queue.QueueBackgroundWorkItemAsync(workItems[1]);

        // Third write must not throw — DropOldest makes room by evicting
        // the FIRST item, so items 2 and 3 survive.
        await queue.QueueBackgroundWorkItemAsync(workItems[2]);

        var first = await queue.DequeueAsync(CancellationToken.None);
        var second = await queue.DequeueAsync(CancellationToken.None);
        await first(Mock.Of<IServiceProvider>(), CancellationToken.None);
        await second(Mock.Of<IServiceProvider>(), CancellationToken.None);

        Assert.Equal([2, 3], executed);
    }

    [Fact]
    public async Task DequeueAsync_Cancellation_Throws()
    {
        var queue = CreateQueue();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await queue.DequeueAsync(cts.Token));
    }
}

public class BackgroundQueueProcessorTests
{
    private static (BackgroundQueueProcessor processor, BackgroundTaskQueue queue) CreateProcessor(
        int capacity = 10,
        int gracePeriodSeconds = 2)
    {
        var queue = new BackgroundTaskQueue(
            Options.Create(new BackgroundTaskOptions { Capacity = capacity, GracePeriodSeconds = gracePeriodSeconds }),
            Mock.Of<ILogger<BackgroundTaskQueue>>());

        var services = new ServiceCollection().BuildServiceProvider();
        var processor = new BackgroundQueueProcessor(
            queue,
            services,
            Options.Create(new BackgroundTaskOptions { GracePeriodSeconds = gracePeriodSeconds }),
            Mock.Of<ILogger<BackgroundQueueProcessor>>());

        return (processor, queue);
    }

    [Fact]
    public async Task ProcessesQueuedWorkItem()
    {
        var (processor, queue) = CreateProcessor();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await processor.StartAsync(CancellationToken.None);

        await queue.QueueBackgroundWorkItemAsync((_, _) =>
        {
            tcs.SetResult();
            return Task.CompletedTask;
        });

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(tcs.Task.IsCompletedSuccessfully);
        await processor.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ContinuesAfterWorkItemThrows()
    {
        var (processor, queue) = CreateProcessor();
        var secondItemExecuted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await processor.StartAsync(CancellationToken.None);

        // First item throws — processor should not die.
        await queue.QueueBackgroundWorkItemAsync((_, _) =>
            throw new InvalidOperationException("boom"));

        await queue.QueueBackgroundWorkItemAsync((_, _) =>
        {
            secondItemExecuted.SetResult();
            return Task.CompletedTask;
        });

        await secondItemExecuted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(secondItemExecuted.Task.IsCompletedSuccessfully);
        await processor.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_CompletesGracefully()
    {
        var (processor, queue) = CreateProcessor(gracePeriodSeconds: 1);

        // Start and verify processor is running.
        await processor.StartAsync(CancellationToken.None);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await queue.QueueBackgroundWorkItemAsync((_, _) =>
        {
            started.SetResult();
            return Task.CompletedTask;
        });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(started.Task.IsCompletedSuccessfully);

        // Stop — should not hang.
        await processor.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task DisposeAsync_DoesNotThrow()
    {
        var (processor, queue) = CreateProcessor();

        // Should not throw.
        await processor.DisposeAsync();

        // The queue is independent of the processor — verify it still works.
        await queue.QueueBackgroundWorkItemAsync((_, _) => Task.CompletedTask);
        var item = await queue.DequeueAsync(CancellationToken.None);
        Assert.NotNull(item);
    }
}
