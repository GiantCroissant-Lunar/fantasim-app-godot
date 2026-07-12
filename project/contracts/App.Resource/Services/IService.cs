using ServiceArchi.Contracts;
using ServiceArchi.Contracts.Attributes;

namespace FantaSim.App.Resource;

[ServiceContract]
public interface IService
{
    event EventHandler<ResourceRuntimeChangingEventArgs>? RuntimeChanging;

    event EventHandler? RuntimeChanged;

    IReadOnlyList<string> ListLoaded();

    IReadOnlyList<string> ListAvailable();

    IReadOnlyList<ResourceEntry> ListEntries();

    bool IsLoaded(string id);

    /// <summary>
    /// Returns whether one or more lifecycle operations for <paramref name="id"/> have begun but
    /// have not yet published their completion. The state becomes true before RuntimeChanging is
    /// invoked and false before RuntimeChanged is invoked, so late subscribers can close multicast
    /// event-snapshot races with a subscribe-then-read handshake. Concurrent operations for the
    /// same bundle are counted, so interim RuntimeChanged notifications may still observe true;
    /// the state is false before the final completion notification that clears the count.
    /// </summary>
    bool IsRuntimeChangeInProgress(string id);

    IResourceManifest? GetManifest(string id);

    Task AutoLoadAsync(CancellationToken cancellationToken = default);

    Task LoadAsync(string path, CancellationToken cancellationToken = default);

    Task LoadFromDirectoryAsync(string id, CancellationToken cancellationToken = default);

    Task LoadRemoteAsync(string url, CancellationToken cancellationToken = default);

    Task UnloadAsync(string id, CancellationToken cancellationToken = default);

    Task ReloadAsync(string id, CancellationToken cancellationToken = default);

    Task ReloadByPathAsync(string path, CancellationToken cancellationToken = default);

    IDisposable WatchResource(string id, TimeSpan? debounce = null);

    Task UnloadAllAsync(CancellationToken cancellationToken = default);
}
