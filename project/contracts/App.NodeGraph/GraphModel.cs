using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace FantaSim.App.NodeGraph;

/// <summary>One node in an executable graph: a single function call. <see cref="FunctionId"/> resolves
/// to a provider via <see cref="INodeFunctionProvider"/>; <see cref="Params"/> are static payload
/// fields; wired inputs are merged in by the executor at run time.</summary>
public sealed record GraphNode(string Id, string FunctionId, JsonObject Params);

/// <summary>Edge kind. Data wires thread upstream output fields into downstream payloads. Control
/// wires are reserved for a future VisualScript layer and do not carry data.</summary>
public enum WireKind
{
    Data,
    Control,
}

/// <summary>A data-flow (or control-flow, future) edge: the <c>FromPort</c> field of
/// <c>FromNode</c>'s result becomes the <c>ToPort</c> payload key of <c>ToNode</c>.</summary>
public sealed record GraphWire(
    string FromNode,
    string FromPort,
    string ToNode,
    string ToPort,
    WireKind Kind = WireKind.Data);

/// <summary>An executable graph: nodes (function calls) + wires (data flow) + the terminal node
/// whose result is returned by the executor.</summary>
public sealed record GraphDocument(IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphWire> Wires, string SinkNodeId);

/// <summary>Canvas-space bounds for non-executable graph annotations such as comments or groups.</summary>
public sealed record GraphAnnotationBounds(float X, float Y, float Width, float Height);

/// <summary>Domain-neutral annotation projected beside nodes and wires for graph authoring UIs.</summary>
public sealed record GraphAnnotation(
    string AnnotationId,
    string Kind,
    string Label,
    GraphAnnotationBounds Bounds,
    IReadOnlyList<string> NodeIds,
    string? Text = null,
    string? Color = null);

/// <summary>Domain-neutral pointer from a parent node to another authored graph in the same family.</summary>
public sealed record GraphSubgraph(
    string ParentGraphId,
    string ParentNodeId,
    string SubgraphId,
    string Label,
    string? Description = null,
    IReadOnlyDictionary<string, string>? InputPortMap = null,
    IReadOnlyDictionary<string, string>? OutputPortMap = null);

/// <summary>Discriminated edit applied to an <see cref="IGraphSource"/>. Kept structural so every
/// domain's graph source accepts the same edit vocabulary.</summary>
public abstract record GraphEdit
{
    public sealed record AddNode(GraphNode Node) : GraphEdit;
    public sealed record RemoveNode(string NodeId) : GraphEdit;
    public sealed record AddWire(GraphWire Wire) : GraphEdit;
    public sealed record RemoveWire(string FromNode, string FromPort, string ToNode, string ToPort) : GraphEdit;
    public sealed record SetParam(string NodeId, string Key, JsonNode? Value) : GraphEdit;
}
