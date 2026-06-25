using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Microsoft.Extensions.Logging;
using PluginArchi.Extensibility.Abstractions;
using R3;

namespace FantaSim.App.Resource.Bundle;

public sealed class BundleHost
{
    private static readonly System.Net.Http.HttpClient Http = new();

    private readonly BundleVfs _vfs;
    private readonly DllExtractor _extractor;
    private readonly BundleSceneHost _sceneHost;
    private readonly IPluginHost _pluginHost;
    private readonly ILogger _logger;
    private readonly Func<string, bool> _isCollectibleAssembly;
    private readonly Dictionary<string, LoadedBundle> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public BundleHost(
        BundleVfs vfs,
        DllExtractor extractor,
        BundleSceneHost sceneHost,
        IPluginHost pluginHost,
        ILogger logger,
        Func<string, bool> isCollectibleAssembly)
    {
        _vfs = vfs ?? throw new ArgumentNullException(nameof(vfs));
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        _sceneHost = sceneHost ?? throw new ArgumentNullException(nameof(sceneHost));
        _pluginHost = pluginHost ?? throw new ArgumentNullException(nameof(pluginHost));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _isCollectibleAssembly = isCollectibleAssembly ?? throw new ArgumentNullException(nameof(isCollectibleAssembly));
    }

    public IReadOnlyDictionary<string, LoadedBundle> Loaded => _loaded;

    public IReadOnlyList<string> ListLoaded()
        => _loaded.Keys.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray();

    public bool IsLoaded(string bundleId) => _loaded.ContainsKey(bundleId);

    public BundleManifest? GetManifest(string bundleId)
        => _loaded.TryGetValue(bundleId, out var bundle) ? bundle.Manifest : null;

    public async Task LoadAsync(string pckPath, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LoadCoreAsync(pckPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task LoadRemoteAsync(string url, CancellationToken cancellationToken = default)
    {
        var bytes = await Http.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);
        var tempDir = Path.Combine(Path.GetTempPath(), "fantasim_bundles_remote");
        Directory.CreateDirectory(tempDir);
        var fileName = Path.GetFileName(new Uri(url).LocalPath);
        var tempPath = Path.Combine(tempDir, fileName);
        await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken).ConfigureAwait(false);
        await LoadAsync(tempPath, cancellationToken).ConfigureAwait(false);
    }

