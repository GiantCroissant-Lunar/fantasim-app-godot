using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.NodeGraph;
using FantaSim.App.World;

namespace FantaSim.App.World.GenerationGraph;

/// <summary>
/// Editable world-generation graph source backed by the typed world authoring model and projected
/// into the generic node-graph document expected by the shared UI/executor.
/// </summary>
public sealed class WorldGenerationGraphSource : IGraphSource, IGraphAnnotationSource, IGraphNodeMetadataSource, IGraphNodePresentationSource
{
    private readonly object _gate = new();

    public WorldGenerationGraphSource(string sourceId, WorldGenerationGraphView graph)
    {
        SourceId = string.IsNullOrWhiteSpace(sourceId)
            ? throw new ArgumentException("Source id must be non-empty.", nameof(sourceId))
            : sourceId;
        Graph = graph ?? throw new ArgumentNullException(nameof(graph));
        Document = CompileForAuthoring(Graph);
        Annotations = MapAnnotations(Graph);
    }

    public string SourceId { get; }

    public WorldGenerationGraphView Graph { get; private set; }

    public GraphDocument Document { get; private set; }

    public IReadOnlyList<GraphAnnotation> Annotations { get; private set; }

    public event Action? Changed;

    public Task ApplyEditAsync(GraphEdit edit, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(edit);

        lock (_gate)
        {
            var next = ApplyEdit(Graph, edit);

            Graph = next;
            Document = CompileForAuthoring(next);
            Annotations = MapAnnotations(next);
        }

        Changed?.Invoke();
        return Task.CompletedTask;
    }

    /// <summary>Compile with full execution validation. Use this before running the graph.</summary>
    public CompiledWorldGenerationGraph CompileForExecution(string? sinkNodeId = null)
        => WorldGenerationGraphCompiler.Compile(Graph, sinkNodeId);

    public bool TryGetNodeTypeInfo(string typeId, out GraphNodeTypeInfo? info)
    {
        var schema = WorldGenerationNodeCatalog.Find(typeId);
        if (schema is null)
        {
            info = null;
            return false;
        }

        info = new GraphNodeTypeInfo(
            schema.TypeId,
            schema.Category,
            schema.Summary,
            schema.IsSideEffect,
            schema.IsExpensive,
            schema.ProviderMetadata,
            schema.ExecutionTraits);
        return true;
    }

    public bool TryGetNodePresentation(string nodeId, out GraphNodePresentation? presentation)
    {
        var node = Graph.Nodes.FirstOrDefault(candidate =>
            string.Equals(candidate.NodeId, nodeId, StringComparison.Ordinal));
        if (node is null)
        {
            presentation = null;
            return false;
        }

        presentation = BuildPresentation(node);
        return true;
    }

    internal static WorldGenerationGraphView ApplyEdit(WorldGenerationGraphView graph, GraphEdit edit)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(edit);

