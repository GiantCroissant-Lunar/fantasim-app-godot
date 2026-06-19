namespace FantaSim.App.Resource.Providers;

public interface IProvider
{
    Task LoadAsync(string path, CancellationToken cancellationToken = default);

    Task LoadRemoteAsync(string url, CancellationToken cancellationToken = default);

    Task UnloadAsync(string id, CancellationToken cancellationToken = default);

    Task ReloadAsync(string id, CancellationToken cancellationToken = default);

    Task ReloadByPathAsync(string path, CancellationToken cancellationToken = default);

    Task UnloadAllAsync(CancellationToken cancellationToken = default);

    IReadOnlyList<string> ListLoaded();

    IReadOnlyList<ResourceEntry> ListEntries();

    bool IsLoaded(string id);

    IResourceManifest? GetManifest(string id);
}
