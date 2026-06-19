using CrosscutFoundation.Messaging;
using FantaSim.App.Ui.Providers;
using Microsoft.Extensions.Logging;
using ResourceService = FantaSim.App.Resource.IService;

namespace FantaSim.App.Ui.Services;

public sealed class Service : IService, IDisposable
{
    private readonly IViewHost _viewHost;
    private readonly ResourceService _resource;
    private readonly ILogger _logger;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _active = new(StringComparer.Ordinal);
    private readonly IDisposable? _showSubscription;
    private readonly IDisposable? _hideSubscription;
    private bool _disposed;

    public Service(
        IViewHost viewHost,
        ResourceService resource,
        IMessageBus? bus,
        ILoggerFactory loggerFactory)
    {
        _viewHost = viewHost ?? throw new ArgumentNullException(nameof(viewHost));
        _resource = resource ?? throw new ArgumentNullException(nameof(resource));
        _logger = (loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory))).CreateLogger<Service>();
        _showSubscription = bus?.Subscribe<ShowViewMessage>(message => _ = ShowAsync(message.ViewId));
        _hideSubscription = bus?.Subscribe<HideViewMessage>(message => _ = HideAsync(message.ViewId));
    }

    public IReadOnlyList<string> ActiveViews => _active.Keys.OrderBy(view => view, StringComparer.Ordinal).ToArray();

    public event Action? ViewsChanged;

    public async Task ShowAsync(string viewId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(viewId) || !_active.TryAdd(viewId, 0))
            return;

        try
        {
            if (!_resource.IsLoaded(viewId))
                await _resource.LoadFromDirectoryAsync(viewId, cancellationToken).ConfigureAwait(false);

            if (!_resource.IsLoaded(viewId))
            {
                _active.TryRemove(viewId, out _);
                _logger.LogWarning("View bundle not available: {ViewId}", viewId);
                return;
            }

            _viewHost.Mount(viewId);
            _logger.LogInformation("View shown: {ViewId}", viewId);
            ViewsChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _active.TryRemove(viewId, out _);
            _logger.LogError(ex, "Show view failed: {ViewId}", viewId);
            throw;
        }
    }

    public async Task HideAsync(string viewId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(viewId) || !_active.TryRemove(viewId, out _))
            return;

        try
        {
            _viewHost.Unmount(viewId);
            ViewsChanged?.Invoke();
            await _resource.UnloadAsync(viewId, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("View hidden: {ViewId}", viewId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hide view failed: {ViewId}", viewId);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _showSubscription?.Dispose();
        _hideSubscription?.Dispose();
    }
}
