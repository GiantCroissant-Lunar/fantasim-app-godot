#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PluginArchi.Extensibility.Abstractions;
using R3;

namespace FantaSim.App.Resource;

// Outcome of a frame-deferred reload+collection probe. ProbeAvailable=false means the
// unload never initiated (nothing to probe); Collected=true means the old ALC was
// confirmed collected after at most `Attempts` frame-deferred probes.
public readonly record struct ReloadResult(bool ProbeAvailable, bool Collected, int Attempts);

// Pure-C# reload policy (NO Godot). Orchestrates the three seams the plan extracts:
//   unmount (view host drops the bundle-typed managed ref)
//   -> unloadReload (ALC unload + reload, returns a weak-only PluginUnloadResult probe)
//   -> NextFrame-deferred GC probe (the fix for the false "still pinned" gate).
//
// The probe is deferred to the NEXT FRAME so the in-flight reload async machinery is
// released before probing. The old gate (BundleHost.VerifyOldContextCollectedAsync) ran
// synchronously inside the live reload stack and got a false negative because transient
// refs (the async state machine, deferred holders) still pinned the ALC during the check.
//
// Production wires the real Godot FrameProvider + the real BundleHost/ViewHost closures
// (S2b/S3). Tests inject R3.FakeFrameProvider + delegate stand-ins (plain xUnit, no Godot).
public sealed class ReloadPolicy
{
    private readonly FrameProvider _frameProvider;
    private readonly ILogger? _logger;

    public ReloadPolicy(FrameProvider frameProvider, ILogger? logger = null)
    {
        _frameProvider = frameProvider;
        _logger = logger;
    }

    public async Task<ReloadResult> ReloadAsync(
        string bundleId,
        Func<CancellationToken, Task> unmount,
        Func<CancellationToken, Task<PluginUnloadResult?>> unloadReload,
        int maxAttempts = 8,
        CancellationToken ct = default)
    {
        _logger?.LogDebug("ReloadAsync: bundleId={BundleId} -- unmount then unloadReload", bundleId);

        await unmount(ct).ConfigureAwait(false);
        var probe = await unloadReload(ct).ConfigureAwait(false);
        return await VerifyCollectedAsync(bundleId, probe, maxAttempts, ct).ConfigureAwait(false);
    }

    // The frame-deferred collection probe, standalone: BundleHost's post-reload verification calls
    // this directly (2026-07-03 fix — its 79bc07e Task.Delay loop probed from a threadpool thread
    // with no frame coordination, reporting false "still pinned" while deferred main-thread cleanup
    // was still queued). Each attempt suspends on Observable.NextFrame so the Godot main loop
    // processes CallDeferred cleanup (node QueueFree, view unmounts) between probes.
    public async Task<ReloadResult> VerifyCollectedAsync(
        string bundleId,
        PluginUnloadResult? probe,
        int maxAttempts = 8,
        CancellationToken ct = default)
    {
        if (probe is null || !probe.UnloadInitiated)
            return new ReloadResult(ProbeAvailable: false, Collected: false, Attempts: 0);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            // Defer the probe to the NEXT FRAME so the in-flight reload async machinery is
            // released first -- the old gate probed synchronously inside the live reload stack
            // and got a false "still pinned".
            await Observable.NextFrame(_frameProvider, ct).FirstAsync(ct).ConfigureAwait(false);
            if (probe.IsCollected(forceGc: true))
            {
                _logger?.LogDebug(
                    "VerifyCollectedAsync: bundleId={BundleId} collected at attempt {Attempt}",
                    bundleId, attempt);
                return new ReloadResult(true, true, attempt);
            }
        }

        _logger?.LogWarning(
            "VerifyCollectedAsync: bundleId={BundleId} NOT collected after {Attempts} frame-deferred attempts",
            bundleId, maxAttempts);
        return new ReloadResult(true, false, maxAttempts);
    }
}