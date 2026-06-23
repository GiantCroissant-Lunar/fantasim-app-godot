using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

    private readonly IGraphSource _source;
    private readonly Func<Task<JsonObject>>? _runAsync;
    private readonly string _title;
    private string _status = "ready";
    private int _revision;
    private bool _disposed;

    public NodeGraphViewSource(IGraphSource source, Func<Task<JsonObject>>? runAsync = null, string? title = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _runAsync = runAsync;
        _title = title ?? $"{source.SourceId} graph";
        _source.Changed += OnSourceChanged;
        Populate();
    }

    public string ViewId => $"{_source.SourceId}-node-graph";

    public event Action? Changed;

    public void Dispose()
    {
        if (_disposed) return;
        _source.Changed -= OnSourceChanged;
        _disposed = true;
    }

    // MVVM surface reflected by the resident binder (by property name: "Nodes" / "Wires").
    public ObservableCollection<NodeItem> Nodes { get; } = new();
    public ObservableCollection<WireItem> Wires { get; } = new();

    public RuntimeSurfaceDocument BuildDocument()
    {
        JsonObject MkLabel(string id, string text) => new()
        {
            ["id"] = id,
            ["type"] = "label",
            ["properties"] = new JsonObject { ["text"] = new JsonObject { ["literal"] = text } },
        };

        var toolbar = new JsonArray { MkLabel("lbl-status", _status) };
        if (_runAsync is not null)
        {
            toolbar.Add(new JsonObject
            {
                ["id"] = "btn-run",
                ["type"] = "button",
                ["properties"] = new JsonObject { ["text"] = new JsonObject { ["literal"] = "RUN GRAPH" } },
                ["actions"] = new JsonArray { new JsonObject { ["event"] = "pressed", ["command"] = "run" } },
            });
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
                    MkLabel("lbl-title", _title),
                    new JsonObject
                    {
                        ["id"] = "graph",
                        ["type"] = "nodeGraph",
                        ["layout"] = new JsonObject { ["minHeight"] = 480 },
                    },
                },
            },
        };

        return root.Deserialize<RuntimeSurfaceDocument>(JsonOptions)
            ?? throw new InvalidOperationException("node-graph view document failed to deserialize.");
    }

    public async void Dispatch(string action, string? componentId)
    {
        if (action != "run" || _runAsync is null) return;

        _status = "running…";
        Changed?.Invoke();
        try
        {
            await _runAsync();
            _status = "done";
        }
        catch (Exception ex)
        {
            _status = $"failed: {ex.Message}";
        }
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

    private void OnSourceChanged()
    {
        Populate();
        Changed?.Invoke();
    }

    private void Populate()
    {
        Nodes.Clear();
        Wires.Clear();
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
            Nodes.Add(new NodeItem(
                NodeId: n.Id, TypeId: n.FunctionId, InputCount: ins.Count, OutputCount: outs.Count,
                Category: "graph", TypeKey: n.FunctionId, Summary: n.FunctionId,
                Detail: n.Params.ToJsonString(), IsSideEffect: true, IsExpensive: false,
                Inputs: ins, Outputs: outs));
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

        return new JsonObject
        {
            ["graph"] = new JsonObject
            {
                ["status"] = _status,
                ["title"] = _title,
                ["revision"] = _revision,
                ["nodes"] = nodes,
                ["wires"] = wires,
            },
        };
    }

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
