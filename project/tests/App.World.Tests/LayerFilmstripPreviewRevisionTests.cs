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

    [Fact]
    public async Task Rejects_blocked_render_when_revision_advances_before_completion()
    {
        var revision = 7;
        var enteredRender = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseRender = new ManualResetEventSlim();

        var renderTask = Task.Run(() => FilmstripRevisionGate.RenderIfStable(
            requested: 7,
            readRevision: () => Volatile.Read(ref revision),
            render: _ =>
            {
                enteredRender.SetResult();
                releaseRender.Wait();
                return "stale-frame";
            }));

        await enteredRender.Task.WaitAsync(System.TimeSpan.FromSeconds(5));
        Volatile.Write(ref revision, 8);
        releaseRender.Set();

        Assert.Null(await renderTask.WaitAsync(System.TimeSpan.FromSeconds(5)));
    }
}
