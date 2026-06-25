#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.Ui.Providers;
using FantaSim.App.Ui.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ResourceService = FantaSim.App.Resource.IService;

namespace FantaSim.App.Ui.Tests;

/// <summary>
/// Real coverage for the resident UI <see cref="Service"/> the host depends on to show/hide
/// collectible view bundles. The service is pure C# over an <see cref="IViewHost"/> and the
/// resource service, so it runs headless with fakes: the fake resource models which bundles can
/// be loaded, and the fake view host records mount/unmount calls. Replaces the former no-op
/// smoke placeholder that asserted a tautology and exercised no code.
/// </summary>
public sealed class UiServiceTests
{
    private static Service NewService(FakeViewHost viewHost, FakeResource resource)
        => new(viewHost, resource, bus: null, NullLoggerFactory.Instance);

    [Fact]
    public async Task ShowAsync_BundleAvailable_MountsTracksActiveAndRaisesViewsChanged()
    {
        var viewHost = new FakeViewHost();
        var service = NewService(viewHost, new FakeResource("face"));
        var changed = 0;
        service.ViewsChanged += () => changed++;

        await service.ShowAsync("face");

        Assert.Equal(new[] { "face" }, viewHost.Mounted);
        Assert.Equal(new[] { "face" }, service.ActiveViews);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task ShowAsync_BundleUnavailable_DoesNotMount_AndDropsFromActive()
    {
        var viewHost = new FakeViewHost();
        var service = NewService(viewHost, new FakeResource(/* nothing loadable */));

        await service.ShowAsync("missing");

        Assert.Empty(viewHost.Mounted);
        Assert.Empty(service.ActiveViews);
    }

    [Fact]
    public async Task ShowAsync_CalledTwiceForSameView_MountsOnce()
    {
        var viewHost = new FakeViewHost();
        var service = NewService(viewHost, new FakeResource("face"));
        var changed = 0;
        service.ViewsChanged += () => changed++;

        await service.ShowAsync("face");
        await service.ShowAsync("face");

        Assert.Equal(new[] { "face" }, viewHost.Mounted); // not mounted a second time
        Assert.Equal(new[] { "face" }, service.ActiveViews);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task HideAsync_UnmountsUnloadsAndDropsFromActive()
    {
        var viewHost = new FakeViewHost();
        var resource = new FakeResource("face");
        var service = NewService(viewHost, resource);
        await service.ShowAsync("face");

        await service.HideAsync("face");

        Assert.Equal(new[] { "face" }, viewHost.Unmounted);
        Assert.Equal(new[] { "face" }, resource.Unloaded);
        Assert.Empty(service.ActiveViews);
    }

    [Fact]
    public async Task HideAsync_ViewNotActive_IsNoOp()
    {
        var viewHost = new FakeViewHost();
        var resource = new FakeResource("face");
        var service = NewService(viewHost, resource);

        await service.HideAsync("never-shown");

        Assert.Empty(viewHost.Unmounted);
        Assert.Empty(resource.Unloaded);
    }

    [Fact]
    public async Task ActiveViews_AreOrderedOrdinally()
    {
        var viewHost = new FakeViewHost();
        var service = NewService(viewHost, new FakeResource("beta", "alpha", "gamma"));

        await service.ShowAsync("beta");
        await service.ShowAsync("alpha");
        await service.ShowAsync("gamma");

        Assert.Equal(new[] { "alpha", "beta", "gamma" }, service.ActiveViews);
    }

    private sealed class FakeViewHost : IViewHost
    {
        public List<string> Mounted { get; } = new();
        public List<string> Unmounted { get; } = new();
        public void Mount(string viewId) => Mounted.Add(viewId);
        public void Unmount(string viewId) => Unmounted.Add(viewId);

        public Task<bool> UnmountAndWaitAsync(string viewId, CancellationToken cancellationToken = default)
        {
            var wasMounted = Mounted.Contains(viewId, StringComparer.Ordinal);
            Unmount(viewId);
            return Task.FromResult(wasMounted);
        }

        public bool UnmountNow(string viewId)
        {
            var wasMounted = Mounted.Contains(viewId, StringComparer.Ordinal);
            Unmount(viewId);
            return wasMounted;
        }
    }

    // Models the resource service: only ids passed to the constructor become "loaded" when the
    // UI service asks for them; everything else stays unavailable.
    private sealed class FakeResource : ResourceService
    {
        private readonly HashSet<string> _loadable;
        private readonly HashSet<string> _loaded = new(StringComparer.Ordinal);

        public FakeResource(params string[] loadable)
            => _loadable = new HashSet<string>(loadable, StringComparer.Ordinal);

        public List<string> Unloaded { get; } = new();

        public event EventHandler<FantaSim.App.Resource.ResourceRuntimeChangingEventArgs>? RuntimeChanging { add { } remove { } }

        public event EventHandler? RuntimeChanged { add { } remove { } }

        public bool IsLoaded(string id) => _loaded.Contains(id);

        public Task LoadFromDirectoryAsync(string id, CancellationToken cancellationToken = default)
        {
            if (_loadable.Contains(id))
                _loaded.Add(id);
            return Task.CompletedTask;
        }

        public Task UnloadAsync(string id, CancellationToken cancellationToken = default)
        {
            _loaded.Remove(id);
            Unloaded.Add(id);
            return Task.CompletedTask;
        }

        // --- remainder: not exercised by the UI service paths under test ---
        public IReadOnlyList<string> ListLoaded() => System.Linq.Enumerable.ToArray(_loaded);
        public IReadOnlyList<string> ListAvailable() => Array.Empty<string>();
        public IReadOnlyList<FantaSim.App.Resource.ResourceEntry> ListEntries() => Array.Empty<FantaSim.App.Resource.ResourceEntry>();
        public FantaSim.App.Resource.IResourceManifest? GetManifest(string id) => null;
        public Task AutoLoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadRemoteAsync(string url, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReloadAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReloadByPathAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IDisposable WatchResource(string id, TimeSpan? debounce = null) => throw new NotSupportedException();
        public Task UnloadAllAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
