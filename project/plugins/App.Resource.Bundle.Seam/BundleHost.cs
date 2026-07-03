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

namespace FantaSim.App.Resource.Bundle;

public sealed class BundleHost
{
    private static readonly System.Net.Http.HttpClient Http = new();

    private readonly BundleVfs _vfs;
    private readonly BundleExtractor _extractor;
    private readonly BundleSceneHost _sceneHost;
    private readonly IPluginHost _pluginHost;
    private readonly ILogger _logger;
    private readonly Func<string, bool> _isCollectibleAssembly;
    private readonly Dictionary<string, LoadedBundle> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public BundleHost(
        BundleVfs vfs,
        BundleExtractor extractor,
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
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await LoadCoreAsync(pckPath, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task LoadRemoteAsync(string url, CancellationToken cancellationToken = default)
    {
        var bytes = await Http.GetByteArrayAsync(url, cancellationToken);
        var tempDir = Path.Combine(Path.GetTempPath(), "fantasim_bundles_remote");
        Directory.CreateDirectory(tempDir);
        var fileName = Path.GetFileName(new Uri(url).LocalPath);
        var tempPath = Path.Combine(tempDir, fileName);
        await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken);
        await LoadAsync(tempPath, cancellationToken);
    }

    public async Task UnloadAsync(string bundleId, CancellationToken cancellationToken = default)
    {
        PluginUnloadResult? unloadResult;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            unloadResult = await UnloadCoreAsync(bundleId, detachScene: true, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        QueueOldContextCollectionVerification(bundleId, unloadResult, cancellationToken);
    }

    public async Task ReloadAsync(string bundleId, CancellationToken cancellationToken = default)
    {
        PluginUnloadResult? unloadResult = null;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_loaded.TryGetValue(bundleId, out var bundle))
            {
                _logger.LogWarning("Bundle not loaded for reload: {BundleId}", bundleId);
                return;
            }

            var pckPath = bundle.PckPath;
            unloadResult = await UnloadCoreAsync(bundleId, detachScene: true, cancellationToken);
            await LoadCoreAsync(pckPath, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        QueueOldContextCollectionVerification(bundleId, unloadResult, cancellationToken);
    }

    public async Task ReloadByPckPathAsync(string pckPath, CancellationToken cancellationToken = default)
    {
        PluginUnloadResult? unloadResult = null;
        string? reloadedBundleId = null;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var match = _loaded.Values.FirstOrDefault(bundle =>
                string.Equals(bundle.PckPath, pckPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileName(bundle.PckPath), Path.GetFileName(pckPath), StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                await LoadCoreAsync(pckPath, cancellationToken);
                return;
            }

            var bundleId = match.BundleId;
            reloadedBundleId = bundleId;
            unloadResult = await UnloadCoreAsync(bundleId, detachScene: true, cancellationToken);
            await LoadCoreAsync(pckPath, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        if (reloadedBundleId is not null)
            QueueOldContextCollectionVerification(reloadedBundleId, unloadResult, cancellationToken);
    }

    public async Task UnloadAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var bundleId in _loaded.Keys.ToArray())
                await UnloadCoreAsync(bundleId, detachScene: false, cancellationToken);
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
                var extractionResult = _extractor.Extract(bundleResPath, pluginAssembly);
                if (!string.IsNullOrWhiteSpace(extractionResult?.PluginAssemblyPath))
                {
                    pluginTempDir = Path.GetDirectoryName(extractionResult!.PluginAssemblyPath);
                    if (!string.IsNullOrWhiteSpace(pluginTempDir))
                    {
                        _logger.LogInformation(
                            "Bundle plugin extracted: {BundleId} assembly={Assembly} dir={Directory} dataFiles={DataFileCount}",
                            bundleId,
                            pluginAssembly,
                            pluginTempDir,
                            extractionResult.DataFiles.Count);
                        await _pluginHost.AddGroupAsync(bundleId, manifest?.DisplayName ?? bundleId, pluginTempDir);
                        pluginGroupAdded = true;
                    }
                }
                else
                {
                    _logger.LogWarning(
                        "Bundle plugin assembly was not extracted: {BundleId} expected={Assembly} bundleResPath={BundleResPath}",
                        bundleId,
                        pluginAssembly,
                        bundleResPath);
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
                await _pluginHost.RemoveGroupAsync(bundleId);

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
                unloadResult = await diagnostics.RemoveGroupWithDiagnosticsAsync(bundleId);
            else
                await _pluginHost.RemoveGroupAsync(bundleId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove plugin group {BundleId}", bundleId);
        }

        // ShutdownAsync has run and ALC.Unload is initiated; drop the shared MessagePack resolver
        // cache's collectible-keyed entries so they don't root the dying context (the dump-verified
        // world-bundle pin).
        SharedMessagePackCachePurge.EvictCollectibleEntries(bundleId, _logger);

        _sceneHost.RemoveScene(bundleId, detachScene);
        _loaded.Remove(bundleId);
        _logger.LogInformation("Bundle unloaded: {BundleId}", bundleId);
        return unloadResult;
    }

    private void QueueOldContextCollectionVerification(
        string bundleId,
        PluginUnloadResult? unloadResult,
        CancellationToken cancellationToken)
    {
        if (unloadResult is null || !unloadResult.UnloadInitiated)
        {
            // Without this line a skipped probe is indistinguishable from a reload that never ran --
            // exactly the "no gate line at all" ambiguity the scene-tier diagnosis hit.
            _logger.LogWarning(
                "Hot-reload: collection probe skipped for bundle {BundleId} (unloadResult={HasResult}, unloadInitiated={Initiated})",
                bundleId,
                unloadResult is not null,
                unloadResult?.UnloadInitiated ?? false);
            return;
        }

        _ = VerifyOldContextCollectedAfterReloadReturnsAsync(bundleId, unloadResult, cancellationToken);
    }

    private async Task VerifyOldContextCollectedAfterReloadReturnsAsync(
        string bundleId,
        PluginUnloadResult unloadResult,
        CancellationToken cancellationToken)
    {
        try
        {
            // FRAME-DEFERRED gate (2026-07-03 fix; restores the S2a/S2b-proven ReloadPolicy probe
            // that 79bc07e replaced with a threadpool Task.Delay loop): each attempt suspends on
            // Observable.NextFrame via the Godot frame provider, so the main thread processes the
            // deferred resident cleanup (Callable.From(...).CallDeferred() node frees, view
            // unmounts) between probes. The Task.Delay loop probed from a threadpool thread with
            // no frame coordination and reported a false "still pinned" while that cleanup was
            // still queued. 32 frame-deferred attempts (~0.5 s @60fps) is generous — a genuinely
            // unpinned ALC collects within a few frames.
            var policy = new FantaSim.App.Resource.ReloadPolicy(
                R3.ObservableSystem.DefaultFrameProvider, _logger);
            var result = await policy.VerifyCollectedAsync(
                bundleId, unloadResult, maxAttempts: 32, cancellationToken);

            // Keep the historic gate strings EXACT — the verify-windowed workflow greps for them.
            if (result.Collected)
            {
                _logger.LogInformation("Hot-reload: old ALC collected for bundle {BundleId}", bundleId);
            }
            else if (result.ProbeAvailable)
            {
                _logger.LogWarning(
                    "Hot-reload: old ALC still pinned for bundle {BundleId} after unload (reload degraded -- a strong ref is holding the collectible context)",
                    bundleId);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hot-reload: old ALC collection verification failed for bundle {BundleId}", bundleId);
        }
    }
}

public sealed record LoadedBundle(
    string BundleId,
    string PckPath,
    string BundleResPath,
    BundleManifest? Manifest,
    string? PluginTempDir = null);
