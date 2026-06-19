namespace FantaSim.App.Resource;

public sealed record ResourceEntry(
    string BundleId,
    string DisplayName,
    string Version,
    string PckPath,
    string BundleResPath,
    IReadOnlyList<string> EntryScenes,
    IReadOnlyList<string> ManagedAssemblies,
    string? PluginTempDir,
    string Status);
