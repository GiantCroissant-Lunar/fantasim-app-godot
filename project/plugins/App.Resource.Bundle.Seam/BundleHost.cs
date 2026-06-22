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
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await UnloadCoreAsync(bundleId, detachScene: true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ReloadAsync(string bundleId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_loaded.TryGetValue(bundleId, out var bundle))
            {
                _logger.LogWarning("Bundle not loaded for reload: {BundleId}", bundleId);
                return;
            }

            var pckPath = bundle.PckPath;
            await UnloadCoreAsync(bundleId, detachScene: true, cancellationToken).ConfigureAwait(false);
            await LoadCoreAsync(pckPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ReloadByPckPathAsync(string pckPath, CancellationToken cancellationToken = default)
    {
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
            await UnloadCoreAsync(bundleId, detachScene: true, cancellationToken).ConfigureAwait(false);
            await LoadCoreAsync(pckPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
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

    private async Task UnloadCoreAsync(string bundleId, bool detachScene, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_loaded.TryGetValue(bundleId, out _))
            return;

        try
        {
            await _pluginHost.RemoveGroupAsync(bundleId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove plugin group {BundleId}", bundleId);
        }

        _sceneHost.RemoveScene(bundleId, detachScene);
        _loaded.Remove(bundleId);
        _logger.LogInformation("Bundle unloaded: {BundleId}", bundleId);
    }
}

public sealed record LoadedBundle(
    string BundleId,
    string PckPath,
    string BundleResPath,
    BundleManifest? Manifest,
    string? PluginTempDir = null);
