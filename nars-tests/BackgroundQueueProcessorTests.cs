using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NarsApi.Infrastructure;
using Xunit;

namespace NarsApi.Tests;

public class BackgroundQueueProcessorTests
{
    // Generous real-time ceiling so a hung worker fails the test without
    // spuriously timing out on a loaded CI runner. The TCS below is the real
    // completion signal; this only guards against a deadlock.
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

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
    public async Task ProcessBackgroundWorkItem_ExecutesQueuedItem()
    {
        var (processor, queue) = CreateProcessor();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await processor.StartAsync(CancellationToken.None);

        await queue.QueueBackgroundWorkItemAsync((_, _) =>
        {
            tcs.SetResult();
            return Task.CompletedTask;
        });

        await tcs.Task.WaitAsync(TestTimeout);
        await processor.StopAsync(CancellationToken.None);
        await processor.DisposeAsync();
    }

    [Fact]
    public async Task ProcessBackgroundWorkItem_ContinuesAfterWorkItemThrows()
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

        await secondItemExecuted.Task.WaitAsync(TestTimeout);
        await processor.StopAsync(CancellationToken.None);
        await processor.DisposeAsync();
    }

    [Fact]
    public async Task StopAsync_CompletesGracefully()
    {
        // Use a long grace period so the negative assertion below cannot be
        // tripped by a slow scheduler: stopTask stays incomplete until the
        // in-flight item is released, no matter how long the runner pauses.
        var (processor, queue) = CreateProcessor(gracePeriodSeconds: 30);

        // Start and verify processor is running.
        await processor.StartAsync(CancellationToken.None);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var workItemFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await queue.QueueBackgroundWorkItemAsync(async (_, _) =>
        {
            started.SetResult();
            try
            {
                await allowCompletion.Task;
            }
            finally
            {
                workItemFinished.SetResult();
            }
        });
        await started.Task.WaitAsync(TestTimeout);

        // Stop while the in-flight item is still running — StopAsync must wait for
        // it within the grace period rather than aborting it immediately.
        var stopTask = processor.StopAsync(CancellationToken.None);
        await Task.Yield();
        Assert.False(stopTask.IsCompleted, "StopAsync must wait for the in-flight work item");

        allowCompletion.SetResult();
        await workItemFinished.Task.WaitAsync(TestTimeout);
        await stopTask.WaitAsync(TestTimeout);
        await processor.DisposeAsync();
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
