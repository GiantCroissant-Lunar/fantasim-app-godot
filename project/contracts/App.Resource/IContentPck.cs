namespace FantaSim.App.Resource;

public interface IContentPck
{
    string Id { get; }

    string Uri { get; }

    string DisplayName { get; }

    IReadOnlyList<string> EntryScenes { get; }

    IReadOnlyDictionary<string, string> Metadata { get; }
}
