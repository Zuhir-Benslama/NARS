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

    [Fact]
    public async Task DequeueAsync_FIFO_Order()
    {
        var queue = CreateQueue();
        var expected = new[] { 0, 1, 2 };
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
    public async Task QueueBackgroundWorkItemAsync_Full_DropsNewest()
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

        // Third write must not throw — DropWrite rejects the NEW item, so
        // items 1 and 2 survive and the caller's write fails (logged).
        await queue.QueueBackgroundWorkItemAsync(workItems[2]);

        var first = await queue.DequeueAsync(CancellationToken.None);
        var second = await queue.DequeueAsync(CancellationToken.None);
        await first(Mock.Of<IServiceProvider>(), CancellationToken.None);
        await second(Mock.Of<IServiceProvider>(), CancellationToken.None);

        Assert.Equal([1, 2], executed);
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
