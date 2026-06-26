using System;
using System.Collections.Generic;
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

/// <summary>Domain-neutral node type presentation metadata for graph authoring UIs.</summary>
public sealed record GraphNodeTypeInfo(
    string TypeId,
    string Category,
    string Summary,
    bool IsSideEffect,
    bool IsExpensive,
    FunctionProviderMetadata? ProviderMetadata = null,
    FunctionExecutionTraits? ExecutionTraits = null);

/// <summary>Optional extension for sources that can describe node types in their active graph.</summary>
public interface IGraphNodeMetadataSource
{
    bool TryGetNodeTypeInfo(string typeId, out GraphNodeTypeInfo? info);
}

/// <summary>Optional instance-level node presentation for sources that keep authored labels/details
/// outside the executable <see cref="GraphNode"/> document.</summary>
public sealed record GraphNodePresentation(
    string NodeId,
    string Label,
    string? Summary = null,
    string? Detail = null,
    IReadOnlyList<string>? ParameterLines = null);

public interface IGraphNodePresentationSource
{
    bool TryGetNodePresentation(string nodeId, out GraphNodePresentation? presentation);
}

/// <summary>Optional extension for sources that can expose non-executable graph annotations.</summary>
public interface IGraphAnnotationSource
{
    IReadOnlyList<GraphAnnotation> Annotations { get; }
}

/// <summary>Optional extension for graph families that can navigate from nodes to child graphs.</summary>
public interface IGraphSubgraphSource
{
    string ActiveGraphId { get; }
    string ActiveGraphLabel { get; }
    IReadOnlyList<GraphSubgraph> Subgraphs { get; }
    void SelectGraph(string graphId);
}
