using System;
using System.Threading;
using System.Threading.Tasks;
using NLMCL.App.ViewModels;
using Xunit;

namespace NLMCL.Tests
{
    public sealed class DownloadQueueTests
    {
        [Fact]
        public async Task Second_task_waits_until_the_first_one_finishes()
        {
            DownloadQueue queue = new DownloadQueue();
            TaskCompletionSource<bool> firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> secondStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            Task first = queue.RunAsync("版本安装", async () =>
            {
                firstStarted.TrySetResult(true);
                await releaseFirst.Task;
            }, CancellationToken.None);

            await firstStarted.Task;
            Task second = queue.RunAsync("Mod 安装", () =>
            {
                secondStarted.TrySetResult(true);
                return Task.CompletedTask;
            }, CancellationToken.None);

            await Task.Delay(100);
            Assert.Equal("版本安装", queue.ActiveTitle);
            Assert.Equal(1, queue.WaitingCount);
            Assert.False(secondStarted.Task.IsCompleted);
            Assert.Contains("排队 1", queue.Summary);

            releaseFirst.TrySetResult(true);
            await Task.WhenAll(first, second);

            Assert.Equal(string.Empty, queue.ActiveTitle);
            Assert.Equal(0, queue.WaitingCount);
            Assert.True(secondStarted.Task.IsCompleted);
        }

        [Fact]
        public async Task Cancelling_a_waiting_item_from_the_ui_removes_it()
        {
            DownloadQueue queue = new DownloadQueue();
            TaskCompletionSource<bool> firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool secondRan = false;

            Task first = queue.RunAsync("版本安装", async () =>
            {
                firstStarted.TrySetResult(true);
                await releaseFirst.Task;
            }, CancellationToken.None);

            await firstStarted.Task;
            Task second = queue.RunAsync("Mod 安装", () =>
            {
                secondRan = true;
                return Task.CompletedTask;
            }, CancellationToken.None);

            await Task.Delay(100);
            Assert.Equal(2, queue.Items.Count);
            Assert.True(queue.Items[0].IsActive);
            Assert.False(queue.Items[0].CanCancel);
            Assert.True(queue.Items[1].CanCancel);

            queue.Items[1].CancelCommand.Execute(null);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);

            Assert.Single(queue.Items);
            Assert.Equal(0, queue.WaitingCount);
            Assert.False(secondRan);

            releaseFirst.TrySetResult(true);
            await first;
            Assert.Empty(queue.Items);
        }

        [Fact]
        public async Task Cancelling_a_waiting_task_does_not_start_it()
        {
            DownloadQueue queue = new DownloadQueue();
            TaskCompletionSource<bool> firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> releaseFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool secondRan = false;

            Task first = queue.RunAsync("版本安装", async () =>
            {
                firstStarted.TrySetResult(true);
                await releaseFirst.Task;
            }, CancellationToken.None);

            await firstStarted.Task;

            using CancellationTokenSource cts = new CancellationTokenSource();
            Task second = queue.RunAsync("Mod 安装", () =>
            {
                secondRan = true;
                return Task.CompletedTask;
            }, cts.Token);

            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);

            releaseFirst.TrySetResult(true);
            await first;

            Assert.False(secondRan);
            Assert.Equal(0, queue.WaitingCount);
            Assert.Equal(string.Empty, queue.ActiveTitle);
        }
    }
}