    public async Task UnloadAsync(string bundleId, CancellationToken cancellationToken = default)
    {
        PluginUnloadResult? unloadResult;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            unloadResult = await UnloadCoreAsync(bundleId, detachScene: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        await VerifyOldContextCollectedAsync(bundleId, unloadResult, cancellationToken).ConfigureAwait(false);
    }

    public async Task ReloadAsync(string bundleId, CancellationToken cancellationToken = default)
    {
        PluginUnloadResult? unloadResult = null;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_loaded.TryGetValue(bundleId, out var bundle))
            {
                _logger.LogWarning("Bundle not loaded for reload: {BundleId}", bundleId);
                return;
            }

            var pckPath = bundle.PckPath;
            unloadResult = await UnloadCoreAsync(bundleId, detachScene: true, cancellationToken).ConfigureAwait(false);
            await LoadCoreAsync(pckPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        await VerifyOldContextCollectedAsync(bundleId, unloadResult, cancellationToken).ConfigureAwait(false);
    }

    public async Task ReloadByPckPathAsync(string pckPath, CancellationToken cancellationToken = default)
    {
        PluginUnloadResult? unloadResult = null;
        string? reloadedBundleId = null;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var match = _loaded.Values.FirstOrDefault(bundle =>
                string.Equals(bundle.PckPath, pckPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileName(bundle.PckPath), Path.GetFileName(pckPath), StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                await LoadCoreAsync(pckPath, cancellationToken).ConfigureAwait(false);
                return;
            }

            var bundleId = match.BundleId;
            reloadedBundleId = bundleId;
            unloadResult = await UnloadCoreAsync(bundleId, detachScene: true, cancellationToken).ConfigureAwait(false);
            await LoadCoreAsync(pckPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        if (reloadedBundleId is not null)
            await VerifyOldContextCollectedAsync(reloadedBundleId, unloadResult, cancellationToken).ConfigureAwait(false);
    }

    public async Task UnloadAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var bundleId in _loaded.Keys.ToArray())
                await UnloadCoreAsync(bundleId, detachScene: false, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task LoadCoreAsync(string pckPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var pckName = Path.GetFileNameWithoutExtension(pckPath);
        var bundleResPath = $"res://bundles/{pckName}";

        if (!_vfs.LoadPck(pckPath))
        {
            _logger.LogError("Failed to load PCK: {Path}", pckPath);
            return;
        }

        var manifest = _vfs.ReadManifest(bundleResPath);
        var bundleId = manifest?.BundleId;
        if (string.IsNullOrWhiteSpace(bundleId))
            bundleId = pckName;

        var pluginAssembly = manifest?.PluginAssembly;
        if (!string.IsNullOrWhiteSpace(pluginAssembly))
        {
            var assemblyName = Path.GetFileNameWithoutExtension(pluginAssembly);
            if (!_isCollectibleAssembly(assemblyName))
            {
                throw new InvalidOperationException(
                    $"Bundle '{bundleId}' plugin assembly '{assemblyName}' is not registered in collectible-bundles.json.");
            }
        }

        string? pluginTempDir = null;
        var pluginGroupAdded = false;
        var sceneRegistered = false;

        try
        {
            // Load the bundle's managed plugin assembly into its collectible ALC BEFORE instantiating
            // the entry scene. The scene's resident-script bindings (manifest residentScripts) resolve a
            // bundle-local type (e.g. FantaSim.App.Timeline.TimelineFace), which is only discoverable once
            // its assembly is loaded -- so the assembly must come first or the binding silently no-ops.
            if (!string.IsNullOrWhiteSpace(pluginAssembly))
            {
                var tempPath = _extractor.ExtractAllToTemp(bundleResPath, pluginAssembly);
                if (!string.IsNullOrWhiteSpace(tempPath))
                {
                    pluginTempDir = Path.GetDirectoryName(tempPath);
                    if (!string.IsNullOrWhiteSpace(pluginTempDir))
                    {
                        await _pluginHost.AddGroupAsync(bundleId, manifest?.DisplayName ?? bundleId, pluginTempDir).ConfigureAwait(false);
                        pluginGroupAdded = true;
                    }
                }
            }

            var entryScene = manifest?.EntryScene;
            if (!string.IsNullOrWhiteSpace(entryScene))
            {
                var scene = _sceneHost.InstantiateScene(bundleResPath.PathJoin(entryScene), manifest);
                if (scene is not null)
                {
                    _sceneHost.RegisterScene(bundleId, scene);
                    sceneRegistered = true;
                }
            }

            _loaded[bundleId] = new LoadedBundle(bundleId, pckPath, bundleResPath, manifest, pluginTempDir);
            _logger.LogInformation("Bundle loaded: {BundleId} from {Path}", bundleId, pckPath);
        }
        catch
        {
            if (pluginGroupAdded)
                await _pluginHost.RemoveGroupAsync(bundleId).ConfigureAwait(false);

            if (sceneRegistered)
                _sceneHost.RemoveScene(bundleId, detachFromParent: true);

            throw;
        }
    }

    private async Task<PluginUnloadResult?> UnloadCoreAsync(string bundleId, bool detachScene, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_loaded.TryGetValue(bundleId, out _))
            return null;

        PluginUnloadResult? unloadResult = null;
        try
        {
            // Prefer the diagnostic unload so we get a weak-only probe of the old ALC; fall back to the
            // plain unload when the host doesn't implement it (older PluginArchi). Same unload either way.
            if (_pluginHost is IPluginHostDiagnostics diagnostics)
                unloadResult = await diagnostics.RemoveGroupWithDiagnosticsAsync(bundleId).ConfigureAwait(false);
            else
                await _pluginHost.RemoveGroupAsync(bundleId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove plugin group {BundleId}", bundleId);
        }

        _sceneHost.RemoveScene(bundleId, detachScene);
        _loaded.Remove(bundleId);
        _logger.LogInformation("Bundle unloaded: {BundleId}", bundleId);
        return unloadResult;
    }

    private async Task VerifyOldContextCollectedAsync(
        string bundleId,
        PluginUnloadResult? unloadResult,
        CancellationToken cancellationToken)
    {
        // No diagnostic available (older host) or nothing was unloaded -> nothing to verify.
        if (unloadResult is null || !unloadResult.UnloadInitiated)
            return;

        // The SOUND "bundle unloaded" signal is the old collectible ALC being GC-collected -- NOT a
        // successful Directory.Delete (on macOS/Linux, unlinking a still-mapped DLL succeeds even while
        // the ALC is alive). A resident holder typically drops its ref a few frames after unload
        // (cross-alc-rules R4), so poll on a bounded cadence and force a GC on each check.
        // The retry loop lives HERE in the host -- PluginArchi never loops (no stop-the-world GC in shared code).
        //
        // GATE-TIMING FIX (mirrors the xUnit-proven ReloadPolicy in App.Resource): the old gate probed
        // `IsCollected` on a `Task.Delay` loop SYNCHRONOUSLY inside the live reload call stack, so
        // transient refs (the in-flight async state machine, R4 deferred holders) pinned the ALC
        // during the check and produced a false "still pinned". Deferring each probe to the NEXT FRAME
        // via R3 releases the in-flight stack before the probe runs. The autoloaded
        // FrameProviderDispatcher (the lead installs it in complete-app) sets
        // ObservableSystem.DefaultFrameProvider to GodotFrameProvider.Process, so NextFrame ticks on
        // real Godot frames. In a non-Godot/headless context the R3 default still ticks, so this stays
        // buildable + testable. Go through ObservableSystem.DefaultFrameProvider -- do NOT reference
        // GodotFrameProvider directly (it lives in the addon source, not a referenced assembly).
        const int maxAttempts = 60; // frames are ~16ms; 60 frames ~= 1s worst case

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            // Defer to the NEXT FRAME so the in-flight reload async machinery is released first --
            // the old gate probed synchronously inside the live reload stack and got a false negative.
            await Observable.NextFrame(ObservableSystem.DefaultFrameProvider, cancellationToken)
                .FirstAsync(cancellationToken).ConfigureAwait(false);

            // forceGc every attempt -- matches ReloadPolicy (the probe is meaningless without a fresh
            // GC since IsCollected only reports true after the ALC is actually unreferenced+collected).
            if (unloadResult.IsCollected(forceGc: true))
            {
                _logger.LogInformation("Hot-reload: old ALC collected for bundle {BundleId}", bundleId);
                return;
            }
        }

        _logger.LogWarning(
            "Hot-reload: old ALC still pinned for bundle {BundleId} after unload (reload degraded -- a strong ref is holding the collectible context)",
            bundleId);
    }
}

public sealed record LoadedBundle(
    string BundleId,
    string PckPath,
    string BundleResPath,
    BundleManifest? Manifest,
    string? PluginTempDir = null);
