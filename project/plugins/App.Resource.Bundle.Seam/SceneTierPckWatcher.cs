using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;

namespace FantaSim.App.Resource.Bundle;

/// <summary>
/// Resident watcher for scene-tier bundle PCKs (manifest metadata bundleType == "scene-tier":
/// stage/assist/timeline). View-tier bundles (world/activity/iii + ViewHost-mounted views) already
/// get a per-view ResourcePckWatcher; scene tiers have none because a plain Resource.ReloadAsync
/// cannot release the SceneFlow pin — they must reload via the resource.reload_bundle command
/// (SceneFlow Exit->Enter), and that handler must run on the Godot main thread. FileSystemWatcher
/// events arrive on threadpool threads, so the dispatch marshals via Callable.From(...).CallDeferred
/// (the same cross-thread entry ViewHost uses for RuntimeChanged rebinds).
/// </summary>
public sealed class SceneTierPckWatcher : IDisposable
{
    private const string SceneTierBundleType = "scene-tier";
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(500);

    private readonly IRegistry _registry;
    private readonly ILogger _log;
    private readonly FileSystemWatcher _watcher;
    private readonly object _gate = new();
    private readonly Dictionary<string, CancellationTokenSource> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _reloadSerializer = new(1, 1);
    private bool _disposed;

    public static SceneTierPckWatcher? TryCreate(IRegistry registry, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var log = loggerFactory.CreateLogger("Host.SceneTierPckWatcher");
        var resourcesDir = new FantaSim.App.Resource.Bundle.GodotBundleDirectoryResolver().ResolveResourcesDirectory();
        if (!Directory.Exists(resourcesDir))
        {
            log.LogInformation("scene-tier pck watch skipped: bundle directory '{Dir}' does not exist.", resourcesDir);
            return null;
        }

        var watcher = new SceneTierPckWatcher(registry, log, resourcesDir);
        log.LogInformation("scene-tier pck watch installed on '{Dir}'.", resourcesDir);
        return watcher;
    }

    private SceneTierPckWatcher(IRegistry registry, ILogger log, string resourcesDir)
    {
        _registry = registry;
        _log = log;
        _watcher = new FileSystemWatcher(resourcesDir, "*.pck")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };

        _watcher.Changed += OnPckChanged;
        _watcher.Created += OnPckChanged;
        _watcher.Renamed += OnPckRenamed;
    }

    private void OnPckChanged(object sender, FileSystemEventArgs e) => ScheduleReload(e.FullPath);

    private void OnPckRenamed(object sender, RenamedEventArgs e) => ScheduleReload(e.FullPath);

    private void ScheduleReload(string path)
    {
        var bundleId = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(bundleId))
            return;

        lock (_gate)
        {
            if (_disposed)
                return;

            if (_pending.TryGetValue(bundleId, out var existing))
            {
                existing.Cancel();
                existing.Dispose();
            }

            var cts = new CancellationTokenSource();
            _pending[bundleId] = cts;
            _ = DispatchAfterDebounceAsync(bundleId, cts.Token);
        }
    }

    private async Task DispatchAfterDebounceAsync(string bundleId, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Debounce, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!IsLoadedSceneTierBundle(bundleId))
            return;

        Callable.From(() =>
        {
            _ = ReloadOnMainThreadAsync(bundleId);
        }).CallDeferred();
    }

    private bool IsLoadedSceneTierBundle(string bundleId)
    {
        var resource = _registry.TryGet<FantaSim.App.Resource.IService>();
        if (resource is null || !resource.IsLoaded(bundleId))
        {
            _log.LogDebug("pck change ignored: bundle '{BundleId}' is not loaded.", bundleId);
            return false;
        }

        var bundleType = resource.GetManifest(bundleId)?.Metadata.GetValueOrDefault("bundleType");
        if (!string.Equals(bundleType, SceneTierBundleType, StringComparison.OrdinalIgnoreCase))
        {
            _log.LogDebug(
                "pck change ignored: bundle '{BundleId}' type '{BundleType}' reloads via its own view watcher.",
                bundleId,
                bundleType);
            return false;
        }

        return true;
    }

    private async Task ReloadOnMainThreadAsync(string bundleId)
    {
        var commands = _registry.TryGet<FantaSim.App.Command.IService>();
        if (commands is null)
        {
            _log.LogWarning("scene-tier pck reload skipped for '{BundleId}': command service is not registered.", bundleId);
            return;
        }

        // Serialize reloads so a multi-pck install (task bundle:install) cannot interleave two
        // SceneFlow Exit->Enter cascades; WaitAsync continuations resume on the main thread.
        await _reloadSerializer.WaitAsync();
        try
        {
            if (_disposed)
                return;

            _log.LogInformation("scene-tier pck changed: dispatching resource.reload_bundle for '{BundleId}'.", bundleId);
            var result = await commands.ExecuteAsync(new FantaSim.App.Command.CommandRequest(
                Command: "resource.reload_bundle",
                PayloadJson: JsonSerializer.Serialize(new { bundleId }),
                ActorKind: "system",
                ActorId: "scene-tier-pck-watcher"));

            if (result.Ok)
                _log.LogInformation("scene-tier pck reload completed for '{BundleId}': {Result}", bundleId, result.ResultJson);
            else
                _log.LogError(
                    "scene-tier pck reload failed for '{BundleId}': {ErrorType} {ErrorMessage}",
                    bundleId,
                    result.Error?.Type,
                    result.Error?.Message);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "scene-tier pck reload threw for '{BundleId}'.", bundleId);
        }
        finally
        {
            _reloadSerializer.Release();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _watcher.Dispose();
            foreach (var cts in _pending.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }

            _pending.Clear();
        }
    }
}
