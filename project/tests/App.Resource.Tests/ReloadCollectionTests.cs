#nullable enable
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Threading.Tasks;
using FantaSim.App.Ui;
using Xunit;

namespace App.Resource.Tests;

// Plain-xUnit proof (no Godot, no export) that the ALC pin is a managed reference: a
// real-but-simple in-memory IViewSource pins the bundle's collectible ALC; dropping it
// (unmount) lets the ALC collect. Replaces the false-green FakeViewHost.
public class ReloadCollectionTests
{
    [Fact]
    public void Held_WhenViewHostHoldsSource_NotCollected()
    {
        var tempDir = NewTempDir(nameof(Held_WhenViewHostHoldsSource_NotCollected));
        try
        {
            var (weak, host) = MountIntoFreshAlc(tempDir, "held");

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.True(weak.IsAlive, "ALC should stay pinned while the view host still holds the source.");
            GC.KeepAlive(host);
        }
        finally
        {
            BestEffortDelete(tempDir);
        }
    }

    [Fact]
    public async Task Dropped_WhenViewHostUnmounts_Collected()
    {
        var tempDir = NewTempDir(nameof(Dropped_WhenViewHostUnmounts_Collected));
        try
        {
            var (weak, host) = MountIntoFreshAlc(tempDir, "drop");

            await host.UnmountAndWaitAsync("drop");

            for (var attempt = 0; attempt < 10; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                if (!weak.IsAlive)
                {
                    break;
                }

                await Task.Delay(25);
            }

            Assert.False(weak.IsAlive, "ALC should collect once the view host drops the source.");
        }
        finally
        {
            BestEffortDelete(tempDir);
        }
    }

    // All ALC-typed locals live only inside this [NoInlining] helper so they do not linger on
    // the test method's stack frame (which would keep the ALC reachable for the duration of
    // the test, defeating the dropped-pin assertion).
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference alcRef, SimpleViewHost host) MountIntoFreshAlc(string dir, string viewId)
    {
        var dll = TestViewSourceFactory.EmitAssembly(dir, "GenView_" + viewId, viewId);

        var alc = new AssemblyLoadContext("s1-" + viewId, isCollectible: true);
        var asm = alc.LoadFromAssemblyPath(dll);
        var type = asm.GetType("GeneratedViews.TestViewSource")!;
        var source = (IViewSource)Activator.CreateInstance(type)!;

        var host = new SimpleViewHost();
        host.Mount(viewId, source);

        var weak = new WeakReference(alc);

        // Initiate unload. The host still holds `source` (the managed pin), so the ALC stays
        // reachable until UnmountAndWaitAsync drops it.
        alc.Unload();

        return (weak, host);
    }

    private static string NewTempDir(string testName)
    {
        var dir = Path.Combine(Path.GetTempPath(), "app-resource-tests", testName + "-" + Guid.NewGuid().ToString("N"));
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