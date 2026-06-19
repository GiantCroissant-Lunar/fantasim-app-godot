using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using BoomHud.Abstractions.Runtime;
using FantaSim.App.Ui;

namespace FantaSim.App.Iii;

/// <summary>
/// A BoomHud <c>nodeGraph</c> view over an executable iii <see cref="GraphDocument"/> — the visual
/// surface for the text→3D pipeline (the editor replacement for the hard-coded pipeline-worker DAG).
/// The resident BoomHudGraphEditBinder reflects over <see cref="Nodes"/>/<see cref="Wires"/> to fill a
/// GraphEdit; RUN executes the graph through the supplied <see cref="GraphExecutor"/>.
/// </summary>
public sealed class IiiGraphViewSource : IViewSource
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly GraphDocument _graph;
    private readonly Func<GraphExecutor?> _resolveExecutor;
    private string _status = "ready";
    private int _revision;

    public IiiGraphViewSource(GraphDocument graph, Func<GraphExecutor?> resolveExecutor)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _resolveExecutor = resolveExecutor ?? throw new ArgumentNullException(nameof(resolveExecutor));
        Populate();
    }

    public string ViewId => "iii-graph";
    public event Action? Changed;

    // MVVM surface reflected by the resident binder/enhancer (by property name).
    public ObservableCollection<NodeItem> Nodes { get; } = new();
    public ObservableCollection<WireItem> Wires { get; } = new();

    private void Populate()
    {
        Nodes.Clear();
        Wires.Clear();

        // Derive each node's input/output ports from the wires (+ a visible output for the sink).
        var inPorts = _graph.Nodes.ToDictionary(n => n.Id, _ => new List<string>(), StringComparer.Ordinal);
        var outPorts = _graph.Nodes.ToDictionary(n => n.Id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var w in _graph.Wires)
        {
            if (!outPorts[w.FromNode].Contains(w.FromPort)) outPorts[w.FromNode].Add(w.FromPort);
            if (!inPorts[w.ToNode].Contains(w.ToPort)) inPorts[w.ToNode].Add(w.ToPort);
        }
        if (outPorts[_graph.SinkNodeId].Count == 0) outPorts[_graph.SinkNodeId].Add("glb_path");

        foreach (var n in _graph.Nodes)
        {
            var ins = inPorts[n.Id].Select(p => new PortItem(p, p, "data", true)).ToList();
            var outs = outPorts[n.Id].Select(p => new PortItem(p, p, "data", false)).ToList();
            Nodes.Add(new NodeItem(
                NodeId: n.Id, TypeId: n.FunctionId, InputCount: ins.Count, OutputCount: outs.Count,
                Category: "iii", TypeKey: n.FunctionId, Summary: n.FunctionId,
                Detail: n.Params.ToJsonString(), IsSideEffect: true,
                IsExpensive: n.FunctionId is "comfy.generate" or "blender.refine" or "asset.to_gltf",
                Inputs: ins, Outputs: outs));
        }

        foreach (var w in _graph.Wires)
        {
            Wires.Add(new WireItem(
                w.FromNode, outPorts[w.FromNode].IndexOf(w.FromPort),
                w.ToNode, inPorts[w.ToNode].IndexOf(w.ToPort),
                w.FromPort, w.ToPort, "data"));
        }
    }

    public RuntimeSurfaceDocument BuildDocument()
    {
        var doc = new JsonObject
        {
            ["protocolVersion"] = "0.1",
            ["surfaceId"] = ViewId,
            ["catalogId"] = "boomhud.runtime.basic.v1",
            ["revision"] = ++_revision,
            ["dataModel"] = new JsonObject { ["status"] = _status },
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
                        ["children"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["id"] = "btn-run",
                                ["type"] = "button",
                                ["properties"] = new JsonObject { ["text"] = new JsonObject { ["literal"] = "RUN GRAPH" } },
                                ["actions"] = new JsonArray { new JsonObject { ["event"] = "pressed", ["command"] = "run" } },
                            },
                        },
                    },
                    new JsonObject
                    {
                        ["id"] = "lbl-status",
                        ["type"] = "label",
                        ["properties"] = new JsonObject { ["text"] = new JsonObject { ["literal"] = $"iii graph — {_status}" } },
                    },
                    new JsonObject
                    {
                        ["id"] = "graph",
                        ["type"] = "nodeGraph",
                        ["layout"] = new JsonObject { ["minHeight"] = 480 },
                    },
                },
            },
        };

        return doc.Deserialize<RuntimeSurfaceDocument>(JsonOptions)
            ?? throw new InvalidOperationException("iii-graph view document failed to deserialize.");
    }

    public async void Dispatch(string action, string? componentId)
    {
        if (action != "run") return;
        var executor = _resolveExecutor();
        if (executor is null) { _status = "executor unavailable"; Changed?.Invoke(); return; }

        _status = "running…";
        Changed?.Invoke();
        try
        {
            var jobId = Guid.NewGuid().ToString("N")[..8];
            var result = await executor.ExecuteAsync(_graph, new JsonObject { ["job_id"] = jobId });
            _status = $"done → {result["glb_path"]}";
        }
        catch (Exception ex)
        {
            _status = $"failed: {ex.Message}";
        }
        Changed?.Invoke();
    }

    // Binder mutation hooks (reflected on GraphEdit edits). Minimal for now — local re-render.
    public void WireNodes(string fromNodeId, int fromSlot, string toNodeId, int toSlot) => Changed?.Invoke();
    public void UnwireNodes(string fromNodeId, int fromSlot, string toNodeId, int toSlot) => Changed?.Invoke();
    public void RemoveNode(string nodeId) => Changed?.Invoke();
}

/// <summary>MVVM records the resident BoomHud binder + enhancer reflect over by property name.</summary>
public sealed record PortItem(string PortId, string Label, string KindHint, bool Required);

public sealed record NodeItem(
    string NodeId, string TypeId, int InputCount, int OutputCount, string Category, string TypeKey,
    string Summary, string Detail, bool IsSideEffect, bool IsExpensive,
    IReadOnlyList<PortItem> Inputs, IReadOnlyList<PortItem> Outputs,
    IReadOnlyList<string>? ParameterLines = null, IReadOnlyList<ParameterItem>? Parameters = null,
    int PreviewWidth = 0, int PreviewHeight = 0, byte[]? PreviewRgba = null);

public sealed record ParameterItem(string Key, string Label, string Value, string KindHint);

public sealed record WireItem(
    string FromNodeId, int FromSlot, string ToNodeId, int ToSlot, string FromPortId, string ToPortId, string KindHint);
