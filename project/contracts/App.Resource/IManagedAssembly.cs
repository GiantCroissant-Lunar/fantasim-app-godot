namespace FantaSim.App.Resource;

public interface IManagedAssembly
{
    string Id { get; }

    ManagedAssemblyKind Kind { get; }

    string Uri { get; }

    string DisplayName { get; }

    IReadOnlyDictionary<string, string> Metadata { get; }
}
