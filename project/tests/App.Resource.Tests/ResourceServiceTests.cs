#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.Resource;
using FantaSim.App.Resource.Providers;
using FantaSim.App.Resource.Services;
using Microsoft.Extensions.Logging.Abstractions;
using RegistryArchi.Contracts;
using RegistryArchi.Core;
using Xunit;

namespace FantaSim.App.Resource.Tests;

/// <summary>
/// Real coverage for the resident resource <see cref="Service"/> the host depends on for PCK
/// discovery and the load lifecycle. The service is pure C# over an <see cref="IDirectoryResolver"/>
/// plus an <see cref="IProvider"/> resolved from the registry, so it runs headless: a temp directory
/// stands in for the Godot resources folder and a fake provider records the delegated load calls.
/// Replaces the former no-op smoke placeholder that asserted a tautology and exercised no code.
/// </summary>
public sealed class ResourceServiceTests : IDisposable
{
    private readonly string _resourcesDir;

    public ResourceServiceTests()
    {
        _resourcesDir = Path.Combine(Path.GetTempPath(), "fantasim-resource-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_resourcesDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_resourcesDir, recursive: true);
        }
        catch
        {
            // best-effort cleanup of the temp resources dir
        }
    }

    private Service NewService(
        IDirectoryResolver resolver,
        IProvider? provider = null,
        Func<string?>? autoLoadExclude = null)
    {
        var registry = new Registry();
        registry.Register<IProvider>(provider ?? new FakeProvider());
        return new Service(registry, resolver, NullLoggerFactory.Instance, autoLoadExclude);
    }

    [Fact]
    public void ListAvailable_ReturnsPckStems_SortedCaseInsensitively_IgnoringNonPck()
    {
        File.WriteAllText(Path.Combine(_resourcesDir, "Zebra.pck"), "");
        File.WriteAllText(Path.Combine(_resourcesDir, "alpha.pck"), "");
        File.WriteAllText(Path.Combine(_resourcesDir, "Beta.pck"), "");
        File.WriteAllText(Path.Combine(_resourcesDir, "notes.txt"), ""); // must be ignored

        var service = NewService(new FakeDirectoryResolver(_resourcesDir));

        Assert.Equal(new[] { "alpha", "Beta", "Zebra" }, service.ListAvailable());
    }

    [Fact]
    public void ListAvailable_MissingDirectory_ReturnsEmpty()
    {
        var missing = Path.Combine(_resourcesDir, "does-not-exist");
        var service = NewService(new FakeDirectoryResolver(missing));

        Assert.Empty(service.ListAvailable());
    }

    [Fact]
    public async Task AutoLoadAsync_LoadsPcksInOrder_ExcludingConfiguredIds()
    {
        File.WriteAllText(Path.Combine(_resourcesDir, "b.pck"), "");
        File.WriteAllText(Path.Combine(_resourcesDir, "a.pck"), "");
        File.WriteAllText(Path.Combine(_resourcesDir, "skip.pck"), "");

        var provider = new FakeProvider();
        var service = NewService(
            new FakeDirectoryResolver(_resourcesDir),
            provider,
            autoLoadExclude: () => "skip");

        await service.AutoLoadAsync();

        // sorted by file name, "skip" excluded by the exclude-list
        Assert.Equal(
            new[] { "a.pck", "b.pck" },
            provider.LoadedPaths.Select(Path.GetFileName).ToArray());
    }

    [Fact]
    public async Task AutoLoadAsync_MissingDirectory_LoadsNothing()
    {
        var provider = new FakeProvider();
        var service = NewService(
            new FakeDirectoryResolver(Path.Combine(_resourcesDir, "does-not-exist")),
            provider);

        await service.AutoLoadAsync();

        Assert.Empty(provider.LoadedPaths);
    }

    [Fact]
    public async Task LoadAsync_DelegatesToProvider_AndRaisesRuntimeChanged()
    {
        var provider = new FakeProvider();
        var service = NewService(new FakeDirectoryResolver(_resourcesDir), provider);
        var runtimeChanged = 0;
        service.RuntimeChanged += (_, _) => runtimeChanged++;

        await service.LoadAsync("/some/world.pck");

        Assert.Equal(new[] { "/some/world.pck" }, provider.LoadedPaths);
        Assert.Equal(1, runtimeChanged);
    }

    [Fact]
    public async Task ReloadStateIsVisibleBeforeChangingAndClearedBeforeCompletion()
    {
        var provider = new GatedReloadProvider();
        var service = NewService(new FakeDirectoryResolver(_resourcesDir), provider);
        var changingObservedActive = false;
        var changedObservedInactive = false;
        service.RuntimeChanging += (_, args) =>
        {
            if (args.BundleId == "world")
                changingObservedActive = service.IsRuntimeChangeInProgress("world");
        };
        service.RuntimeChanged += (_, _) =>
            changedObservedInactive = !service.IsRuntimeChangeInProgress("world");

        var reload = service.ReloadAsync("world");
        await provider.ReloadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(service.IsRuntimeChangeInProgress("world"));
        provider.ReleaseReload.SetResult();
        await reload;

        Assert.True(changingObservedActive);
        Assert.True(changedObservedInactive);
        Assert.False(service.IsRuntimeChangeInProgress("world"));
    }

