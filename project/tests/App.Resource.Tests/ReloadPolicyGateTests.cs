#nullable enable
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.Ui;
using PluginArchi.Extensibility.Abstractions;
using R3;
using Xunit;

namespace App.Resource.Tests;

// Plain-xUnit proof (no Godot, no export) that the FRAME-DEFERRED collection gate in
// ReloadPolicy reports COLLECTED once the view-host ref is dropped -- the case the old
// in-stack gate (BundleHost.VerifyOldContextCollectedAsync) reported as a false "still
// pinned" because it probed synchronously inside the live reload async stack.
//
// Reuses the S1 harness: SimpleViewHost (real-but-simple IViewHost that holds/drops the
// IViewSource managed ref) and TestViewSourceFactory (Roslyn-emitted IViewSource .dll).
// The frame source is R3.FakeFrameProvider (manual .Advance() ticks); the policy suspends
// on Observable.NextFrame and the test pumps frames until the policy task completes.
public class ReloadPolicyGateTests
{
    [Fact]
    public async Task FrameDeferredGate_CollectsAfterUnmount()
    {
        var dir = NewTempDir(nameof(FrameDeferredGate_CollectsAfterUnmount));
        try
        {
            var fake = new FakeFrameProvider();
            var policy = new FantaSim.App.Resource.ReloadPolicy(fake);
            var (host, unloadReload) = SetUp(dir, "gate");

            // unmount drops the IViewSource managed ref; the policy then defers the collection
            // probe to the next frame(s) via Observable.NextFrame before force-GC probing.
            var task = policy.ReloadAsync(
                "gate",
                ct => host.UnmountAndWaitAsync("gate", ct),
                unloadReload);

            // PUMP frames until the policy completes -- each NextFrame await needs an Advance().
            // Do NOT await `task` before pumping (would deadlock: the continuation needs a tick).
            // FakeFrameProvider.Advance() is instant; real frames are ~16ms. ALC.Unload() is
            // asynchronous and needs wall-clock time to tear down, so a small delay between
            // ticks simulates real-frame timing and lets the unload thread complete.
            // Cap iterations to avoid an infinite loop on failure.
            for (var i = 0; i < 200 && !task.IsCompleted; i++)
            {
                fake.Advance();
                await Task.Delay(25);
                await Task.Yield();
            }

            var result = await task;
            Assert.True(result.ProbeAvailable, "probe should be available (unload was initiated)");
            Assert.True(result.Collected,
                "frame-deferred gate should observe collection after the view ref is dropped");
        }
        finally
        {
            BestEffortDelete(dir);
        }
    }

    // All ALC-typed locals (dll path, alc, asm, type, source) live ONLY inside this
    // [NoInlining] helper so they do not linger on the test method's frame (which would pin
    // the ALC for the duration of the test and defeat the collection assertion).
    //
    // The returned unloadReload closure captures `alc` STRONGLY so it is still alive when
    // Unload() is called (after unmount drops the source pin). Once Unload() has been
    // initiated, the closure nulls its strong capture and returns a weak-only
    // PluginUnloadResult -- so after the closure returns, no strong ref to the ALC
    // survives and the frame-deferred probe can observe collection.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (SimpleViewHost host, Func<CancellationToken, Task<PluginUnloadResult?>> unloadReload)
        SetUp(string dir, string viewId)
    {
        var dll = TestViewSourceFactory.EmitAssembly(dir, "GenGate_" + viewId, viewId);
        var alc = new AssemblyLoadContext("s2-" + viewId, isCollectible: true);
        var asm = alc.LoadFromAssemblyPath(dll);
        var type = asm.GetType("GeneratedViews.TestViewSource")!;
        var source = (IViewSource)Activator.CreateInstance(type)!;
        var host = new SimpleViewHost();
        host.Mount(viewId, source);

        AssemblyLoadContext? capturedAlc = alc;
        Func<CancellationToken, Task<PluginUnloadResult?>> unloadReload = _ =>
        {
            if (capturedAlc is null)
                return Task.FromResult<PluginUnloadResult?>(
                    new PluginUnloadResult(viewId, unloadInitiated: false, null));
            capturedAlc.Unload();
            var result = new PluginUnloadResult(viewId, unloadInitiated: true, capturedAlc);
            capturedAlc = null;
            return Task.FromResult<PluginUnloadResult?>(result);
        };

        return (host, unloadReload);
        // asm/type/source locals fall out of scope; host keeps `source` alive until
        // UnmountAndWaitAsync drops it. `alc` is reachable only via capturedAlc until the
        // closure runs and nulls it.
    }

    private static string NewTempDir(string testName)
    {
        var dir = Path.Combine(
            Path.GetTempPath(),
            "app-resource-tests",
            testName + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void BestEffortDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
            // Best effort -- the ALC may still be tearing down. Not a test failure.
        }
    }
}