namespace FantaSim.App.Resource;

public interface IResourceManifest
{
    string BundleId { get; }

    string DisplayName { get; }

    string Version { get; }

    string EntryScene { get; }

    string? PluginAssembly { get; }

    IReadOnlyList<string> Scenes { get; }

    IReadOnlyDictionary<string, string> Metadata { get; }

    IResourceContent? Content { get; }

    IResourceManaged? Managed { get; }
}
