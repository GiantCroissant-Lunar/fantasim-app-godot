using ServiceArchi.Contracts;
using ServiceArchi.Contracts.Attributes;

namespace FantaSim.App.Resource;

[ServiceContract]
public interface IService
{
    event EventHandler? RuntimeChanged;

    IReadOnlyList<string> ListLoaded();

    IReadOnlyList<string> ListAvailable();

    IReadOnlyList<ResourceEntry> ListEntries();

    bool IsLoaded(string id);

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
