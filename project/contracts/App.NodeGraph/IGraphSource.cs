using System;
using System.Threading;
using System.Threading.Tasks;

namespace FantaSim.App.NodeGraph;

/// <summary>A live, editable graph instance a view binds to. Analogous to
/// App.Timeline.ITimelineSource: the paradigm owns the shape, a domain source owns a concrete
/// instance. Implementations keep the canonical <see cref="Document"/> and raise
/// <see cref="Changed"/> after structural mutations.</summary>
public interface IGraphSource
{
    string SourceId { get; }
    GraphDocument Document { get; }
    event Action? Changed;
    Task ApplyEditAsync(GraphEdit edit, CancellationToken cancellationToken = default);
}
