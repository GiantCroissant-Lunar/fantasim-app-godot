#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.Resource;
using FantaSim.App.Ui;
using Godot;
using PluginArchi.Extensibility.Abstractions;
using R3;

namespace CodeQuality;

// Headless code-quality / integration runner. Boots under `godot --headless`, runs the checks,
// and quits with 0 (passed) or 1 (failed) so CI / `task` can gate.
//
// Step C: the REAL reload-collection check. The headless analog of the S2a xUnit test
// (ReloadPolicyGateTests), but the frame source is the REAL GodotFrameProvider.Process
// (ticked each frame by the FrameProviderDispatcher autoload) instead of FakeFrameProvider.
// It proves the frame-deferred ReloadPolicy gate observes ALC collection on REAL Godot frames.
public partial class CodeQualityRunner : Node
{
    public override void _Ready() => _ = RunAsync();

    private async Task RunAsync()
    {
        var exitCode = 0;
        try
        {
            GD.Print("[code-quality] S3C starting: real reload-collection check on GodotFrameProvider.Process");

            // Mirror ReloadPolicyGateTests: the policy's frame source is the REAL Godot frame
            // provider (autoload ticks it every real frame). Build the host + unloadReload via
            // the NoInlining SetUp helper so no ALC-typed local lingers on this frame.
            var (host, unloadReload) = SetUp(Path.GetTempPath(), "s3c");

            var policy = new ReloadPolicy(GodotFrameProvider.Process);

            // Because the FrameProviderDispatcher autoload ticks GodotFrameProvider.Process every
            // real frame, we AWAIT ReloadAsync directly -- NO manual fake.Advance() pump loop
            // (that was only for the xUnit FakeFrameProvider). maxAttempts=60 mirrors the xUnit
            // test's generous pump on the real ~16ms frame loop.
            var result = await policy.ReloadAsync(
                "s3c",
                ct => host.UnmountAndWaitAsync("s3c", ct),
                unloadReload,
                maxAttempts: 60);

            if (result.ProbeAvailable && result.Collected)
            {
                GD.Print($"[code-quality] S3C reload-collection: Collected=true attempts={result.Attempts}");
                exitCode = 0;
            }
            else
            {
                GD.PrintErr($"[code-quality] S3C FAILED: ProbeAvailable={result.ProbeAvailable} Collected={result.Collected}");
                exitCode = 1;
            }
        }
        catch (Exception ex)
        {
            exitCode = 1;
            GD.PrintErr($"[code-quality] FAILED: {ex}");
        }

        GD.Print($"[code-quality] done, exitCode={exitCode}");
        GetTree().Quit(exitCode);
    }

    // All ALC-typed locals (dll path, alc, asm, type, source) live ONLY inside this
    // [NoInlining] helper so they do not linger on RunAsync's frame (which would pin the
    // ALC for the duration of the check and defeat the collection assertion).
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
        var tempDir = Path.Combine(dir, "code-quality", "s3c-" + Guid.NewGuid().ToString("N"));
        var dll = TestViewSourceFactory.EmitAssembly(tempDir, "GenS3C_" + viewId, viewId);
        var alc = new AssemblyLoadContext("s3c-" + viewId, isCollectible: true);
        // Godot loads the game assemblies into its OWN ALC (not the Default ALC), so the
        // collectible ALC's default fallback cannot resolve the emitted view's dependencies
        // (FantaSim.App.Ui.Contracts, BoomHud.Abstractions). Share the host's already-loaded
        // assemblies across the collectible boundary -- this binds the emitted IViewSource to
        // the HOST's contract type (so the cast below works) and keeps those deps OUT of the
        // collectible ALC (only the emitted view unloads). Mirrors the bundle SharedAssemblyPolicy.
        alc.Resolving += (_, name) => AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => !a.IsDynamic && a.GetName().Name == name.Name);
        var asm = alc.LoadFromAssemblyPath(dll);
        var type = asm.GetType("GeneratedViews.TestViewSource", throwOnError: true)!;
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
}