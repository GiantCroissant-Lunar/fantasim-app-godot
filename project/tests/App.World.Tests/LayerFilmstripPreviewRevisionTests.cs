using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FantaSim.App.World.Tests;

public sealed class LayerFilmstripPreviewRevisionTests
{
    [Fact]
    public void Stable_when_requested_start_and_completed_revision_match()
    {
        var revision = 7;
        var result = FilmstripRevisionGate.RenderIfStable(
            requested: 7,
            readRevision: () => revision,
            render: start => $"frame-r{start}");

        Assert.Equal("frame-r7", result);
    }

    [Fact]
    public void Rejects_before_render_when_start_is_already_stale()
    {
        var rendered = false;
        var result = FilmstripRevisionGate.RenderIfStable(
            requested: 7,
            readRevision: () => 8,
            render: _ =>
            {
                rendered = true;
                return "should-not-render";
            });

        Assert.Null(result);
        Assert.False(rendered);
    }

    // Flaked once (2026-07-15) in a full Release suite run, then passed 25 isolated reps, 3 suite
    // runs, and 33 further suite runs under 3x CPU oversubscription without reproducing. The only
    // interleaving-dependent windows are the two 5s WaitAsync deadlines below (the first races
    // Task.Run scheduling against pool starvation from sibling tests that block pool threads).
    // The deadlines and assertions are intentionally unchanged; on timeout the failure now
    // self-explains with task/interleaving/thread-pool state so the next flake pinpoints itself.
    [Fact]
    public async Task Rejects_blocked_render_when_revision_advances_before_completion()
    {
        var revision = 7;
        var enteredRender = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseRender = new ManualResetEventSlim();
        var clock = Stopwatch.StartNew();

        var renderTask = Task.Run(() => FilmstripRevisionGate.RenderIfStable(
            requested: 7,
            readRevision: () => Volatile.Read(ref revision),
            render: _ =>
            {
                enteredRender.SetResult();
                releaseRender.Wait();
                return "stale-frame";
            }));

        try
        {
            await enteredRender.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            // Unblock the (possibly still-queued) render before `using` disposes the event, so a
            // diagnosed failure cannot also leak a pool thread stuck waiting on a disposed handle.
            releaseRender.Set();
            Assert.Fail(Diagnostics(
                "render was not entered within 5s (Task.Run work item likely queued behind a starved thread pool)",
                clock, renderTask, enteredRender, Volatile.Read(ref revision)));
        }

        var enteredAtMs = clock.ElapsedMilliseconds;
        Volatile.Write(ref revision, 8);
        releaseRender.Set();

        string? result = null;
        try
        {
            result = await renderTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            Assert.Fail(Diagnostics(
                $"render was entered at +{enteredAtMs}ms but did not complete within 5s of release",
                clock, renderTask, enteredRender, Volatile.Read(ref revision)));
        }

        Assert.Null(result);
    }

    private static string Diagnostics(
        string what,
        Stopwatch clock,
        Task renderTask,
        TaskCompletionSource enteredRender,
        int revision)
    {
        ThreadPool.GetAvailableThreads(out var availableWorkers, out var availableIo);
        ThreadPool.GetMinThreads(out var minWorkers, out _);
        ThreadPool.GetMaxThreads(out var maxWorkers, out _);
        var report = new StringBuilder()
            .AppendLine($"FilmstripRevisionGate interleaving gate failed: {what}.")
            .AppendLine($"  elapsed: {clock.ElapsedMilliseconds} ms")
            .AppendLine($"  renderTask.Status: {renderTask.Status}")
            .AppendLine($"  enteredRender.Task.Status: {enteredRender.Task.Status}")
            .AppendLine($"  revision (current): {revision}")
            .AppendLine($"  ThreadPool threads: {ThreadPool.ThreadCount}, pending work items: {ThreadPool.PendingWorkItemCount}, completed work items: {ThreadPool.CompletedWorkItemCount}")
            .AppendLine($"  worker threads available/min/max: {availableWorkers}/{minWorkers}/{maxWorkers}, io available: {availableIo}")
            .Append($"  ProcessorCount: {Environment.ProcessorCount}");
        if (renderTask.IsFaulted)
            report.AppendLine().Append($"  renderTask exception: {renderTask.Exception}");
        return report.ToString();
    }
}
