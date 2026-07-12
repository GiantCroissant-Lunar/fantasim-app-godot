using FantaSim.App.Resource.Providers;
using Microsoft.Extensions.Logging;
using System.Runtime.ExceptionServices;

namespace FantaSim.App.Resource.Services;

public sealed class Service : IService
{
    private readonly RegistryArchi.Contracts.IRegistry _providerRegistry;
    private readonly IDirectoryResolver _directoryResolver;
    private readonly Func<string?> _autoLoadExcludeProvider;
    private readonly ILogger _logger;
    private readonly object _runtimeChangeGate = new();
    private readonly Dictionary<string, int> _runtimeChanges = new(StringComparer.OrdinalIgnoreCase);
    private IProvider? _resolvedProvider;

    public Service(
        RegistryArchi.Contracts.IRegistry providerRegistry,
        IDirectoryResolver directoryResolver,
        ILoggerFactory loggerFactory,
        Func<string?>? autoLoadExcludeProvider = null)
    {
        _providerRegistry = providerRegistry ?? throw new ArgumentNullException(nameof(providerRegistry));
        _directoryResolver = directoryResolver ?? throw new ArgumentNullException(nameof(directoryResolver));
        _logger = (loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory))).CreateLogger<Service>();
        _autoLoadExcludeProvider = autoLoadExcludeProvider ?? (() => string.Empty);
    }

    private IProvider Provider =>
        _resolvedProvider ??= _providerRegistry.Get<IProvider>(RegistryArchi.Contracts.SelectionMode.HighestPriority);

    public event EventHandler<ResourceRuntimeChangingEventArgs>? RuntimeChanging;

    public event EventHandler? RuntimeChanged;

    public async Task AutoLoadAsync(CancellationToken cancellationToken = default)
    {
        var resourcesDir = _directoryResolver.ResolveResourcesDirectory();
        if (!Directory.Exists(resourcesDir))
        {
            _logger.LogInformation("No resource directory found at {Path}", resourcesDir);
            return;
        }

        var excluded = ParseIdList(_autoLoadExcludeProvider());
        var pckFiles = Directory.GetFiles(resourcesDir, "*.pck")
            .Where(path => !excluded.Contains(Path.GetFileNameWithoutExtension(path)))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var pckFile in pckFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await LoadAsync(pckFile, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to auto-load resource {Path}", pckFile);
            }
        }
    }

    public IReadOnlyList<string> ListLoaded() => Provider.ListLoaded();

    public IReadOnlyList<string> ListAvailable()
    {
        var resourcesDir = _directoryResolver.ResolveResourcesDirectory();
        if (!Directory.Exists(resourcesDir))
            return Array.Empty<string>();

        return Directory.GetFiles(resourcesDir, "*.pck")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray()!;
    }

    public IReadOnlyList<ResourceEntry> ListEntries() => Provider.ListEntries();

    public bool IsLoaded(string id) => Provider.IsLoaded(id);

    public bool IsRuntimeChangeInProgress(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;

        lock (_runtimeChangeGate)
            return _runtimeChanges.TryGetValue(id, out var count) && count > 0;
    }

    public IResourceManifest? GetManifest(string id) => Provider.GetManifest(id);

    public async Task LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        await Provider.LoadAsync(path, cancellationToken);
        OnRuntimeChanged();
    }

    public async Task LoadFromDirectoryAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var pckPath = Path.Combine(_directoryResolver.ResolveResourcesDirectory(), $"{id}.pck");
        if (!File.Exists(pckPath))
        {
            _logger.LogWarning("Resource PCK not found for {Id}: {Path}", id, pckPath);
            return;
        }

        await LoadAsync(pckPath, cancellationToken);
    }

    public async Task LoadRemoteAsync(string url, CancellationToken cancellationToken = default)
    {
        await Provider.LoadRemoteAsync(url, cancellationToken);
        OnRuntimeChanged();
    }

    public async Task UnloadAsync(string id, CancellationToken cancellationToken = default)
        => await ExecuteRuntimeChangeAsync(
            id,
            ResourceRuntimeOperation.Unload,
            Provider.UnloadAsync,
            cancellationToken);

    public async Task ReloadAsync(string id, CancellationToken cancellationToken = default)
        => await ExecuteRuntimeChangeAsync(
            id,
            ResourceRuntimeOperation.Reload,
            Provider.ReloadAsync,
            cancellationToken);

    public async Task ReloadByPathAsync(string path, CancellationToken cancellationToken = default)
    {
        if (ResolveLoadedBundleId(path) is { } bundleId)
        {
            await ExecuteRuntimeChangeAsync(
                bundleId,
                ResourceRuntimeOperation.Reload,
                (_, ct) => Provider.ReloadByPathAsync(path, ct),
                cancellationToken);
            return;
        }

        await Provider.ReloadByPathAsync(path, cancellationToken);
        OnRuntimeChanged();
    }

    public IDisposable WatchResource(string id, TimeSpan? debounce = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var resourcesDir = _directoryResolver.ResolveResourcesDirectory();
        if (!Directory.Exists(resourcesDir))
            return NoopDisposable.Instance;

        return new ResourcePckWatcher(this, id, resourcesDir, debounce ?? TimeSpan.FromMilliseconds(500));
    }

    public async Task UnloadAllAsync(CancellationToken cancellationToken = default)
    {
        var bundleIds = Provider.ListLoaded().ToArray();
        foreach (var bundleId in bundleIds)
            BeginRuntimeChange(bundleId);

        Exception? failure = null;
        try
        {
            foreach (var bundleId in bundleIds)
                OnRuntimeChanging(bundleId, ResourceRuntimeOperation.UnloadAll);
            await Provider.UnloadAllAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            foreach (var bundleId in bundleIds)
                CompleteRuntimeChange(bundleId);
            failure = PublishRuntimeChangedPreserving(failure);
        }

        Rethrow(failure);
    }

    private async Task ExecuteRuntimeChangeAsync(
        string bundleId,
        ResourceRuntimeOperation operation,
        Func<string, CancellationToken, Task> change,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleId);
        BeginRuntimeChange(bundleId);

        Exception? failure = null;
        try
        {
            OnRuntimeChanging(bundleId, operation);
            await change(bundleId, cancellationToken);
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            CompleteRuntimeChange(bundleId);
            failure = PublishRuntimeChangedPreserving(failure);
        }

        Rethrow(failure);
    }

    private void BeginRuntimeChange(string bundleId)
    {
        lock (_runtimeChangeGate)
            _runtimeChanges[bundleId] = _runtimeChanges.GetValueOrDefault(bundleId) + 1;
    }

    private void CompleteRuntimeChange(string bundleId)
    {
        lock (_runtimeChangeGate)
        {
            if (!_runtimeChanges.TryGetValue(bundleId, out var count) || count <= 1)
                _runtimeChanges.Remove(bundleId);
            else
                _runtimeChanges[bundleId] = count - 1;
        }
    }

    private Exception? PublishRuntimeChangedPreserving(Exception? priorFailure)
    {
        try
        {
            OnRuntimeChanged();
            return priorFailure;
        }
        catch (Exception completionFailure)
        {
            if (priorFailure is null)
                return completionFailure;

            _logger.LogError(
                completionFailure,
                "A RuntimeChanged subscriber failed while propagating an earlier resource lifecycle error.");
            return priorFailure;
        }
    }

    private static void Rethrow(Exception? failure)
    {
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private void OnRuntimeChanging(string bundleId, ResourceRuntimeOperation operation)
        => RuntimeChanging?.Invoke(this, new ResourceRuntimeChangingEventArgs(bundleId, operation));

    private void OnRuntimeChanged()
    {
        Exception? firstFailure = null;
        foreach (var subscriber in RuntimeChanged?.GetInvocationList() ?? Array.Empty<Delegate>())
        {
            try
            {
                ((EventHandler)subscriber)(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                firstFailure ??= ex;
                _logger.LogError(ex, "Resource RuntimeChanged subscriber failed; continuing completion fan-out.");
            }
        }

        Rethrow(firstFailure);
    }

    private string? ResolveLoadedBundleId(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var fileName = Path.GetFileName(path);
        foreach (var entry in Provider.ListEntries())
        {
            if (string.Equals(entry.PckPath, path, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileName(entry.PckPath), fileName, StringComparison.OrdinalIgnoreCase))
            {
                return entry.BundleId;
            }
        }

        var id = Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrWhiteSpace(id) || !Provider.IsLoaded(id) ? null : id;
    }

    private static HashSet<string> ParseIdList(string? value)
        => (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();

        public void Dispose()
        {
        }
    }

    private sealed class ResourcePckWatcher : IDisposable
    {
        private readonly Service _service;
        private readonly string _id;
        private readonly string _pckFileName;
        private readonly TimeSpan _debounce;
        private readonly FileSystemWatcher _watcher;
        private readonly object _gate = new();
        private CancellationTokenSource? _debounceCts;
        private bool _disposed;
        private int _reloadInProgress;

        public ResourcePckWatcher(Service service, string id, string resourcesDir, TimeSpan debounce)
        {
            _service = service;
            _id = id;
            _pckFileName = $"{id}.pck";
            _debounce = debounce;
            _watcher = new FileSystemWatcher(resourcesDir, "*.pck")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };

            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.Renamed += OnFileRenamed;
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            if (string.Equals(Path.GetFileName(e.FullPath), _pckFileName, StringComparison.OrdinalIgnoreCase))
                ScheduleReload();
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            if (string.Equals(Path.GetFileName(e.FullPath), _pckFileName, StringComparison.OrdinalIgnoreCase))
                ScheduleReload();
        }

        private void ScheduleReload()
        {
            lock (_gate)
            {
                if (_disposed)
                    return;

                _debounceCts?.Cancel();
                _debounceCts?.Dispose();
                _debounceCts = new CancellationTokenSource();
                _ = ReloadAfterDelayAsync(_debounceCts.Token);
            }
        }

        private async Task ReloadAfterDelayAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(_debounce, cancellationToken);
                if (Interlocked.Exchange(ref _reloadInProgress, 1) != 0)
                    return;

                try
                {
                    await _service.ReloadAsync(_id, cancellationToken);
                }
                finally
                {
                    Interlocked.Exchange(ref _reloadInProgress, 0);
                }
            }
            catch (OperationCanceledException)
            {
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
                _debounceCts?.Cancel();
                _debounceCts?.Dispose();
            }
        }
    }
}
