using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using BoomHud.Abstractions.Runtime;
using FantaSim.App.NodeGraph;
using FantaSim.App.Ui;

namespace FantaSim.App.Ui.NodeGraph;

/// <summary>
/// Generic BoomHud nodeGraph view over any <see cref="IGraphSource"/>. Renders the source's
/// <see cref="GraphDocument"/> as a MVVM Nodes/Wires surface, forwards wire/unwire/remove-node
/// edits back to the source, and dispatches an optional RUN action. Domains that need richer
/// per-node presentation (custom summaries, previews, palettes) reuse the shared
/// <see cref="NodeItem"/>/<see cref="WireItem"/> records and build their own surface, or wrap an
/// <see cref="IGraphSource"/> with extra metadata before handing it to this view.
/// </summary>
public class NodeGraphViewSource : IViewSource, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private const string CommentBoundaryKind = "comment-boundary";

    private readonly IGraphSource _source;
    private readonly IGraphRuntimeStateSource? _runtimeState;
    private readonly Func<Task<JsonObject>>? _runAsync;
    private readonly string _title;
    private readonly Stack<string> _graphBackStack = new();
    private string _status = "ready";
    private string? _activeGraphId;
    private string? _activeGraphLabel;
    private string? _expectedGraphId;
    private int _revision;
    private bool _disposed;

    public NodeGraphViewSource(
        IGraphSource source,
        Func<Task<JsonObject>>? runAsync = null,
        string? title = null,
        IGraphRuntimeStateSource? runtimeState = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _runtimeState = runtimeState;
        _runAsync = runAsync;
        _title = title ?? $"{source.SourceId} graph";
        _source.Changed += OnSourceChanged;
        if (_runtimeState is not null)
            _runtimeState.RuntimeStateChanged += OnRuntimeStateChanged;
        Populate();
        _expectedGraphId = _activeGraphId;
    }

    public string ViewId => $"{_source.SourceId}-node-graph";

    public event Action? Changed;

    public void Dispose()
    {
        if (_disposed) return;
        _source.Changed -= OnSourceChanged;
        if (_runtimeState is not null)
            _runtimeState.RuntimeStateChanged -= OnRuntimeStateChanged;
        _disposed = true;
    }

    // MVVM surface reflected by the resident binder (by property name: "Nodes" / "Wires").
    public ObservableCollection<NodeItem> Nodes { get; } = new();
    public ObservableCollection<WireItem> Wires { get; } = new();
    public ObservableCollection<AnnotationItem> Annotations { get; } = new();
    public ObservableCollection<SubgraphItem> Subgraphs { get; } = new();

    public RuntimeSurfaceDocument BuildDocument()
    {
        JsonObject MkLabel(string id, string text) => new()
        {
            ["id"] = id,
            ["type"] = "label",
            ["properties"] = new JsonObject { ["text"] = new JsonObject { ["literal"] = text } },
        };

        JsonObject MkButton(string id, string text, string command) => new()
        {
            ["id"] = id,
            ["type"] = "button",
            ["properties"] = new JsonObject { ["text"] = new JsonObject { ["literal"] = text } },
            ["actions"] = new JsonArray { new JsonObject { ["event"] = "pressed", ["command"] = command } },
        };

        var toolbar = new JsonArray { MkLabel("lbl-status", _status) };
        if (_graphBackStack.Count > 0)
            toolbar.Add(MkButton("btn-graph-back", "BACK", "graph-back"));

        if (_source is IGraphAnnotationSource)
            toolbar.Add(MkButton("btn-add-comment-boundary", "ADD COMMENT", "add-comment-boundary"));

        foreach (var subgraph in Subgraphs)
        {
            toolbar.Add(MkButton(
                $"btn-subgraph-{SafeComponentId(subgraph.ParentNodeId)}-{SafeComponentId(subgraph.SubgraphId)}",
                $"OPEN {subgraph.Label}",
                $"open-subgraph:{subgraph.SubgraphId}"));
        }

        if (_runAsync is not null)
        {
            toolbar.Add(MkButton("btn-run", "RUN GRAPH", "run"));
        }

        var root = new JsonObject
        {
            ["protocolVersion"] = "0.1",
            ["surfaceId"] = ViewId,
            ["catalogId"] = "boomhud.runtime.basic.v1",
            ["revision"] = ++_revision,
            ["dataModel"] = BuildDataModel(),
            ["root"] = new JsonObject
            {
                ["id"] = "root",
                ["type"] = "container",
                ["layout"] = new JsonObject { ["type"] = "vertical", ["gap"] = 6 },
                ["children"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["id"] = "toolbar",
                        ["type"] = "container",
                        ["layout"] = new JsonObject { ["type"] = "horizontal", ["gap"] = 8 },
                        ["children"] = toolbar,
                    },
                    MkLabel("lbl-title", ActiveTitle()),
                    new JsonObject
                    {
                        ["id"] = "graph",
                        ["type"] = "nodeGraph",
                        ["layout"] = new JsonObject { ["minHeight"] = 480 },
                        ["properties"] = new JsonObject
                        {
                            ["items"] = new JsonObject
                            {
                                ["binding"] = new JsonObject
                                {
                                    ["path"] = "/graph/nodes",
                                    ["fallback"] = new JsonArray(),
                                },
                            },
                            ["wires"] = new JsonObject
                            {
                                ["binding"] = new JsonObject
                                {
                                    ["path"] = "/graph/wires",
                                    ["fallback"] = new JsonArray(),
                                },
                            },
                        },
                    },
                },
            },
        };

        return root.Deserialize<RuntimeSurfaceDocument>(JsonOptions)
            ?? throw new InvalidOperationException("node-graph view document failed to deserialize.");
    }

    public async void Dispatch(string action, string? componentId)
    {
        if (action == "graph-back")
        {
            NavigateBack();
            return;
        }

        if (action == "add-comment-boundary")
        {
            AddCommentBoundary();
            return;
        }

        const string openSubgraphPrefix = "open-subgraph:";
        if (action.StartsWith(openSubgraphPrefix, StringComparison.Ordinal))
        {
            OpenSubgraph(action[openSubgraphPrefix.Length..]);
            return;
        }

        if (action != "run" || _runAsync is null) return;

        _status = "running…";
        Changed?.Invoke();
        try
        {
            var result = await _runAsync();
            _status = DescribeRunResult(result);
        }
        catch (Exception ex)
        {
            _status = $"failed: {ex.Message}";
        }
        Changed?.Invoke();
    }

    private static string DescribeRunResult(JsonObject result)
    {
        if (result.TryGetPropertyValue("products", out var productsNode)
            && productsNode is JsonArray products
            && products.Count > 0)
        {
            var noun = products.Count == 1 ? "product" : "products";
            var tick = TryReadLong(result, "canonicalTick") ?? TryReadLong(result, "tick");
            return tick is { } value
                ? $"done: {products.Count} {noun} @ tick {value}"
                : $"done: {products.Count} {noun}";
        }

        return "done";
    }

    private static long? TryReadLong(JsonObject result, string key)
    {
        if (!result.TryGetPropertyValue(key, out var node) || node is not JsonValue value)
            return null;

        if (value.TryGetValue<long>(out var longValue))
            return longValue;
        if (value.TryGetValue<int>(out var intValue))
            return intValue;
        if (value.TryGetValue<string>(out var text)
            && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var stringValue))
        {
            return stringValue;
        }

        return null;
    }

    private void OpenSubgraph(string subgraphId)
    {
        if (_source is not IGraphSubgraphSource navigator)
        {
            _status = "subgraphs unavailable";
            Changed?.Invoke();
            return;
        }

        var subgraph = Subgraphs.FirstOrDefault(candidate =>
            string.Equals(candidate.SubgraphId, subgraphId, StringComparison.Ordinal));
        if (subgraph is null)
        {
            _status = $"unknown subgraph: {subgraphId}";
            Changed?.Invoke();
            return;
        }

        var pushedBackStack = false;
        if (!string.Equals(navigator.ActiveGraphId, subgraph.SubgraphId, StringComparison.Ordinal))
        {
            _graphBackStack.Push(navigator.ActiveGraphId);
            pushedBackStack = true;
        }

        _status = $"opened {subgraph.Label}";
        _expectedGraphId = subgraph.SubgraphId;
        try
        {
            navigator.SelectGraph(subgraph.SubgraphId);
        }
        catch (Exception ex)
        {
            if (pushedBackStack && _graphBackStack.Count > 0)
                _graphBackStack.Pop();
            _expectedGraphId = navigator.ActiveGraphId;
            _status = ex.Message;
            Changed?.Invoke();
        }
    }

    private void NavigateBack()
    {
        if (_source is not IGraphSubgraphSource navigator)
        {
            _status = "subgraphs unavailable";
            Changed?.Invoke();
            return;
        }

        while (_graphBackStack.Count > 0)
        {
            var graphId = _graphBackStack.Pop();
            try
            {
                _status = $"opened {graphId}";
                _expectedGraphId = graphId;
                navigator.SelectGraph(graphId);
                return;
            }
            catch (Exception ex)
            {
                _expectedGraphId = navigator.ActiveGraphId;
                _status = ex.Message;
            }
        }

        _status = "ready";
        Changed?.Invoke();
    }

    // Binder mutation hooks (reflected on GraphEdit edits). Forwarded to the source for validation.
    public async void WireNodes(string fromNodeId, int fromSlot, string toNodeId, int toSlot)
    {
        var fromPort = SlotToPortId(fromNodeId, fromSlot, output: true);
        var toPort = SlotToPortId(toNodeId, toSlot, output: false);
        if (fromPort is null || toPort is null) return;
        try { await _source.ApplyEditAsync(new GraphEdit.AddWire(new GraphWire(fromNodeId, fromPort, toNodeId, toPort))); }
        catch (Exception ex) { _status = ex.Message; Changed?.Invoke(); }
    }

    public async void UnwireNodes(string fromNodeId, int fromSlot, string toNodeId, int toSlot)
    {
        var match = Wires.FirstOrDefault(w => w.FromNodeId == fromNodeId && w.FromSlot == fromSlot
                                              && w.ToNodeId == toNodeId && w.ToSlot == toSlot);
        if (match is null) return;
        try { await _source.ApplyEditAsync(new GraphEdit.RemoveWire(match.FromNodeId, match.FromPortId, match.ToNodeId, match.ToPortId)); }
        catch (Exception ex) { _status = ex.Message; Changed?.Invoke(); }
    }

    public async void RemoveNode(string nodeId)
    {
        try { await _source.ApplyEditAsync(new GraphEdit.RemoveNode(nodeId)); }
        catch (Exception ex) { _status = ex.Message; Changed?.Invoke(); }
    }

    public async void AddAnnotation(
        string annotationId,
        string kind,
        string label,
        float x,
        float y,
        float width,
        float height,
        string[] nodeIds,
        string? text,
        string? color)
    {
        await ApplyGraphEditAsync(
            new GraphEdit.AddAnnotation(new GraphAnnotation(
                annotationId,
                kind,
                label,
                new GraphAnnotationBounds(x, y, width, height),
                nodeIds,
                text,
                color)),
            $"added {label}");
    }

    public async void UpdateAnnotation(
        string annotationId,
        string kind,
        string label,
        float x,
        float y,
        float width,
        float height,
        string[] nodeIds,
        string? text,
        string? color)
    {
        await ApplyGraphEditAsync(
            new GraphEdit.UpdateAnnotation(new GraphAnnotation(
                annotationId,
                kind,
                label,
                new GraphAnnotationBounds(x, y, width, height),
                nodeIds,
                text,
                color)),
            $"updated {label}");
    }

    public void SetAnnotationBounds(string annotationId, float x, float y, float width, float height)
    {
        var annotation = FindAnnotation(annotationId);
        if (annotation is null)
        {
            _status = $"unknown annotation: {annotationId}";
            Changed?.Invoke();
            return;
        }

        UpdateAnnotation(
            annotation.AnnotationId,
            annotation.Kind,
            annotation.Label,
            x,
            y,
            width,
            height,
            annotation.NodeIds.ToArray(),
            annotation.Text,
            annotation.Color);
    }

    public async void RemoveAnnotation(string annotationId)
    {
        await ApplyGraphEditAsync(
            new GraphEdit.RemoveAnnotation(annotationId),
            $"removed {annotationId}");
    }

    private void AddCommentBoundary()
    {
        var nodeIds = Nodes.Select(node => node.NodeId).ToArray();
        if (nodeIds.Length == 0)
        {
            _status = "comment boundary needs at least one node";
            Changed?.Invoke();
            return;
        }

        var ordinal = NextAnnotationOrdinal();
        var label = $"Comment {ordinal}";
        AddAnnotation(
            $"comment_{ordinal}",
            CommentBoundaryKind,
            label,
            40,
            40,
            320,
            180,
            nodeIds,
            "Graph note",
            "#6ea8fe");
    }

    private AnnotationItem? FindAnnotation(string annotationId)
        => Annotations.FirstOrDefault(annotation =>
            string.Equals(annotation.AnnotationId, annotationId, StringComparison.Ordinal));

    private int NextAnnotationOrdinal()
    {
        var used = Annotations
            .Select(annotation => annotation.AnnotationId)
            .ToHashSet(StringComparer.Ordinal);

        for (var ordinal = Annotations.Count + 1; ; ordinal++)
        {
            if (!used.Contains($"comment_{ordinal}"))
                return ordinal;
        }
    }

    private async Task ApplyGraphEditAsync(GraphEdit edit, string status)
    {
        _status = status;
        try
        {
            await _source.ApplyEditAsync(edit);
        }
        catch (Exception ex)
        {
            _status = ex.Message;
            Changed?.Invoke();
        }
    }

    private void OnSourceChanged()
    {
        if (_source is IGraphSubgraphSource subgraphSource)
        {
            if (_expectedGraphId is not null
                && !string.Equals(_expectedGraphId, subgraphSource.ActiveGraphId, StringComparison.Ordinal))
            {
                _graphBackStack.Clear();
            }

            _expectedGraphId = subgraphSource.ActiveGraphId;
        }

        Populate();
        Changed?.Invoke();
    }

    private void OnRuntimeStateChanged()
    {
        // Refresh only the runtime-state slice of each node; the graph structure is unchanged.
        var nodeStates = _runtimeState?.NodeStates;
        if (nodeStates is null) return;

        var changed = false;
        for (var i = 0; i < Nodes.Count; i++)
        {
            var node = Nodes[i];
            nodeStates.TryGetValue(node.NodeId, out var state);
            if (!Equals(node.RuntimeState, state))
            {
                Nodes[i] = node with { RuntimeState = state };
                changed = true;
            }
        }

        if (changed)
            Changed?.Invoke();
    }

    private void Populate()
    {
        Nodes.Clear();
        Wires.Clear();
        Annotations.Clear();
        Subgraphs.Clear();
        _activeGraphId = null;
        _activeGraphLabel = null;
        var doc = _source.Document;

        var inPorts = doc.Nodes.ToDictionary(n => n.Id, _ => new List<string>(), StringComparer.Ordinal);
        var outPorts = doc.Nodes.ToDictionary(n => n.Id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var w in doc.Wires)
        {
            if (w.Kind != WireKind.Data) continue;
            if (!outPorts.ContainsKey(w.FromNode) || !inPorts.ContainsKey(w.ToNode)) continue;
            if (!outPorts[w.FromNode].Contains(w.FromPort)) outPorts[w.FromNode].Add(w.FromPort);
            if (!inPorts[w.ToNode].Contains(w.ToPort)) inPorts[w.ToNode].Add(w.ToPort);
        }
        if (outPorts.TryGetValue(doc.SinkNodeId, out var sinkOuts) && sinkOuts.Count == 0)
            sinkOuts.Add("result");

        foreach (var n in doc.Nodes)
        {
            var ins = inPorts[n.Id].Select(p => new PortItem(p, p, "data", true)).ToList();
            var outs = outPorts[n.Id].Select(p => new PortItem(p, p, "data", false)).ToList();

            var category = "graph";
            var summary = n.FunctionId;
            var isSideEffect = true;
            var isExpensive = false;
            FunctionProviderMetadata? metadata = null;
            FunctionExecutionTraits? traits = null;
            if (_source is IGraphNodeMetadataSource metadataSource
                && metadataSource.TryGetNodeTypeInfo(n.FunctionId, out var nodeType)
                && nodeType is not null)
            {
                category = nodeType.Category;
                summary = nodeType.Summary;
                isSideEffect = nodeType.IsSideEffect;
                isExpensive = nodeType.IsExpensive;
                metadata = nodeType.ProviderMetadata;
                traits = nodeType.ExecutionTraits;
            }

            GraphNodePresentation? presentation = null;
            if (_source is IGraphNodePresentationSource presentationSource)
                presentationSource.TryGetNodePresentation(n.Id, out presentation);

            var label = !string.IsNullOrWhiteSpace(presentation?.Label)
                ? presentation.Label!
                : n.FunctionId;
            if (!string.IsNullOrWhiteSpace(presentation?.Summary))
                summary = presentation.Summary!;
            var detail = presentation?.Detail ?? string.Empty;
            var parameterLines = presentation?.ParameterLines ?? BuildParameterLines(n.Params);

            GraphNodeRuntimeState? runtimeState = null;
            _runtimeState?.NodeStates.TryGetValue(n.Id, out runtimeState);

            Nodes.Add(new NodeItem(
                NodeId: n.Id, TypeId: label, InputCount: ins.Count, OutputCount: outs.Count,
                Category: category, TypeKey: n.FunctionId, Summary: summary,
                Detail: detail, IsSideEffect: isSideEffect, IsExpensive: isExpensive,
                Inputs: ins, Outputs: outs,
                ParameterLines: parameterLines,
                ProviderMetadata: metadata, ExecutionTraits: traits,
                RuntimeState: runtimeState));
        }

        foreach (var w in doc.Wires)
        {
            if (w.Kind != WireKind.Data) continue;
            if (!outPorts.ContainsKey(w.FromNode) || !inPorts.ContainsKey(w.ToNode)) continue;
            Wires.Add(new WireItem(
                w.FromNode, outPorts[w.FromNode].IndexOf(w.FromPort),
                w.ToNode, inPorts[w.ToNode].IndexOf(w.ToPort),
                w.FromPort, w.ToPort, "data"));
        }

        if (_source is IGraphAnnotationSource annotationSource)
        {
            foreach (var annotation in annotationSource.Annotations)
            {
                Annotations.Add(new AnnotationItem(
                    annotation.AnnotationId,
                    annotation.Kind,
                    annotation.Label,
                    new AnnotationBoundsItem(
                        annotation.Bounds.X,
                        annotation.Bounds.Y,
                        annotation.Bounds.Width,
                        annotation.Bounds.Height),
                    annotation.NodeIds,
                    annotation.Text,
                    annotation.Color));
            }
        }

        if (_source is IGraphSubgraphSource subgraphSource)
        {
            _activeGraphId = subgraphSource.ActiveGraphId;
            _activeGraphLabel = subgraphSource.ActiveGraphLabel;
            foreach (var subgraph in subgraphSource.Subgraphs)
            {
                Subgraphs.Add(new SubgraphItem(
                    subgraph.ParentGraphId,
                    subgraph.ParentNodeId,
                    subgraph.SubgraphId,
                    subgraph.Label,
                    subgraph.Description,
                    subgraph.InputPortMap,
                    subgraph.OutputPortMap));
            }
        }
    }

    private string? SlotToPortId(string nodeId, int slot, bool output)
    {
        var node = Nodes.FirstOrDefault(n => n.NodeId == nodeId);
        if (node is null) return null;
        var ports = output ? node.Outputs : node.Inputs;
        return slot >= 0 && slot < ports.Count ? ports[slot].PortId : null;
    }

    private JsonObject BuildDataModel()
    {
        var nodes = new JsonArray();
        foreach (var node in Nodes)
        {
            nodes.Add(new JsonObject
            {
                ["nodeId"] = node.NodeId,
                ["label"] = node.TypeId,
                ["typeId"] = node.TypeKey,
                ["category"] = node.Category,
                ["summary"] = node.Summary,
                ["detail"] = node.Detail,
                ["isSideEffect"] = node.IsSideEffect,
                ["isExpensive"] = node.IsExpensive,
                ["inputCount"] = node.InputCount,
                ["outputCount"] = node.OutputCount,
                ["inputs"] = BuildPorts(node.Inputs),
                ["outputs"] = BuildPorts(node.Outputs),
                ["parameterLines"] = BuildStringArray(node.ParameterLines ?? Array.Empty<string>()),
                ["parameters"] = BuildParameters(node.Parameters ?? Array.Empty<ParameterItem>()),
                ["provider"] = BuildProviderMetadata(node.ProviderMetadata),
                ["providerLabel"] = BuildProviderLabel(node.ProviderMetadata),
                ["executionTraits"] = BuildExecutionTraits(node.ExecutionTraits),
                ["chips"] = BuildNodeChips(node),
                ["providerLines"] = BuildProviderLines(node.ProviderMetadata, node.ExecutionTraits),
                ["runtimeState"] = BuildNodeRuntimeState(node.RuntimeState),
            });
        }

        var wires = new JsonArray();
        foreach (var wire in Wires)
        {
            wires.Add(new JsonObject
            {
                ["fromNodeId"] = wire.FromNodeId,
                ["fromSlot"] = wire.FromSlot,
                ["toNodeId"] = wire.ToNodeId,
                ["toSlot"] = wire.ToSlot,
                ["fromPortId"] = wire.FromPortId,
                ["toPortId"] = wire.ToPortId,
                ["kindHint"] = wire.KindHint,
            });
        }

        var annotations = new JsonArray();
        foreach (var annotation in Annotations)
        {
            annotations.Add(new JsonObject
            {
                ["annotationId"] = annotation.AnnotationId,
                ["kind"] = annotation.Kind,
                ["label"] = annotation.Label,
                ["bounds"] = new JsonObject
                {
                    ["x"] = annotation.Bounds.X,
                    ["y"] = annotation.Bounds.Y,
                    ["width"] = annotation.Bounds.Width,
                    ["height"] = annotation.Bounds.Height,
                },
                ["nodeIds"] = BuildStringArray(annotation.NodeIds),
                ["text"] = annotation.Text,
                ["color"] = annotation.Color,
            });
        }

        var subgraphs = new JsonArray();
        foreach (var subgraph in Subgraphs)
        {
            subgraphs.Add(new JsonObject
            {
                ["parentGraphId"] = subgraph.ParentGraphId,
                ["parentNodeId"] = subgraph.ParentNodeId,
                ["subgraphId"] = subgraph.SubgraphId,
                ["label"] = subgraph.Label,
                ["description"] = subgraph.Description,
                ["inputPortMap"] = BuildStringMap(subgraph.InputPortMap),
                ["outputPortMap"] = BuildStringMap(subgraph.OutputPortMap),
            });
        }

        return new JsonObject
        {
            ["graph"] = new JsonObject
            {
                ["status"] = _status,
                ["title"] = _title,
                ["activeGraphId"] = _activeGraphId,
                ["activeGraphLabel"] = _activeGraphLabel,
                ["revision"] = _revision,
                ["runState"] = BuildRunState(_runtimeState?.RunState),
                ["nodes"] = nodes,
                ["wires"] = wires,
                ["annotations"] = annotations,
                ["subgraphs"] = subgraphs,
            },
        };
    }

    private string ActiveTitle()
        => string.IsNullOrWhiteSpace(_activeGraphLabel) ? _title : $"{_title} - {_activeGraphLabel}";

    private static JsonArray BuildPorts(IReadOnlyList<PortItem> ports)
    {
        var result = new JsonArray();
        foreach (var port in ports)
        {
            result.Add(new JsonObject
            {
                ["portId"] = port.PortId,
                ["label"] = port.Label,
                ["kindHint"] = port.KindHint,
                ["required"] = port.Required,
            });
        }
        return result;
    }

    private static JsonArray BuildStringArray(IEnumerable<string> values)
    {
        var result = new JsonArray();
        foreach (var v in values) result.Add(v);
        return result;
    }

    private static JsonObject BuildStringMap(IReadOnlyDictionary<string, string>? values)
    {
        var result = new JsonObject();
        if (values is null)
            return result;

        foreach (var (key, value) in values)
            result[key] = value;
        return result;
    }

    private static JsonObject? BuildRunState(GraphRunState? state)
    {
        if (state is null)
            return null;

        return new JsonObject
        {
            ["runId"] = state.RunId,
            ["status"] = state.Status.ToString(),
            ["startedAt"] = state.StartedAt?.ToString("O"),
            ["endedAt"] = state.EndedAt?.ToString("O"),
            ["errorMessage"] = state.ErrorMessage,
        };
    }

    private static JsonObject? BuildNodeRuntimeState(GraphNodeRuntimeState? state)
    {
        if (state is null)
            return null;

        return new JsonObject
        {
            ["nodeId"] = state.NodeId,
            ["status"] = state.Status.ToString(),
            ["startedAt"] = state.StartedAt?.ToString("O"),
            ["endedAt"] = state.EndedAt?.ToString("O"),
            ["inputsJson"] = state.InputsJson,
            ["outputsJson"] = state.OutputsJson,
            ["logsJson"] = state.LogsJson,
            ["artifactsJson"] = state.ArtifactsJson,
            ["progress"] = state.Progress,
        };
    }

    private static JsonObject? BuildProviderMetadata(FunctionProviderMetadata? metadata)
    {
        if (metadata is null)
            return null;

        return new JsonObject
        {
            ["kind"] = metadata.ProviderKind,
            ["id"] = metadata.ProviderId,
            ["runtimeRequirement"] = metadata.RuntimeRequirement,
            ["determinism"] = metadata.Determinism,
            ["trustLevel"] = metadata.TrustLevel,
        };
    }

    private static string? BuildProviderLabel(FunctionProviderMetadata? metadata)
    {
        if (metadata is null)
            return null;

        var kind = metadata.ProviderKind;
        var id = metadata.ProviderId;
        return !string.IsNullOrWhiteSpace(id)
            ? $"{kind} / {id}"
            : kind;
    }

    private static JsonObject? BuildExecutionTraits(FunctionExecutionTraits? traits)
    {
        if (traits is null)
            return null;

        return new JsonObject
        {
            ["requiresExternalProcess"] = traits.RequiresExternalProcess,
            ["requiresNetwork"] = traits.RequiresNetwork,
            ["requiresMainThread"] = traits.RequiresMainThread,
            ["isDeterministic"] = traits.IsDeterministic,
            ["supportsCancellation"] = traits.SupportsCancellation,
            ["defaultTimeoutSeconds"] = traits.DefaultTimeoutSeconds,
            ["cacheKeyShape"] = traits.CacheKeyShape,
            ["artifactShape"] = traits.ArtifactShape,
            ["commitEligibility"] = traits.CommitEligibility,
        };
    }

    private static JsonArray BuildNodeChips(NodeItem node)
    {
        var chips = new JsonArray();

        if (!string.IsNullOrWhiteSpace(node.Category) && !string.Equals(node.Category, "graph", StringComparison.Ordinal))
            chips.Add(node.Category);

        if (node.ProviderMetadata is { } metadata)
        {
            if (!string.IsNullOrWhiteSpace(metadata.ProviderKind))
                chips.Add(metadata.ProviderKind);
            if (!string.IsNullOrWhiteSpace(metadata.ProviderId))
                chips.Add(metadata.ProviderId);
        }

        if (node.IsSideEffect)
            chips.Add("side-effect");
        if (node.IsExpensive)
            chips.Add("expensive");

        if (node.ExecutionTraits is { } traits)
        {
            if (traits.RequiresExternalProcess == true)
                chips.Add("external-process");
            if (traits.RequiresNetwork == true)
                chips.Add("network");
            if (traits.RequiresMainThread == true)
                chips.Add("main-thread");
            if (traits.SupportsCancellation == true)
                chips.Add("cancellable");
            if (traits.IsDeterministic == false)
                chips.Add("non-deterministic");
            if (!string.IsNullOrWhiteSpace(traits.CommitEligibility))
                chips.Add(traits.CommitEligibility);
        }

        return chips;
    }

    private static JsonArray BuildProviderLines(
        FunctionProviderMetadata? metadata,
        FunctionExecutionTraits? traits)
    {
        var lines = FunctionProviderDetailFormatter.Format(metadata, traits);
        var result = new JsonArray();
        foreach (var line in lines)
            result.Add(line);
        return result;
    }

    private static string SafeComponentId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "subgraph";

        var chars = value.Select(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' ? ch : '-').ToArray();
        var safe = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(safe) ? "subgraph" : safe;
    }

    private static IReadOnlyList<string> BuildParameterLines(JsonObject parameters)
    {
        if (parameters.Count == 0)
            return Array.Empty<string>();

        var lines = new List<string>(parameters.Count);
        foreach (var (key, value) in parameters)
        {
            var formatted = FormatParameterValue(value);
            if (string.IsNullOrWhiteSpace(formatted))
                continue;

            lines.Add($"{HumanizeKey(key)}: {Shorten(formatted, 64)}");
        }

        return lines;
    }

    private static string FormatParameterValue(JsonNode? value)
    {
        if (value is null)
            return string.Empty;
        if (value is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<string>(out var stringValue))
                return stringValue;
            if (jsonValue.TryGetValue<int>(out var intValue))
                return intValue.ToString(CultureInfo.InvariantCulture);
            if (jsonValue.TryGetValue<long>(out var longValue))
                return longValue.ToString(CultureInfo.InvariantCulture);
            if (jsonValue.TryGetValue<double>(out var doubleValue))
                return doubleValue.ToString(CultureInfo.InvariantCulture);
            if (jsonValue.TryGetValue<bool>(out var boolValue))
                return boolValue ? "true" : "false";
        }

        return value.ToJsonString();
    }

    private static string HumanizeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return "Value";

        var words = new List<string>();
        var start = 0;
        for (var i = 1; i < key.Length; i++)
        {
            if (char.IsUpper(key[i]) && !char.IsUpper(key[i - 1]))
            {
                words.Add(key[start..i]);
                start = i;
            }
        }
        words.Add(key[start..]);
        return string.Join(' ', words.Select(word =>
            word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..]));
    }

    private static string Shorten(string value, int maxLength)
    {
        if (value.Length <= maxLength)
            return value;
        return value[..Math.Max(1, maxLength - 1)] + ".";
    }

    private static JsonArray BuildParameters(IReadOnlyList<ParameterItem> parameters)
    {
        var result = new JsonArray();
        foreach (var p in parameters)
        {
            result.Add(new JsonObject
            {
                ["key"] = p.Key,
                ["label"] = p.Label,
                ["value"] = p.Value,
                ["kindHint"] = p.KindHint,
            });
        }
        return result;
    }
}
