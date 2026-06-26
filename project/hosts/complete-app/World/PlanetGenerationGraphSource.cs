using System.Globalization;
using System.Text.Json.Nodes;
using FantaSim.App.NodeGraph;
using FantaSim.App.World;
using FantaSim.App.World.Composition;

namespace FantaSim.App.Common.Entry;

internal sealed class PlanetGenerationGraphSource :
    IGraphSource,
    IGraphAnnotationSource,
    IGraphSubgraphSource,
    IGraphNodeMetadataSource,
    IGraphNodePresentationSource
{
    private const string GeosphereSphereId = "geosphere";
    private const string AtmosphereSphereId = "atmosphere";
    private const string LayerScopeFunctionId = "world.layer-scope";
    private const string LayerSourceFunctionId = "world.layer-source";
    private const string LayerNormalizeFunctionId = "world.layer-normalize";
    private readonly object _gate = new();
    private WorldGenerationGraphView _activeGraph;

    public PlanetGenerationGraphSource(
        string sourceId,
        WorldGenerationGraphFamilyDocument family,
        long activeTick)
    {
        SourceId = string.IsNullOrWhiteSpace(sourceId)
            ? throw new ArgumentException("Source id must be non-empty.", nameof(sourceId))
            : sourceId;
        Family = family ?? throw new ArgumentNullException(nameof(family));
        ActiveGraphId = family.BaseGraph.GraphId;
        ActiveTick = activeTick;
        _activeGraph = family.BaseGraph;
        RecomposeLocked();
    }

    public string SourceId { get; }

    public WorldGenerationGraphFamilyDocument Family { get; private set; }

    public string ActiveGraphId { get; private set; }

    public string ActiveGraphLabel => _activeGraph.Label;

    public long ActiveTick { get; private set; }

    public GraphDocument Document { get; private set; } = new(Array.Empty<GraphNode>(), Array.Empty<GraphWire>(), string.Empty);

    public IReadOnlyList<GraphAnnotation> Annotations { get; private set; } = Array.Empty<GraphAnnotation>();

    public IReadOnlyList<GraphSubgraph> Subgraphs { get; private set; } = Array.Empty<GraphSubgraph>();

    public event Action? Changed;

    public static WorldGenerationGraphFamilyDocument BuildFallbackFamily(PlanetPresentationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var nodes = document.Layers.Count == 0
            ? new[]
            {
                new WorldGenerationGraphNode(
                    "planet",
                    "world.presentation.planet",
                    document.PlanetId,
                    "world/presentation",
                    IsSideEffect: false,
                    IsExpensive: false,
                    Inputs: Array.Empty<WorldGenerationGraphPort>(),
                    Outputs: new[] { new WorldGenerationGraphPort("planet", "Planet", "world/planet", Required: false) })
            }
            : document.Layers
                .Select(layer => new WorldGenerationGraphNode(
                    SafeNodeId(layer.LayerId),
                    "world.presentation.layer",
                    layer.LayerId,
                    "world/presentation",
                    IsSideEffect: false,
                    IsExpensive: false,
                    Inputs: Array.Empty<WorldGenerationGraphPort>(),
                    Outputs: new[] { new WorldGenerationGraphPort("layer", "Layer", "world/layer", Required: false) },
                    Parameters: new[]
                    {
                        new WorldGenerationGraphParameter("regimeId", "Regime", layer.RegimeId, "string"),
                        new WorldGenerationGraphParameter("productAddress", "Product", layer.ProductAddress, "string"),
                        new WorldGenerationGraphParameter("sourceId", "Source", layer.SourceId ?? string.Empty, "string"),
                        new WorldGenerationGraphParameter("sourceKind", "Source Kind", layer.SourceKind ?? string.Empty, "string"),
                        new WorldGenerationGraphParameter("sourceAvailability", "Availability", layer.SourceAvailability ?? string.Empty, "string"),
                        new WorldGenerationGraphParameter("rendererContract", "Renderer", layer.RendererContract ?? string.Empty, "string"),
                    }))
                .ToArray();

        var graph = new WorldGenerationGraphView(
            "world.presentation.layers",
            "Planet Presentation Layers",
            "Layer provenance projected from the current planet presentation document.",
            nodes,
            Array.Empty<WorldGenerationGraphWire>(),
            Annotations: Array.Empty<WorldGenerationGraphAnnotation>(),
            OutputNodeIds: nodes.Select(node => node.NodeId).ToArray());

        return new WorldGenerationGraphFamilyDocument(
            DocumentId: "world-generation.presentation-fallback",
            SchemaVersion: 1,
            Revision: document.Revision,
            BaseGraph: graph,
            Graphs: Array.Empty<WorldGenerationGraphView>(),
            RegimeGraphBindings: Array.Empty<WorldRegimeGraphBinding>(),
            GraphOverrides: Array.Empty<WorldGenerationGraphScopedOverride>(),
            LegacyOverrides: Array.Empty<WorldGenerationGraphOverride>(),
            RunHistory: Array.Empty<WorldGenerationRunHistoryEntry>(),
            UpdatedUtc: DateTimeOffset.UtcNow,
            SubgraphBindings: Array.Empty<WorldGenerationSubgraphBinding>(),
            LayerGraphBindings: Array.Empty<WorldLayerGraphBinding>());
    }

    public void UpdateFamily(WorldGenerationGraphFamilyDocument family, long tick)
    {
        ArgumentNullException.ThrowIfNull(family);
        lock (_gate)
        {
            Family = family;
            ActiveTick = tick;
            if (TryResolveGraph(family, ActiveGraphId) is null)
                ActiveGraphId = family.BaseGraph.GraphId;
            RecomposeLocked();
        }

        Changed?.Invoke();
    }

    public void SelectGraph(string graphId) => SelectGraph(graphId, null);

    public void SelectGraph(string graphId, long? tick)
    {
        if (string.IsNullOrWhiteSpace(graphId))
            throw new ArgumentException("Graph id must be non-empty.", nameof(graphId));

        lock (_gate)
        {
            _ = ResolveGraph(Family, graphId);
            ActiveGraphId = graphId;
            if (tick.HasValue)
                ActiveTick = tick.Value;
            RecomposeLocked();
        }

        Changed?.Invoke();
    }

    public void SelectRegime(string scheduleKind, string regimeId, long tick, string? sphereId)
    {
        var graphId = TryResolveRegimeGraphId(Family, scheduleKind, regimeId, sphereId)
            ?? Family.BaseGraph.GraphId;
        SelectGraph(graphId, tick);
    }

    public bool TrySelectLayer(string sphereId, string layerId, string? regimeId, long tick)
    {
        var binding = TryFindLayerBinding(Family, sphereId, layerId, regimeId);
        if (binding is null)
            return false;

        SelectGraph(binding.GraphId, tick);
        return true;
    }

    public void SetTick(long tick)
    {
        lock (_gate)
        {
            ActiveTick = tick;
            RecomposeLocked();
        }

        Changed?.Invoke();
    }

    public Task ApplyEditAsync(GraphEdit edit, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(edit);

        lock (_gate)
        {
            var authored = ResolveGraph(Family, ActiveGraphId);
            var edited = ApplyEdit(authored, edit);
            Family = ReplaceGraph(Family, ActiveGraphId, edited);
            RecomposeLocked();
        }

        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public bool TryGetNodeTypeInfo(string typeId, out GraphNodeTypeInfo? info)
    {
        var node = _activeGraph.Nodes.FirstOrDefault(candidate =>
            string.Equals(candidate.TypeId, typeId, StringComparison.Ordinal));
        if (node is null)
        {
            info = null;
            return false;
        }

        info = new GraphNodeTypeInfo(
            node.TypeId,
            node.Category,
            string.IsNullOrWhiteSpace(node.Summary) ? node.Label : node.Summary,
            node.IsSideEffect,
            node.IsExpensive,
            node.ProviderMetadata,
            node.ExecutionTraits);
        return true;
    }

    public bool TryGetNodePresentation(string nodeId, out GraphNodePresentation? presentation)
    {
        var node = _activeGraph.Nodes.FirstOrDefault(candidate =>
            string.Equals(candidate.NodeId, nodeId, StringComparison.Ordinal));
        if (node is null)
        {
            presentation = null;
            return false;
        }

        presentation = BuildPresentation(node);
        return true;
    }

    private void RecomposeLocked()
    {
        _activeGraph = ComposeEffectiveGraph(Family, ActiveGraphId, ActiveTick);
        Document = CompileForAuthoring(_activeGraph);
        Annotations = MapAnnotations(_activeGraph);
        Subgraphs = MapSubgraphs(Family, ActiveGraphId);
    }

    private static WorldGenerationGraphView ComposeEffectiveGraph(
        WorldGenerationGraphFamilyDocument family,
        string graphId,
        long tick)
    {
        var graph = ResolveGraph(family, graphId);
        var nodes = graph.Nodes.ToList();
        var wires = graph.Wires.ToList();
        var annotations = graph.Annotations?.ToList();

        foreach (var layer in (family.GraphOverrides ?? Array.Empty<WorldGenerationGraphScopedOverride>())
                     .Where(layer => string.Equals(layer.GraphId, graphId, StringComparison.Ordinal)
                                     && layer.Range.Contains(tick))
                     .OrderBy(layer => layer.StrengthOrder)
                     .ThenBy(layer => layer.OverrideId, StringComparer.Ordinal))
        {
            foreach (var edit in layer.Edits)
                ApplyScopedEdit(edit, nodes, wires, annotations);
        }

        return graph with { Nodes = nodes, Wires = wires, Annotations = annotations };
    }

    private static GraphDocument CompileForAuthoring(WorldGenerationGraphView graph)
    {
        var nodes = graph.Nodes
            .Select(node => new GraphNode(node.NodeId, node.TypeId, BuildParams(node)))
            .ToArray();
        var wires = graph.Wires
            .Select(wire => new GraphWire(
                wire.FromNodeId,
                wire.FromPortId,
                wire.ToNodeId,
                wire.ToPortId,
                ToWireKind(wire.KindHint)))
            .ToArray();
        var sink = ResolveSinkNodeId(graph);
        return new GraphDocument(nodes, wires, sink);
    }

    private static JsonObject BuildParams(WorldGenerationGraphNode node)
    {
        var payload = new JsonObject();
        if (node.Parameters is null)
            return payload;

        foreach (var parameter in node.Parameters)
            payload[parameter.Key] = ParseParameterValue(parameter);
        return payload;
    }

    private static JsonNode? ParseParameterValue(WorldGenerationGraphParameter parameter)
    {
        var value = parameter.Value;
        try
        {
            return parameter.KindHint.Trim().ToLowerInvariant() switch
            {
                "int" or "integer" => JsonValue.Create(int.Parse(value, CultureInfo.InvariantCulture)),
                "long" or "tick" or "ticks" => JsonValue.Create(long.Parse(value, CultureInfo.InvariantCulture)),
                "float" or "double" or "number" => JsonValue.Create(double.Parse(value, CultureInfo.InvariantCulture)),
                "bool" or "boolean" => JsonValue.Create(bool.Parse(value)),
                "json" => JsonNode.Parse(value),
                _ => JsonValue.Create(value),
            };
        }
        catch
        {
            return JsonValue.Create(value);
        }
    }

    private static IReadOnlyList<GraphAnnotation> MapAnnotations(WorldGenerationGraphView graph)
        => graph.Annotations?
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
               .ToArray()
           ?? Array.Empty<GraphAnnotation>();

    private static IReadOnlyList<GraphSubgraph> MapSubgraphs(
        WorldGenerationGraphFamilyDocument family,
        string graphId)
        => (family.SubgraphBindings ?? Array.Empty<WorldGenerationSubgraphBinding>())
            .Where(binding => string.Equals(binding.ParentGraphId, graphId, StringComparison.Ordinal))
            .Select(binding =>
            {
                var subgraph = TryResolveGraph(family, binding.SubgraphId);
                return new GraphSubgraph(
                    binding.ParentGraphId,
                    binding.NodeId,
                    binding.SubgraphId,
                    subgraph?.Label ?? binding.SubgraphId,
                    subgraph?.Description,
                    binding.InputPortMap,
                    binding.OutputPortMap);
            })
            .ToArray();

    private static WorldGenerationGraphView ApplyEdit(WorldGenerationGraphView graph, GraphEdit edit)
        => edit switch
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

    private static WorldGenerationGraphView AddNode(WorldGenerationGraphView graph, GraphNode node)
    {
        if (graph.Nodes.Any(existing => string.Equals(existing.NodeId, node.Id, StringComparison.Ordinal)))
            throw new ArgumentException($"Graph '{graph.GraphId}' already contains node '{node.Id}'.");

        var typedNode = new WorldGenerationGraphNode(
            node.Id,
            node.FunctionId,
            node.FunctionId,
            "custom",
            IsSideEffect: true,
            IsExpensive: false,
            Inputs: Array.Empty<WorldGenerationGraphPort>(),
            Outputs: Array.Empty<WorldGenerationGraphPort>(),
            Parameters: node.Params.Select(kv =>
                    new WorldGenerationGraphParameter(kv.Key, kv.Key, FormatParameterValue(kv.Value), "string"))
                .ToArray(),
            Summary: $"Custom authored node '{node.FunctionId}'.");
        return graph with { Nodes = graph.Nodes.Append(typedNode).ToArray() };
    }

    private static WorldGenerationGraphView RemoveNode(WorldGenerationGraphView graph, string nodeId)
    {
        var nodes = graph.Nodes
            .Where(node => !string.Equals(node.NodeId, nodeId, StringComparison.Ordinal))
            .ToArray();
        if (nodes.Length == graph.Nodes.Count)
            throw new ArgumentException($"Graph '{graph.GraphId}' does not contain node '{nodeId}'.");

        var wires = graph.Wires
            .Where(wire => !string.Equals(wire.FromNodeId, nodeId, StringComparison.Ordinal)
                           && !string.Equals(wire.ToNodeId, nodeId, StringComparison.Ordinal))
            .ToArray();
        var annotations = graph.Annotations?
            .Select(annotation => annotation with
            {
                NodeIds = annotation.NodeIds
                    .Where(id => !string.Equals(id, nodeId, StringComparison.Ordinal))
                    .ToArray(),
            })
            .ToArray();

        return graph with { Nodes = nodes, Wires = wires, Annotations = annotations };
    }

    private static WorldGenerationGraphView AddWire(WorldGenerationGraphView graph, GraphWire wire)
    {
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

        return graph with { Wires = graph.Wires.Append(typedWire).ToArray() };
    }

    private static WorldGenerationGraphView RemoveWire(WorldGenerationGraphView graph, GraphEdit.RemoveWire edit)
        => graph with
        {
            Wires = graph.Wires
                .Where(wire =>
                    !string.Equals(wire.FromNodeId, edit.FromNode, StringComparison.Ordinal)
                    || !string.Equals(wire.FromPortId, edit.FromPort, StringComparison.Ordinal)
                    || !string.Equals(wire.ToNodeId, edit.ToNode, StringComparison.Ordinal)
                    || !string.Equals(wire.ToPortId, edit.ToPort, StringComparison.Ordinal))
                .ToArray()
        };

    private static WorldGenerationGraphView SetParam(
        WorldGenerationGraphView graph,
        string nodeId,
        string key,
        JsonNode? value)
    {
        var nodes = graph.Nodes.ToList();
        var index = nodes.FindIndex(node => string.Equals(node.NodeId, nodeId, StringComparison.Ordinal));
        if (index < 0)
            throw new ArgumentException($"Graph '{graph.GraphId}' does not contain node '{nodeId}'.");

        var node = nodes[index];
        if (node.Parameters is null)
            throw new ArgumentException($"Node '{nodeId}' has no parameters.");

        var parameters = node.Parameters.ToList();
        var paramIndex = parameters.FindIndex(parameter => string.Equals(parameter.Key, key, StringComparison.Ordinal));
        if (paramIndex < 0)
            throw new ArgumentException($"Node '{nodeId}' has no parameter '{key}'.");

        parameters[paramIndex] = parameters[paramIndex] with { Value = FormatParameterValue(value) };
        nodes[index] = node with { Parameters = parameters };
        return graph with { Nodes = nodes };
    }

    private static WorldGenerationGraphView AddAnnotation(WorldGenerationGraphView graph, GraphAnnotation annotation)
    {
        var typed = MapAnnotation(annotation);
        var annotations = graph.Annotations?.ToList() ?? new List<WorldGenerationGraphAnnotation>();
        if (annotations.Any(existing => string.Equals(existing.AnnotationId, typed.AnnotationId, StringComparison.Ordinal)))
            throw new ArgumentException($"Graph '{graph.GraphId}' already contains annotation '{typed.AnnotationId}'.");
        annotations.Add(typed);
        return graph with { Annotations = annotations };
    }

    private static WorldGenerationGraphView UpdateAnnotation(WorldGenerationGraphView graph, GraphAnnotation annotation)
    {
        var typed = MapAnnotation(annotation);
        var annotations = graph.Annotations?.ToList() ?? new List<WorldGenerationGraphAnnotation>();
        var index = annotations.FindIndex(existing => string.Equals(existing.AnnotationId, typed.AnnotationId, StringComparison.Ordinal));
        if (index < 0)
            throw new ArgumentException($"Graph '{graph.GraphId}' does not contain annotation '{typed.AnnotationId}'.");
        annotations[index] = typed;
        return graph with { Annotations = annotations };
    }

    private static WorldGenerationGraphView RemoveAnnotation(WorldGenerationGraphView graph, string annotationId)
        => graph with
        {
            Annotations = graph.Annotations?
                .Where(annotation => !string.Equals(annotation.AnnotationId, annotationId, StringComparison.Ordinal))
                .ToArray()
        };

    private static WorldGenerationGraphAnnotation MapAnnotation(GraphAnnotation annotation)
        => new(
            annotation.AnnotationId,
            annotation.Kind,
            annotation.Label,
            new WorldGenerationGraphBounds(
                annotation.Bounds.X,
                annotation.Bounds.Y,
                annotation.Bounds.Width,
                annotation.Bounds.Height),
            annotation.NodeIds,
            annotation.Text,
            annotation.Color);

    private static void ApplyScopedEdit(
        WorldGenerationGraphEdit edit,
        List<WorldGenerationGraphNode> nodes,
        List<WorldGenerationGraphWire> wires,
        List<WorldGenerationGraphAnnotation>? annotations)
    {
        switch (edit.Kind)
        {
            case "remove-node" when !string.IsNullOrWhiteSpace(edit.NodeId):
                nodes.RemoveAll(node => string.Equals(node.NodeId, edit.NodeId, StringComparison.Ordinal));
                wires.RemoveAll(wire =>
                    string.Equals(wire.FromNodeId, edit.NodeId, StringComparison.Ordinal)
                    || string.Equals(wire.ToNodeId, edit.NodeId, StringComparison.Ordinal));
                if (annotations is not null)
                {
                    for (var index = 0; index < annotations.Count; index++)
                    {
                        annotations[index] = annotations[index] with
                        {
                            NodeIds = annotations[index].NodeIds
                                .Where(id => !string.Equals(id, edit.NodeId, StringComparison.Ordinal))
                                .ToArray()
                        };
                    }
                }
                return;
            case "add-wire":
                AddScopedWire(edit, nodes, wires);
                return;
            case "remove-wire":
                wires.RemoveAll(wire =>
                    string.Equals(wire.FromNodeId, edit.FromNodeId, StringComparison.Ordinal)
                    && string.Equals(wire.FromPortId, edit.FromPortId, StringComparison.Ordinal)
                    && string.Equals(wire.ToNodeId, edit.ToNodeId, StringComparison.Ordinal)
                    && string.Equals(wire.ToPortId, edit.ToPortId, StringComparison.Ordinal));
                return;
            case "set-param":
                SetScopedParam(edit, nodes);
                return;
        }
    }

    private static void AddScopedWire(
        WorldGenerationGraphEdit edit,
        IReadOnlyList<WorldGenerationGraphNode> nodes,
        List<WorldGenerationGraphWire> wires)
    {
        if (string.IsNullOrWhiteSpace(edit.FromNodeId)
            || string.IsNullOrWhiteSpace(edit.FromPortId)
            || string.IsNullOrWhiteSpace(edit.ToNodeId)
            || string.IsNullOrWhiteSpace(edit.ToPortId))
            return;

        var fromNode = nodes.FirstOrDefault(node => string.Equals(node.NodeId, edit.FromNodeId, StringComparison.Ordinal));
        var toNode = nodes.FirstOrDefault(node => string.Equals(node.NodeId, edit.ToNodeId, StringComparison.Ordinal));
        var fromPort = fromNode?.Outputs.FirstOrDefault(port => string.Equals(port.PortId, edit.FromPortId, StringComparison.Ordinal));
        var toPort = toNode?.Inputs.FirstOrDefault(port => string.Equals(port.PortId, edit.ToPortId, StringComparison.Ordinal));
        if (fromPort is null || toPort is null)
            return;

        var kindHint = string.IsNullOrWhiteSpace(fromPort.KindHint) ? toPort.KindHint : fromPort.KindHint;
        var wire = new WorldGenerationGraphWire(edit.FromNodeId, edit.FromPortId, edit.ToNodeId, edit.ToPortId, kindHint);
        if (!wires.Any(existing => SameEndpoint(existing, wire)))
            wires.Add(wire);
    }

    private static void SetScopedParam(WorldGenerationGraphEdit edit, List<WorldGenerationGraphNode> nodes)
    {
        if (string.IsNullOrWhiteSpace(edit.NodeId)
            || string.IsNullOrWhiteSpace(edit.ParamKey)
            || edit.ParamValue is null)
            return;

        var index = nodes.FindIndex(node => string.Equals(node.NodeId, edit.NodeId, StringComparison.Ordinal));
        if (index < 0)
            return;
        var node = nodes[index];
        if (node.Parameters is null)
            return;
        var parameters = node.Parameters.ToList();
        var paramIndex = parameters.FindIndex(parameter => string.Equals(parameter.Key, edit.ParamKey, StringComparison.Ordinal));
        if (paramIndex < 0)
            return;
        parameters[paramIndex] = parameters[paramIndex] with { Value = edit.ParamValue };
        nodes[index] = node with { Parameters = parameters };
    }

    private static WorldGenerationGraphFamilyDocument ReplaceGraph(
        WorldGenerationGraphFamilyDocument family,
        string graphId,
        WorldGenerationGraphView graph)
    {
        if (string.Equals(family.BaseGraph.GraphId, graphId, StringComparison.Ordinal))
            return family with { BaseGraph = graph };

        var graphs = family.Graphs.ToList();
        var index = graphs.FindIndex(candidate => string.Equals(candidate.GraphId, graphId, StringComparison.Ordinal));
        if (index < 0)
            throw new ArgumentException($"Graph '{graphId}' does not exist in family '{family.DocumentId}'.");
        graphs[index] = graph;
        return family with { Graphs = graphs };
    }

    private static string? TryResolveRegimeGraphId(
        WorldGenerationGraphFamilyDocument family,
        string scheduleKind,
        string regimeId,
        string? sphereId)
    {
        WorldRegimeGraphBinding? binding = null;
        if (sphereId is not null)
        {
            binding = family.RegimeGraphBindings.FirstOrDefault(candidate =>
                string.Equals(candidate.ScheduleKind, scheduleKind, StringComparison.Ordinal)
                && string.Equals(candidate.RegimeId, regimeId, StringComparison.Ordinal)
                && string.Equals(candidate.SphereId, sphereId, StringComparison.Ordinal));
        }

        binding ??= family.RegimeGraphBindings.FirstOrDefault(candidate =>
            string.Equals(candidate.ScheduleKind, scheduleKind, StringComparison.Ordinal)
            && string.Equals(candidate.RegimeId, regimeId, StringComparison.Ordinal)
            && candidate.SphereId is null);

        return binding?.GraphId;
    }

    private static WorldLayerGraphBinding? TryFindLayerBinding(
        WorldGenerationGraphFamilyDocument family,
        string sphereId,
        string layerId,
        string? regimeId)
    {
        var bindings = family.LayerGraphBindings ?? Array.Empty<WorldLayerGraphBinding>();
        if (regimeId is not null)
        {
            var specific = bindings.FirstOrDefault(candidate =>
                string.Equals(candidate.SphereId, sphereId, StringComparison.Ordinal)
                && string.Equals(candidate.LayerId, layerId, StringComparison.Ordinal)
                && string.Equals(candidate.RegimeId, regimeId, StringComparison.Ordinal));
            if (specific is not null)
                return specific;
        }

        return bindings.FirstOrDefault(candidate =>
            string.Equals(candidate.SphereId, sphereId, StringComparison.Ordinal)
            && string.Equals(candidate.LayerId, layerId, StringComparison.Ordinal)
            && candidate.RegimeId is null);
    }

    private static WorldGenerationGraphView ResolveGraph(WorldGenerationGraphFamilyDocument family, string graphId)
        => TryResolveGraph(family, graphId)
           ?? throw new ArgumentException($"Graph '{graphId}' does not exist in family '{family.DocumentId}'.");

    private static WorldGenerationGraphView? TryResolveGraph(WorldGenerationGraphFamilyDocument family, string graphId)
    {
        if (string.Equals(family.BaseGraph.GraphId, graphId, StringComparison.Ordinal))
            return family.BaseGraph;
        return family.Graphs.FirstOrDefault(candidate => string.Equals(candidate.GraphId, graphId, StringComparison.Ordinal));
    }

    private static string ResolveSinkNodeId(WorldGenerationGraphView graph)
    {
        if (graph.OutputNodeIds is { Count: > 0 })
            return graph.OutputNodeIds[0];
        var withOutgoing = graph.Wires.Select(wire => wire.FromNodeId).ToHashSet(StringComparer.Ordinal);
        return graph.Nodes.FirstOrDefault(node => !withOutgoing.Contains(node.NodeId))?.NodeId
               ?? graph.Nodes.FirstOrDefault()?.NodeId
               ?? "sink";
    }

    private static WireKind ToWireKind(string kindHint)
        => string.Equals(kindHint, "control", StringComparison.OrdinalIgnoreCase)
           || string.Equals(kindHint, "control-flow", StringComparison.OrdinalIgnoreCase)
            ? WireKind.Control
            : WireKind.Data;

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

        if (string.Equals(node.TypeId, LayerSourceFunctionId, StringComparison.Ordinal))
            return SelectParameterLines(node.Parameters, "sourceId", "sourceKind", "availability", "providerId", "importFormat", "rendererContract");

        if (string.Equals(node.TypeId, LayerNormalizeFunctionId, StringComparison.Ordinal))
            return SelectParameterLines(node.Parameters, "selectedSourceId", "selectedSourceKind", "normalizedProductKind", "rendererContract");

        if (string.Equals(node.TypeId, LayerScopeFunctionId, StringComparison.Ordinal))
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

    private static string SafeNodeId(string value)
    {
        var chars = value.Select(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' ? ch : '_').ToArray();
        var id = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(id) ? "layer" : id;
    }

    public sealed class PlanetGenerationTimelineGraphBinding : IDisposable
    {
        private readonly ITimelineController _timeline;
        private readonly PlanetGenerationGraphSource _source;
        private string? _currentRegimeId;
        private bool _disposed;

        public PlanetGenerationTimelineGraphBinding(
            ITimelineController timeline,
            PlanetGenerationGraphSource source)
        {
            _timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _timeline.TickChanged += OnTickChanged;
            _timeline.LayerSelectionChanged += OnLayerSelectionChanged;
            FollowNow();
        }

        public void FollowNow() => Follow(_timeline.Tick, _timeline.SelectedLayer);

        public void Dispose()
        {
            if (_disposed)
                return;
            _timeline.TickChanged -= OnTickChanged;
            _timeline.LayerSelectionChanged -= OnLayerSelectionChanged;
            _disposed = true;
        }

        private void OnTickChanged(long tick) => Follow(tick, _timeline.SelectedLayer);

        private void OnLayerSelectionChanged(TimelineLayerSelection? selection) => Follow(_timeline.Tick, selection);

        private void Follow(long tick, TimelineLayerSelection? selectedLayer)
        {
            if (selectedLayer is not null)
            {
                var schedule = ScheduleFor(selectedLayer.SphereId);
                var regime = schedule.RegimeAt(tick);
                if (regime is not null
                    && _source.TrySelectLayer(selectedLayer.SphereId, selectedLayer.LayerId, regime.RegimeId, tick))
                {
                    _currentRegimeId = regime.RegimeId;
                    return;
                }
            }

            var defaultRegime = _timeline.GeosphereSchedule.RegimeAt(tick);
            if (defaultRegime is null)
            {
                _currentRegimeId = null;
                _source.SetTick(tick);
                return;
            }

            if (!string.Equals(_currentRegimeId, defaultRegime.RegimeId, StringComparison.Ordinal))
            {
                _source.SelectRegime(WorldRegimeScheduleKinds.Sphere, defaultRegime.RegimeId, tick, GeosphereSphereId);
                _currentRegimeId = defaultRegime.RegimeId;
                return;
            }

            _source.SetTick(tick);
        }

        private SphereRegimeSchedule ScheduleFor(string sphereId)
            => string.Equals(sphereId, AtmosphereSphereId, StringComparison.Ordinal)
                ? _timeline.AtmosphereSchedule
                : _timeline.GeosphereSchedule;
    }
}