    [Fact]
    public async Task ConcurrentReloadsKeepPerBundleStateActiveUntilTheLastCompletion()
    {
        var provider = new CountingReloadProvider(expectedCalls: 2);
        var service = NewService(new FakeDirectoryResolver(_resourcesDir), provider);

        var first = service.ReloadAsync("world");
        var second = service.ReloadAsync("world");
        await provider.AllEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(service.IsRuntimeChangeInProgress("world"));

        provider.ReleaseOne();
        await provider.OneCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(service.IsRuntimeChangeInProgress("world"));
        Assert.False(service.IsRuntimeChangeInProgress("timeline"));

        provider.ReleaseOne();
        await Task.WhenAll(first, second);
        Assert.False(service.IsRuntimeChangeInProgress("world"));
    }

    [Fact]
    public async Task ReloadFailureClearsStateAndStillPublishesCompletion()
    {
        var service = NewService(new FakeDirectoryResolver(_resourcesDir), new FakeProvider());
        var completions = 0;
        service.RuntimeChanged += (_, _) => completions++;

        await Assert.ThrowsAsync<NotSupportedException>(() => service.ReloadAsync("world"));

        Assert.False(service.IsRuntimeChangeInProgress("world"));
        Assert.Equal(1, completions);
    }

    [Fact]
    public async Task UnrelatedBundleCompletionCannotClearWorldState()
    {
        var provider = new PerBundleGatedProvider("world", "timeline");
        var service = NewService(new FakeDirectoryResolver(_resourcesDir), provider);
        var world = service.ReloadAsync("world");
        var timeline = service.ReloadAsync("timeline");
        await provider.AllEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        provider.Release("timeline");
        await timeline;

        Assert.True(service.IsRuntimeChangeInProgress("world"));
        Assert.False(service.IsRuntimeChangeInProgress("timeline"));

        provider.Release("world");
        await world;
        Assert.False(service.IsRuntimeChangeInProgress("world"));
    }

    [Fact]
    public void IsLoaded_DelegatesToProvider()
    {
        var provider = new FakeProvider();
        provider.Loaded.Add("world");
        var service = NewService(new FakeDirectoryResolver(_resourcesDir), provider);

        Assert.True(service.IsLoaded("world"));
        Assert.False(service.IsLoaded("missing"));
    }

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        var registry = new Registry();
        var resolver = new FakeDirectoryResolver(_resourcesDir);

        Assert.Throws<ArgumentNullException>(
            () => new Service(null!, resolver, NullLoggerFactory.Instance));
        Assert.Throws<ArgumentNullException>(
            () => new Service(registry, null!, NullLoggerFactory.Instance));
        Assert.Throws<ArgumentNullException>(
            () => new Service(registry, resolver, null!));
    }

    private sealed class FakeDirectoryResolver : IDirectoryResolver
    {
        private readonly string _dir;
        public FakeDirectoryResolver(string dir) => _dir = dir;
        public string ResolveResourcesDirectory() => _dir;
    }

    private class FakeProvider : IProvider
    {
        public List<string> LoadedPaths { get; } = new();
        public HashSet<string> Loaded { get; } = new(StringComparer.Ordinal);

        public Task LoadAsync(string path, CancellationToken cancellationToken = default)
        {
            LoadedPaths.Add(path);
            return Task.CompletedTask;
        }

        public bool IsLoaded(string id) => Loaded.Contains(id);

        public IReadOnlyList<string> ListLoaded() => Loaded.ToArray();

        public IReadOnlyList<ResourceEntry> ListEntries() => Array.Empty<ResourceEntry>();

        public IResourceManifest? GetManifest(string id) => null;

        public Task LoadRemoteAsync(string url, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UnloadAsync(string id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public virtual Task ReloadAsync(string id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task ReloadByPathAsync(string path, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UnloadAllAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class GatedReloadProvider : FakeProvider
    {
        public TaskCompletionSource ReloadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseReload { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task ReloadAsync(string id, CancellationToken cancellationToken = default)
        {
            ReloadEntered.TrySetResult();
            await ReleaseReload.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class CountingReloadProvider : FakeProvider
    {
        private readonly int _expectedCalls;
        private readonly SemaphoreSlim _releases = new(0);
        private int _entered;
        private int _completed;

        public CountingReloadProvider(int expectedCalls) => _expectedCalls = expectedCalls;
        public TaskCompletionSource AllEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource OneCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ReleaseOne() => _releases.Release();

        public override async Task ReloadAsync(string id, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _entered) == _expectedCalls)
                AllEntered.TrySetResult();
            await _releases.WaitAsync(cancellationToken);
            if (Interlocked.Increment(ref _completed) == 1)
                OneCompleted.TrySetResult();
        }
    }

    private sealed class PerBundleGatedProvider : FakeProvider
    {
        private readonly Dictionary<string, TaskCompletionSource> _entered;
        private readonly Dictionary<string, TaskCompletionSource> _release;
        private int _enteredCount;

        public PerBundleGatedProvider(params string[] ids)
        {
            _entered = ids.ToDictionary(
                id => id,
                _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
                StringComparer.OrdinalIgnoreCase);
            _release = ids.ToDictionary(
                id => id,
                _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
                StringComparer.OrdinalIgnoreCase);
            ExpectedCount = ids.Length;
        }

        private int ExpectedCount { get; }
        public TaskCompletionSource AllEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release(string id) => _release[id].TrySetResult();

        public override async Task ReloadAsync(string id, CancellationToken cancellationToken = default)
        {
            _entered[id].TrySetResult();
            if (Interlocked.Increment(ref _enteredCount) == ExpectedCount)
                AllEntered.TrySetResult();
            await _release[id].Task.WaitAsync(cancellationToken);
        }
    }
}