        return edit switch
        {
            GraphEdit.AddNode add => AddNode(graph, add.Node),
            GraphEdit.RemoveNode remove => RemoveNode(graph, remove.NodeId),
            GraphEdit.AddWire add => AddWire(graph, add.Wire),
            GraphEdit.RemoveWire remove => RemoveWire(graph, remove),
            GraphEdit.SetParam set => SetParam(graph, set.NodeId, set.Key, set.Value),
            GraphEdit.AddAnnotation add => AddAnnotation(graph, add.Annotation),
            GraphEdit.UpdateAnnotation update => UpdateAnnotation(graph, update.Annotation),
            GraphEdit.RemoveAnnotation remove => RemoveAnnotation(graph, remove.AnnotationId),
            _ => throw new NotSupportedException($"Unsupported graph edit '{edit.GetType().Name}'."),
        };
    }

    private static GraphDocument CompileForAuthoring(WorldGenerationGraphView graph)
        => WorldGenerationGraphCompiler.Compile(graph, validateRequiredInputs: false).Document;

    private static IReadOnlyList<GraphAnnotation> MapAnnotations(WorldGenerationGraphView graph)
    {
        if (graph.Annotations is null)
            return Array.Empty<GraphAnnotation>();

        return graph.Annotations
            .Select(annotation => new GraphAnnotation(
                annotation.AnnotationId,
                annotation.Kind,
                annotation.Label,
                new GraphAnnotationBounds(
                    annotation.Bounds.X,
                    annotation.Bounds.Y,
                    annotation.Bounds.Width,
                    annotation.Bounds.Height),
                annotation.NodeIds,
                annotation.Text,
                annotation.Color))
            .ToList();
    }

    private static WorldGenerationGraphView AddNode(WorldGenerationGraphView graph, GraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (graph.Nodes.Any(existing => string.Equals(existing.NodeId, node.Id, StringComparison.Ordinal)))
            throw new ArgumentException($"Graph '{graph.GraphId}' already contains node '{node.Id}'.");

        var overrides = node.Params.ToDictionary(
            kv => kv.Key,
            kv => FormatParameterValue(kv.Value),
            StringComparer.Ordinal);
        var typedNode = WorldGenerationGraphDefaults.NodeFromSchema(node.Id, node.FunctionId, overrides);
        var nodes = graph.Nodes.Append(typedNode).ToList();
        return ValidateAuthoringGraph(graph with { Nodes = nodes });
    }

    private static WorldGenerationGraphView RemoveNode(WorldGenerationGraphView graph, string nodeId)
    {
        RequireNonEmpty(nodeId, nameof(nodeId));
        if (graph.OutputNodeIds?.Any(id => string.Equals(id, nodeId, StringComparison.Ordinal)) == true)
            throw new ArgumentException($"Cannot remove output node '{nodeId}'.");

        var nodes = graph.Nodes
            .Where(node => !string.Equals(node.NodeId, nodeId, StringComparison.Ordinal))
            .ToList();
        if (nodes.Count == graph.Nodes.Count)
            throw new ArgumentException($"Graph '{graph.GraphId}' does not contain node '{nodeId}'.");

        var wires = graph.Wires
            .Where(wire => !string.Equals(wire.FromNodeId, nodeId, StringComparison.Ordinal)
                           && !string.Equals(wire.ToNodeId, nodeId, StringComparison.Ordinal))
            .ToList();
        var annotations = graph.Annotations?
            .Select(annotation => annotation with
            {
                NodeIds = annotation.NodeIds
                    .Where(id => !string.Equals(id, nodeId, StringComparison.Ordinal))
                    .ToList(),
            })
            .ToList();

        return ValidateAuthoringGraph(graph with { Nodes = nodes, Wires = wires, Annotations = annotations });
    }

    private static WorldGenerationGraphView AddWire(WorldGenerationGraphView graph, GraphWire wire)
    {
        ArgumentNullException.ThrowIfNull(wire);

        var fromNode = graph.Nodes.FirstOrDefault(node => string.Equals(node.NodeId, wire.FromNode, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Graph '{graph.GraphId}' does not contain node '{wire.FromNode}'.");
        var toNode = graph.Nodes.FirstOrDefault(node => string.Equals(node.NodeId, wire.ToNode, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Graph '{graph.GraphId}' does not contain node '{wire.ToNode}'.");

        var fromPort = fromNode.Outputs.FirstOrDefault(port => string.Equals(port.PortId, wire.FromPort, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Node '{wire.FromNode}' has no output port '{wire.FromPort}'.");
        var toPort = toNode.Inputs.FirstOrDefault(port => string.Equals(port.PortId, wire.ToPort, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Node '{wire.ToNode}' has no input port '{wire.ToPort}'.");

        var kindHint = string.IsNullOrWhiteSpace(fromPort.KindHint) ? toPort.KindHint : fromPort.KindHint;
        var typedWire = new WorldGenerationGraphWire(wire.FromNode, wire.FromPort, wire.ToNode, wire.ToPort, kindHint);
        if (graph.Wires.Any(existing => SameEndpoint(existing, typedWire)))
            throw new ArgumentException($"Graph already contains wire '{wire.FromNode}.{wire.FromPort} -> {wire.ToNode}.{wire.ToPort}'.");

        var wires = graph.Wires.Append(typedWire).ToList();
        return ValidateAuthoringGraph(graph with { Wires = wires });
    }

    private static WorldGenerationGraphView RemoveWire(WorldGenerationGraphView graph, GraphEdit.RemoveWire edit)
    {
        var wires = graph.Wires
            .Where(wire =>
                !string.Equals(wire.FromNodeId, edit.FromNode, StringComparison.Ordinal)
                || !string.Equals(wire.FromPortId, edit.FromPort, StringComparison.Ordinal)
                || !string.Equals(wire.ToNodeId, edit.ToNode, StringComparison.Ordinal)
                || !string.Equals(wire.ToPortId, edit.ToPort, StringComparison.Ordinal))
            .ToList();

        if (wires.Count == graph.Wires.Count)
            throw new ArgumentException(
                $"Graph does not contain wire '{edit.FromNode}.{edit.FromPort} -> {edit.ToNode}.{edit.ToPort}'.");

        return ValidateAuthoringGraph(graph with { Wires = wires });
    }

    private static WorldGenerationGraphView SetParam(
        WorldGenerationGraphView graph,
        string nodeId,
        string key,
        JsonNode? value)
    {
        RequireNonEmpty(nodeId, nameof(nodeId));
        RequireNonEmpty(key, nameof(key));

        var index = graph.Nodes.ToList().FindIndex(node => string.Equals(node.NodeId, nodeId, StringComparison.Ordinal));
        if (index < 0)
            throw new ArgumentException($"Graph '{graph.GraphId}' does not contain node '{nodeId}'.");

        var node = graph.Nodes[index];
        if (node.Parameters is null)
            throw new ArgumentException($"Node '{nodeId}' has no parameters.");

        var parameters = node.Parameters.ToList();
        var paramIndex = parameters.FindIndex(parameter => string.Equals(parameter.Key, key, StringComparison.Ordinal));
        if (paramIndex < 0)
            throw new ArgumentException($"Node '{nodeId}' has no parameter '{key}'.");

        parameters[paramIndex] = parameters[paramIndex] with { Value = FormatParameterValue(value) };
        var nodes = graph.Nodes.ToList();
        nodes[index] = node with { Parameters = parameters };
        return ValidateAuthoringGraph(graph with { Nodes = nodes });
    }

    private static WorldGenerationGraphView AddAnnotation(
        WorldGenerationGraphView graph,
        GraphAnnotation annotation)
    {
        var typedAnnotation = MapAnnotation(graph, annotation);
        var annotations = graph.Annotations?.ToList() ?? new List<WorldGenerationGraphAnnotation>();
        if (annotations.Any(existing => string.Equals(existing.AnnotationId, typedAnnotation.AnnotationId, StringComparison.Ordinal)))
            throw new ArgumentException($"Graph '{graph.GraphId}' already contains annotation '{typedAnnotation.AnnotationId}'.");

        annotations.Add(typedAnnotation);
        return ValidateAuthoringGraph(graph with { Annotations = annotations });
    }

    private static WorldGenerationGraphView UpdateAnnotation(
        WorldGenerationGraphView graph,
        GraphAnnotation annotation)
    {
        var typedAnnotation = MapAnnotation(graph, annotation);
        var annotations = graph.Annotations?.ToList() ?? new List<WorldGenerationGraphAnnotation>();
        var index = annotations.FindIndex(existing =>
            string.Equals(existing.AnnotationId, typedAnnotation.AnnotationId, StringComparison.Ordinal));
        if (index < 0)
            throw new ArgumentException($"Graph '{graph.GraphId}' does not contain annotation '{typedAnnotation.AnnotationId}'.");

        annotations[index] = typedAnnotation;
        return ValidateAuthoringGraph(graph with { Annotations = annotations });
    }

    private static WorldGenerationGraphView RemoveAnnotation(
        WorldGenerationGraphView graph,
        string annotationId)
    {
        RequireNonEmpty(annotationId, nameof(annotationId));
        var annotations = graph.Annotations?.ToList() ?? new List<WorldGenerationGraphAnnotation>();
        var next = annotations
            .Where(annotation => !string.Equals(annotation.AnnotationId, annotationId, StringComparison.Ordinal))
            .ToList();
        if (next.Count == annotations.Count)
            throw new ArgumentException($"Graph '{graph.GraphId}' does not contain annotation '{annotationId}'.");

        return ValidateAuthoringGraph(graph with { Annotations = next });
    }

    private static WorldGenerationGraphAnnotation MapAnnotation(
        WorldGenerationGraphView graph,
        GraphAnnotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        RequireNonEmpty(annotation.AnnotationId, nameof(annotation.AnnotationId));
        RequireNonEmpty(annotation.Kind, nameof(annotation.Kind));
        RequireNonEmpty(annotation.Label, nameof(annotation.Label));
        ValidateAnnotationKind(annotation.Kind);
        ArgumentNullException.ThrowIfNull(annotation.Bounds);
        ValidateBounds(annotation.Bounds);

        var graphNodeIds = graph.Nodes.Select(node => node.NodeId).ToHashSet(StringComparer.Ordinal);
        var annotationNodeIds = annotation.NodeIds ?? Array.Empty<string>();
        var nodeIds = annotationNodeIds
            .Where(nodeId => !string.IsNullOrWhiteSpace(nodeId))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        foreach (var nodeId in nodeIds)
        {
            if (!graphNodeIds.Contains(nodeId))
                throw new ArgumentException($"Annotation '{annotation.AnnotationId}' references unknown node '{nodeId}'.");
        }

        return new WorldGenerationGraphAnnotation(
            annotation.AnnotationId,
            annotation.Kind,
            annotation.Label,
            new WorldGenerationGraphBounds(
                annotation.Bounds.X,
                annotation.Bounds.Y,
                annotation.Bounds.Width,
                annotation.Bounds.Height),
            nodeIds,
            annotation.Text,
            annotation.Color);
    }

    private static void ValidateBounds(GraphAnnotationBounds bounds)
    {
        if (!float.IsFinite(bounds.X)
            || !float.IsFinite(bounds.Y)
            || !float.IsFinite(bounds.Width)
            || !float.IsFinite(bounds.Height))
        {
            throw new ArgumentException("Annotation bounds must be finite.");
        }

        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new ArgumentException("Annotation bounds width and height must be positive.");
    }

    private static void ValidateAnnotationKind(string kind)
    {
        if (string.Equals(kind, WorldGenerationGraphAnnotationKinds.CommentBoundary, StringComparison.Ordinal)
            || string.Equals(kind, WorldGenerationGraphAnnotationKinds.GroupBoundary, StringComparison.Ordinal))
        {
            return;
        }

        throw new ArgumentException($"Unsupported world-generation annotation kind '{kind}'.");
    }

    private static WorldGenerationGraphView ValidateAuthoringGraph(WorldGenerationGraphView graph)
    {
        _ = WorldGenerationGraphCompiler.Compile(graph, validateRequiredInputs: false);
        return graph;
    }

    private static string FormatParameterValue(JsonNode? value)
    {
        if (value is null)
            return string.Empty;
        if (value is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<int>(out var intValue))
                return intValue.ToString(CultureInfo.InvariantCulture);
            if (jsonValue.TryGetValue<long>(out var longValue))
                return longValue.ToString(CultureInfo.InvariantCulture);
            if (jsonValue.TryGetValue<double>(out var doubleValue))
                return doubleValue.ToString(CultureInfo.InvariantCulture);
            if (jsonValue.TryGetValue<bool>(out var boolValue))
                return boolValue.ToString();
            if (jsonValue.TryGetValue<string>(out var stringValue))
                return stringValue;
        }

        return value.ToJsonString();
    }

    private static GraphNodePresentation BuildPresentation(WorldGenerationGraphNode node)
        => new(
            node.NodeId,
            string.IsNullOrWhiteSpace(node.Label) ? node.TypeId : node.Label,
            string.IsNullOrWhiteSpace(node.Summary) ? null : node.Summary,
            ParameterLines: BuildPresentationParameterLines(node));

    private static IReadOnlyList<string> BuildPresentationParameterLines(WorldGenerationGraphNode node)
    {
        if (node.Parameters is null || node.Parameters.Count == 0)
            return Array.Empty<string>();

        if (string.Equals(node.TypeId, WorldFunctionProvider.LayerSource, StringComparison.Ordinal))
            return SelectParameterLines(node.Parameters, "sourceId", "sourceKind", "availability", "providerId", "importFormat", "rendererContract");

        if (string.Equals(node.TypeId, WorldFunctionProvider.LayerNormalize, StringComparison.Ordinal))
            return SelectParameterLines(node.Parameters, "selectedSourceId", "selectedSourceKind", "normalizedProductKind", "rendererContract");

        if (string.Equals(node.TypeId, WorldFunctionProvider.LayerScope, StringComparison.Ordinal))
            return SelectParameterLines(node.Parameters, "layerId", "regimeId", "role");

        return node.Parameters
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Value))
            .Take(5)
            .Select(parameter => $"{parameter.Label}: {parameter.Value}")
            .ToArray();
    }

    private static IReadOnlyList<string> SelectParameterLines(
        IReadOnlyList<WorldGenerationGraphParameter> parameters,
        params string[] keys)
    {
        var lines = new List<string>(keys.Length);
        foreach (var key in keys)
        {
            var parameter = parameters.FirstOrDefault(candidate =>
                string.Equals(candidate.Key, key, StringComparison.Ordinal));
            if (parameter is null || string.IsNullOrWhiteSpace(parameter.Value))
                continue;

            lines.Add($"{parameter.Label}: {parameter.Value}");
        }

        return lines;
    }

    private static bool SameEndpoint(WorldGenerationGraphWire left, WorldGenerationGraphWire right)
        => string.Equals(left.FromNodeId, right.FromNodeId, StringComparison.Ordinal)
           && string.Equals(left.FromPortId, right.FromPortId, StringComparison.Ordinal)
           && string.Equals(left.ToNodeId, right.ToNodeId, StringComparison.Ordinal)
           && string.Equals(left.ToPortId, right.ToPortId, StringComparison.Ordinal);

    private static void RequireNonEmpty(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value must be non-empty.", parameterName);
    }
}
