using System;
using System.Threading;
using System.Threading.Tasks;

namespace FantaSim.App.NodeGraph;

/// <summary>
/// A read-only <see cref="IGraphSource"/> over a fixed <see cref="GraphDocument"/>. Edits are
/// rejected. Useful for displaying static recipes (e.g. an iii pipeline) in the node-graph view
/// without a backing editable store.
/// </summary>
public sealed class ReadOnlyGraphSource : IGraphSource
{
    public ReadOnlyGraphSource(string sourceId, GraphDocument document)
    {
        SourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
        Document = document ?? throw new ArgumentNullException(nameof(document));
    }

    public string SourceId { get; }
    public GraphDocument Document { get; }

    // A static document never raises Changed. Empty accessors (no backing field).
    public event Action? Changed { add { } remove { } }

    public Task ApplyEditAsync(GraphEdit edit, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException($"{SourceId} is a read-only graph source; edits are not supported.");
    }
}
