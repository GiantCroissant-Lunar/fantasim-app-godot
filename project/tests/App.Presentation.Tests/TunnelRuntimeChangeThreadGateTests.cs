using System;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.Presentation.Tunnel;
using Xunit;

namespace App.Presentation.Tests;

public sealed class TunnelRuntimeChangeThreadGateTests
{
    [Fact]
    public void MainThread_RunsInlineWithoutDeferring()
    {
        var deferredCalls = 0;
        var applied = 0;

        TunnelRuntimeChangeThreadGate.Run(
            isMainThread: () => true,
            deferToMainThread: _ => deferredCalls++,
            applyOnMainThread: () => applied++);

        Assert.Equal(0, deferredCalls);
        Assert.Equal(1, applied);
    }

    [Fact]
    public async Task WorkerThread_DoesNotReturnBeforeQueuedMainThreadWorkCompletes()
    {
        using var queuedReady = new ManualResetEventSlim();
        Action? queued = null;
        var returned = 0;
        var applied = 0;

        var worker = Task.Run(() =>
        {
            TunnelRuntimeChangeThreadGate.Run(
                isMainThread: () => false,
                deferToMainThread: action =>
                {
                    queued = action;
                    queuedReady.Set();
                },
                applyOnMainThread: () => applied++);
            Interlocked.Exchange(ref returned, 1);
        });

        Assert.True(queuedReady.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, Volatile.Read(ref returned));
        Assert.Equal(0, applied);

        queued!();
        await worker.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, applied);
        Assert.Equal(1, Volatile.Read(ref returned));
    }

    [Fact]
    public async Task WorkerThread_ReceivesExceptionRaisedByMainThreadWork()
    {
        using var queuedReady = new ManualResetEventSlim();
        Action? queued = null;

        var worker = Task.Run(() => TunnelRuntimeChangeThreadGate.Run(
            isMainThread: () => false,
            deferToMainThread: action =>
            {
                queued = action;
                queuedReady.Set();
            },
            applyOnMainThread: () => throw new InvalidOperationException("teardown failed")));

        Assert.True(queuedReady.Wait(TimeSpan.FromSeconds(5)));
        queued!();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await worker.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("teardown failed", error.Message);
    }
}
