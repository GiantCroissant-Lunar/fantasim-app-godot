using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.Resource;
using FantaSim.App.Resource.Providers;
using Godot;
using Microsoft.Extensions.Logging;
using PluginArchi.Extensibility.Abstractions;

namespace FantaSim.App.Resource.Bundle;

public sealed class BundleProvider : IProvider
{
    private readonly BundleHost _host;

    public BundleProvider(
        Node sceneRoot,
        IPluginHost pluginHost,
        ILoggerFactory loggerFactory,
        Func<string, bool> isCollectibleAssembly)
    {
        if (sceneRoot is null) throw new ArgumentNullException(nameof(sceneRoot));
        if (pluginHost is null) throw new ArgumentNullException(nameof(pluginHost));
        if (loggerFactory is null) throw new ArgumentNullException(nameof(loggerFactory));
        if (isCollectibleAssembly is null) throw new ArgumentNullException(nameof(isCollectibleAssembly));

        var vfs = new BundleVfs();
        var extractor = new DllExtractor();
        var sceneHost = new BundleSceneHost(sceneRoot, loggerFactory.CreateLogger("BundleSceneHost"));
        _host = new BundleHost(vfs, extractor, sceneHost, pluginHost, loggerFactory.CreateLogger("BundleHost"), isCollectibleAssembly);
    }

    public Task LoadAsync(string path, CancellationToken cancellationToken = default)
        => _host.LoadAsync(path, cancellationToken);

    public Task LoadRemoteAsync(string url, CancellationToken cancellationToken = default)
        => _host.LoadRemoteAsync(url, cancellationToken);

    public Task UnloadAsync(string id, CancellationToken cancellationToken = default)
        => _host.UnloadAsync(id, cancellationToken);

    public Task ReloadAsync(string id, CancellationToken cancellationToken = default)
        => _host.ReloadAsync(id, cancellationToken);

    public Task ReloadByPathAsync(string path, CancellationToken cancellationToken = default)
        => _host.ReloadByPckPathAsync(path, cancellationToken);

    public Task UnloadAllAsync(CancellationToken cancellationToken = default)
        => _host.UnloadAllAsync(cancellationToken);

    public IReadOnlyList<string> ListLoaded() => _host.ListLoaded();

    public IReadOnlyList<ResourceEntry> ListEntries()
        => _host.Loaded.Values
            .Select(ToResourceEntry)
            .OrderBy(entry => entry.BundleId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public bool IsLoaded(string id) => _host.IsLoaded(id);

    public IResourceManifest? GetManifest(string id) => _host.GetManifest(id);

    private static ResourceEntry ToResourceEntry(LoadedBundle bundle)
    {
        var manifest = bundle.Manifest;
        var entryScenes = manifest is null
            ? Array.Empty<string>()
            : ResolveEntryScenes(manifest).ToArray();
        var managedAssemblies = manifest is null
            ? Array.Empty<string>()
            : ResolveManagedAssemblies(manifest).ToArray();

        return new ResourceEntry(
            bundle.BundleId,
            manifest?.DisplayName ?? bundle.BundleId,
            manifest?.Version ?? string.Empty,
            bundle.PckPath,
            bundle.BundleResPath,
            entryScenes,
            managedAssemblies,
            bundle.PluginTempDir,
            "Loaded");
    }

    private static IEnumerable<string> ResolveEntryScenes(BundleManifest manifest)
    {
        if (!string.IsNullOrWhiteSpace(manifest.EntryScene))
            yield return manifest.EntryScene;

        if (manifest.Content?.Pcks is null)
            yield break;

        foreach (var scene in manifest.Content.Pcks.SelectMany(pck => pck.EntryScenes))
        {
            if (!string.IsNullOrWhiteSpace(scene) && scene != manifest.EntryScene)
                yield return scene;
        }
    }

    private static IEnumerable<string> ResolveManagedAssemblies(BundleManifest manifest)
    {
        if (!string.IsNullOrWhiteSpace(manifest.PluginAssembly))
            yield return manifest.PluginAssembly;

        if (manifest.Managed?.Assemblies is null)
            yield break;

        foreach (var assembly in manifest.Managed.Assemblies.Select(assembly => assembly.Uri))
        {
            if (!string.IsNullOrWhiteSpace(assembly) && assembly != manifest.PluginAssembly)
                yield return assembly;
        }
    }
}
