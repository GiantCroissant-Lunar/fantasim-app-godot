using FantaSim.App.Ui.Providers;
using Godot;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;
using ResourceService = FantaSim.App.Resource.IService;

namespace FantaSim.App.Ui.Seam;

public sealed class ViewHost : IViewHost
{
    private sealed class ActiveView
    {
        public ActiveView(Control mount, ViewRenderer renderer, EventHandler runtimeChanged, IDisposable? watch)
        {
            Mount = mount;
            Renderer = renderer;
            RuntimeChanged = runtimeChanged;
            Watch = watch;
        }

        public Control Mount { get; }
        public ViewRenderer Renderer { get; }
        public EventHandler RuntimeChanged { get; }
        public IDisposable? Watch { get; }
    }

    private readonly Control _uiRoot;
    private readonly IRegistry _registry;
    private readonly ResourceService _resource;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly Dictionary<string, ActiveView> _active = new(StringComparer.Ordinal);

    public ViewHost(Control uiRoot, IRegistry registry, ResourceService resource, ILoggerFactory loggerFactory)
    {
        _uiRoot = uiRoot ?? throw new ArgumentNullException(nameof(uiRoot));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _resource = resource ?? throw new ArgumentNullException(nameof(resource));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<ViewHost>();
    }

    public void Mount(string viewId) => Callable.From(() => MountImpl(viewId)).CallDeferred();

    public void Unmount(string viewId) => Callable.From(() => UnmountImpl(viewId)).CallDeferred();

    public bool UnmountNow(string viewId) => UnmountImpl(viewId);

    public Task<bool> UnmountAndWaitAsync(string viewId, CancellationToken cancellationToken = default)
    {
        // Scene-tree mutation must run on the Godot main thread, so the unmount is deferred. We
        // complete the TCS only after UnmountImpl has run, so an awaiting caller knows the
        // ViewRenderer (and its bundle-typed _source) is actually gone before it proceeds to unload
        // the ALC -- a plain fire-and-forget Unmount() races that unload and leaves the pin in place.
        // RunContinuationsAsynchronously keeps the awaiter's continuation off this main-thread callback.
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (cancellationToken.IsCancellationRequested)
        {
            tcs.TrySetCanceled(cancellationToken);
            return tcs.Task;
        }

        Callable.From(() =>
        {
            try
            {
                tcs.TrySetResult(UnmountImpl(viewId));
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }).CallDeferred();

        return tcs.Task;
    }

    private void MountImpl(string viewId)
    {
        if (_active.ContainsKey(viewId))
            return;

        var mount = new PanelContainer { Name = $"View_{viewId}" };
        mount.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        mount.OffsetLeft = 12;
        mount.OffsetTop = 44;
        mount.OffsetRight = -12;
        mount.OffsetBottom = -12;
        mount.AddThemeStyleboxOverride("panel", ViewBackground());
        _uiRoot.AddChild(mount);

        var renderer = new ViewRenderer(
            mount,
            () => _registry.GetAll<IViewSource>().FirstOrDefault(source => source.ViewId == viewId),
            ResolveShellScenePath,
            _loggerFactory.CreateLogger($"ViewRenderer[{viewId}]"));
        renderer.Bind();

        EventHandler onChanged = (_, _) => Callable.From(renderer.Rebind).CallDeferred();
        _resource.RuntimeChanged += onChanged;
        var watch = _resource.WatchResource(viewId);

        _active[viewId] = new ActiveView(mount, renderer, onChanged, watch);
        _logger.LogInformation("View mounted: {ViewId}", viewId);
    }

    private bool UnmountImpl(string viewId)
    {
        if (!_active.TryGetValue(viewId, out var view))
            return false;

        _active.Remove(viewId);
        _resource.RuntimeChanged -= view.RuntimeChanged;
        view.Watch?.Dispose();
        view.Renderer.Dispose();
        view.Mount.QueueFree();
        _logger.LogInformation("View unmounted: {ViewId}", viewId);
        return true;
    }

    private static StyleBoxFlat ViewBackground()
    {
        var box = new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.05f, 0.06f, 0.96f),
            BorderColor = new Color(0.20f, 0.24f, 0.28f, 1.0f),
        };
        box.SetBorderWidthAll(1);
        box.SetCornerRadiusAll(4);
        box.ContentMarginTop = 10;
        box.ContentMarginRight = 10;
        box.ContentMarginBottom = 10;
        box.ContentMarginLeft = 10;
        return box;
    }

    private string? ResolveShellScenePath(string viewId)
    {
        var manifest = _resource.GetManifest(viewId);
        if (manifest is null)
            return null;

        if (!manifest.Metadata.TryGetValue("uiScene", out var scenePath)
            || string.IsNullOrWhiteSpace(scenePath))
            return null;

        var bundleId = string.IsNullOrWhiteSpace(manifest.BundleId) ? viewId : manifest.BundleId;
        var entry = _resource.ListEntries()
            .FirstOrDefault(candidate => string.Equals(candidate.BundleId, bundleId, StringComparison.OrdinalIgnoreCase));
        var bundleResPath = entry?.BundleResPath;
        if (string.IsNullOrWhiteSpace(bundleResPath))
            bundleResPath = $"res://bundles/{bundleId}";

        return JoinResourcePath(bundleResPath, scenePath);
    }

    private static string JoinResourcePath(string root, string path)
        => $"{root.TrimEnd('/')}/{path.TrimStart('/')}";
}
