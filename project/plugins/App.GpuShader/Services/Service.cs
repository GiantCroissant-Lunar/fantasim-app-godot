using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.GpuShader.Providers;
using Microsoft.Extensions.Logging;

namespace FantaSim.App.GpuShader.Services;

/// <summary>
/// Engine-agnostic GPU shader graph authoring service. It edits and validates graph DTOs and
/// never references Godot Shader, ShaderMaterial, RenderingDevice, or RID types.
/// </summary>
public sealed class Service : IService, IDisposable
{
    private const string ErrorSeverity = "error";
    private const string WarningSeverity = "warning";

    private static readonly IReadOnlyList<GpuShaderNodeSchema> ShaderNodeCatalog = Array.AsReadOnly(new[]
    {
        new GpuShaderNodeSchema(
            "shader.canvas-item",
            "Canvas Item Shader",
            "Shader",
            IsSideEffect: false,
            IsExpensive: false,
            Inputs: Array.Empty<GpuShaderGraphPort>(),
            Outputs: new[] { new GpuShaderGraphPort("target", "Target", "gpu/shader-target", Required: false) },
            Summary: "Declares a Godot canvas_item shader target."),
        new GpuShaderNodeSchema(
            "input.vertex-color",
            "Vertex Color",
            "Input",
            IsSideEffect: false,
            IsExpensive: false,
            Inputs: Array.Empty<GpuShaderGraphPort>(),
            Outputs: new[] { new GpuShaderGraphPort("color", "Color", "gpu/vec4", Required: false) },
            Summary: "Reads the incoming vertex COLOR value."),
        new GpuShaderNodeSchema(
            "output.fragment-color",
            "Fragment Color",
            "Output",
            IsSideEffect: false,
            IsExpensive: false,
            Inputs: new[] { new GpuShaderGraphPort("color", "Color", "gpu/vec4", Required: true) },
            Outputs: new[] { new GpuShaderGraphPort("shader", "Shader", "gpu/shader-source", Required: false) },
            Summary: "Writes a fragment color expression for a material shader."),
        new GpuShaderNodeSchema(
            "shader.compute",
            "Compute Shader",
            "Shader",
            IsSideEffect: false,
            IsExpensive: false,
            Inputs: Array.Empty<GpuShaderGraphPort>(),
            Outputs: new[] { new GpuShaderGraphPort("shader", "Shader", "gpu/compute-shader", Required: false) },
            Summary: "References an imported Godot RDShaderFile by resource path.",
            Parameters: new[]
            {
                new GpuShaderGraphParameter("shaderId", "Shader ID", "shader.compute", "string"),
                new GpuShaderGraphParameter("shaderPath", "Shader Path", "res://shaders/compute.glsl", "resource-path"),
            }),
        new GpuShaderNodeSchema(
            "buffer.storage",
            "Storage Buffer",
            "Buffer",
            IsSideEffect: false,
            IsExpensive: false,
            Inputs: Array.Empty<GpuShaderGraphPort>(),
            Outputs: new[] { new GpuShaderGraphPort("buffer", "Buffer", "gpu/storage-buffer", Required: false) },
            Summary: "Declares one storage-buffer binding and optional readback label.",
            Parameters: new[]
            {
                new GpuShaderGraphParameter("set", "Set", "0", "uint"),
                new GpuShaderGraphParameter("binding", "Binding", "0", "uint"),
                new GpuShaderGraphParameter("label", "Label", "buffer", "string"),
                new GpuShaderGraphParameter("readbackBytes", "Readback Bytes", "", "uint?"),
                new GpuShaderGraphParameter("readbackLabel", "Readback Label", "", "string?"),
            }),
        new GpuShaderNodeSchema(
            "dispatch.groups",
            "Dispatch Groups",
            "Dispatch",
            IsSideEffect: false,
            IsExpensive: false,
            Inputs: Array.Empty<GpuShaderGraphPort>(),
            Outputs: new[] { new GpuShaderGraphPort("groups", "Groups", "gpu/dispatch-groups", Required: false) },
            Summary: "Provides X/Y/Z workgroup counts for a compute dispatch.",
            Parameters: new[]
            {
                new GpuShaderGraphParameter("x", "X", "1", "uint"),
                new GpuShaderGraphParameter("y", "Y", "1", "uint"),
                new GpuShaderGraphParameter("z", "Z", "1", "uint"),
            }),
        new GpuShaderNodeSchema(
            "gpu.dispatch",
            "Dispatch",
            "Dispatch",
            IsSideEffect: true,
            IsExpensive: true,
            Inputs: new[]
            {
                new GpuShaderGraphPort("shader", "Shader", "gpu/compute-shader", Required: true),
                new GpuShaderGraphPort("groups", "Groups", "gpu/dispatch-groups", Required: true),
                new GpuShaderGraphPort("buffers", "Buffers", "gpu/storage-buffer[]", Required: false),
            },
            Outputs: new[] { new GpuShaderGraphPort("result", "Result", "gpu/dispatch-result", Required: false) },
            Summary: "Dispatches one compute shader through the resident RenderingDevice backend."),
    });

    private static readonly IReadOnlyDictionary<string, GpuShaderNodeSchema> ShaderNodeCatalogByType =
        ShaderNodeCatalog.ToDictionary(schema => schema.TypeId, StringComparer.Ordinal);

    private readonly object _shaderGraphLock = new();
    private readonly IShaderGraphBackend _backend;
    private GpuShaderGraphView _shaderGraph = CreateDefaultShaderGraph();
    private bool _disposed;

    public Service(IShaderGraphBackend backend, ILoggerFactory loggerFactory)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        if (loggerFactory is null) throw new ArgumentNullException(nameof(loggerFactory));
    }

    public event Action? ShaderGraphChanged;

    public Task<GpuShaderGraphView> GetShaderGraphAsync(CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        lock (_shaderGraphLock)
        {
            return Task.FromResult(_shaderGraph);
        }
    }

    public Task<IReadOnlyList<GpuShaderNodeSchema>> GetShaderNodeCatalogAsync(CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ShaderNodeCatalog);
    }

    public Task<GpuShaderGraphView> ApplyShaderGraphEditAsync(
        GpuShaderGraphEdit edit,
        CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (edit is null) throw new ArgumentNullException(nameof(edit));

        GpuShaderGraphView updated;
        lock (_shaderGraphLock)
        {
            updated = ApplyShaderGraphEdit(_shaderGraph, edit);
            _shaderGraph = updated;
        }

        ShaderGraphChanged?.Invoke();
        return Task.FromResult(updated);
    }

    public Task<GpuShaderGraphValidationResult> ValidateShaderGraphAsync(
        GpuShaderGraphView graph,
        CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (graph is null) throw new ArgumentNullException(nameof(graph));

        return Task.FromResult(ValidateShaderGraph(graph));
    }

    public Task<GpuShaderInspection> InspectShaderAsync(
        string resourcePath,
        CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(resourcePath))
            throw new ArgumentException("Resource path must not be null or whitespace.", nameof(resourcePath));

        return _backend.InspectShaderAsync(resourcePath, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _backend.Dispose();
    }

    private void EnsureNotDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Service));
    }

    private static GpuShaderGraphView CreateDefaultShaderGraph()
    {
        var target = CreateNodeFromSchema("canvas_item", RequireSchema("shader.canvas-item"));
        var color = CreateNodeFromSchema("vertex_color", RequireSchema("input.vertex-color"));
        var output = CreateNodeFromSchema("fragment_output", RequireSchema("output.fragment-color"));
        return new GpuShaderGraphView(
            "gpu-shader.default",
            "GPU Shader",
            "Default GPU canvas-item shader graph.",
            new[] { target, color, output },
            new[]
            {
                new GpuShaderGraphWire("vertex_color", "color", "fragment_output", "color", "gpu/vec4"),
            });
    }

    private static GpuShaderGraphView ApplyShaderGraphEdit(GpuShaderGraphView graph, GpuShaderGraphEdit edit)
    {
        if (string.IsNullOrWhiteSpace(edit.Kind))
            throw new ArgumentException("Edit Kind must not be null or whitespace.", nameof(edit));

        return edit.Kind switch
        {
            "add-node" => AddNode(graph, edit),
            "remove-node" => RemoveNode(graph, edit),
            "add-wire" => AddWire(graph, edit),
            "remove-wire" => RemoveWire(graph, edit),
            "set-param" => SetParameter(graph, edit),
            "reset-default" => CreateDefaultShaderGraph(),
            "clear" => graph with { Nodes = Array.Empty<GpuShaderGraphNode>(), Wires = Array.Empty<GpuShaderGraphWire>() },
            _ => throw new ArgumentException($"Unknown shader graph edit kind: '{edit.Kind}'.", nameof(edit)),
        };
    }

    private static GpuShaderGraphView AddNode(GpuShaderGraphView graph, GpuShaderGraphEdit edit)
    {
        if (string.IsNullOrWhiteSpace(edit.TypeId))
            throw new ArgumentException("add-node requires TypeId.", nameof(edit));
        var schema = RequireSchema(edit.TypeId);
        var nodes = graph.Nodes?.ToList() ?? new List<GpuShaderGraphNode>();
        var nodeId = string.IsNullOrWhiteSpace(edit.NodeId)
            ? AllocateNodeId(schema.TypeId, nodes)
            : edit.NodeId!;

        if (nodes.Any(node => string.Equals(node.NodeId, nodeId, StringComparison.Ordinal)))
            throw new ArgumentException($"Node id '{nodeId}' already exists.", nameof(edit));

        nodes.Add(CreateNodeFromSchema(nodeId, schema));
        return graph with { Nodes = nodes };
    }

    private static GpuShaderGraphView RemoveNode(GpuShaderGraphView graph, GpuShaderGraphEdit edit)
    {
        if (string.IsNullOrWhiteSpace(edit.NodeId))
            throw new ArgumentException("remove-node requires NodeId.", nameof(edit));

        var nodes = graph.Nodes?.ToList() ?? new List<GpuShaderGraphNode>();
        var removed = nodes.RemoveAll(node => string.Equals(node.NodeId, edit.NodeId, StringComparison.Ordinal));
        if (removed == 0)
            throw new ArgumentException($"Node '{edit.NodeId}' does not exist.", nameof(edit));

        var wires = (graph.Wires ?? Array.Empty<GpuShaderGraphWire>())
            .Where(wire => !string.Equals(wire.FromNodeId, edit.NodeId, StringComparison.Ordinal)
                           && !string.Equals(wire.ToNodeId, edit.NodeId, StringComparison.Ordinal))
            .ToArray();

        return graph with { Nodes = nodes, Wires = wires };
    }

    private static GpuShaderGraphView AddWire(GpuShaderGraphView graph, GpuShaderGraphEdit edit)
    {
        RequireWireFields(edit, "add-wire");
        var nodeById = BuildNodeMap(graph);
        var fromNode = RequireNode(nodeById, edit.FromNodeId!, nameof(edit));
        var toNode = RequireNode(nodeById, edit.ToNodeId!, nameof(edit));
        var fromPort = RequirePort(fromNode.Outputs, edit.FromPortId!, fromNode.NodeId, "output", nameof(edit));
        var toPort = RequirePort(toNode.Inputs, edit.ToPortId!, toNode.NodeId, "input", nameof(edit));

        if (!KindMatches(toPort.KindHint, fromPort.KindHint))
            throw new ArgumentException($"Cannot wire output kind '{fromPort.KindHint}' to input kind '{toPort.KindHint}'.", nameof(edit));

        var wires = graph.Wires?.ToList() ?? new List<GpuShaderGraphWire>();
        if (wires.Any(wire => IsSameWire(wire, edit)))
            throw new ArgumentException("Wire already exists.", nameof(edit));

        if (!IsArrayKind(toPort.KindHint)
            && wires.Any(wire => string.Equals(wire.ToNodeId, edit.ToNodeId, StringComparison.Ordinal)
                                 && string.Equals(wire.ToPortId, edit.ToPortId, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"Input '{edit.ToNodeId}.{edit.ToPortId}' accepts only one incoming wire.", nameof(edit));
        }

        var candidate = new GpuShaderGraphWire(edit.FromNodeId!, edit.FromPortId!, edit.ToNodeId!, edit.ToPortId!, fromPort.KindHint);
        var adjacency = BuildAdjacency(graph.Nodes ?? Array.Empty<GpuShaderGraphNode>(), wires.Append(candidate));
        if (ContainsCycle(adjacency))
            throw new ArgumentException("add-wire would create a shader graph cycle.", nameof(edit));

        wires.Add(candidate);
        return graph with { Wires = wires };
    }

    private static GpuShaderGraphView RemoveWire(GpuShaderGraphView graph, GpuShaderGraphEdit edit)
    {
        RequireWireFields(edit, "remove-wire");
        var wires = graph.Wires?.ToList() ?? new List<GpuShaderGraphWire>();
        var removed = wires.RemoveAll(wire => IsSameWire(wire, edit));
        if (removed == 0)
            throw new ArgumentException("Wire does not exist.", nameof(edit));

        return graph with { Wires = wires };
    }

    private static GpuShaderGraphView SetParameter(GpuShaderGraphView graph, GpuShaderGraphEdit edit)
    {
        if (string.IsNullOrWhiteSpace(edit.NodeId))
            throw new ArgumentException("set-param requires NodeId.", nameof(edit));
        if (string.IsNullOrWhiteSpace(edit.ParamKey))
            throw new ArgumentException("set-param requires ParamKey.", nameof(edit));
        if (edit.ParamValue is null)
            throw new ArgumentException("set-param requires ParamValue.", nameof(edit));

        var nodes = graph.Nodes?.ToList() ?? new List<GpuShaderGraphNode>();
        var nodeIndex = nodes.FindIndex(node => string.Equals(node.NodeId, edit.NodeId, StringComparison.Ordinal));
        if (nodeIndex < 0)
            throw new ArgumentException($"Node '{edit.NodeId}' does not exist.", nameof(edit));

        var node = nodes[nodeIndex];
        var parameters = node.Parameters?.ToList() ?? new List<GpuShaderGraphParameter>();
        var paramIndex = parameters.FindIndex(parameter => string.Equals(parameter.Key, edit.ParamKey, StringComparison.Ordinal));
        if (paramIndex < 0)
            throw new ArgumentException($"Parameter '{edit.ParamKey}' does not exist on node '{edit.NodeId}'.", nameof(edit));

        parameters[paramIndex] = parameters[paramIndex] with { Value = edit.ParamValue };
        nodes[nodeIndex] = node with { Parameters = parameters };
        return graph with { Nodes = nodes };
    }

    private static void RequireWireFields(GpuShaderGraphEdit edit, string kind)
    {
        if (string.IsNullOrWhiteSpace(edit.FromNodeId) || string.IsNullOrWhiteSpace(edit.FromPortId)
            || string.IsNullOrWhiteSpace(edit.ToNodeId) || string.IsNullOrWhiteSpace(edit.ToPortId))
        {
            throw new ArgumentException($"{kind} requires FromNodeId, FromPortId, ToNodeId, ToPortId.", nameof(edit));
        }
    }

    private static GpuShaderNodeSchema RequireSchema(string typeId)
    {
        if (!ShaderNodeCatalogByType.TryGetValue(typeId, out var schema))
            throw new ArgumentException($"Unknown GPU shader node type '{typeId}'.");
        return schema;
    }

    private static GpuShaderGraphNode CreateNodeFromSchema(string nodeId, GpuShaderNodeSchema schema)
        => new(
            nodeId,
            schema.TypeId,
            schema.Label,
            schema.Category,
            schema.IsSideEffect,
            schema.IsExpensive,
            schema.Inputs.ToArray(),
            schema.Outputs.ToArray(),
            schema.Parameters?.ToArray());

    private static string AllocateNodeId(string typeId, IReadOnlyCollection<GpuShaderGraphNode> nodes)
    {
        var stem = NormalizeNodeIdStem(typeId);
        var index = 1;
        var candidate = stem;
        while (nodes.Any(node => string.Equals(node.NodeId, candidate, StringComparison.Ordinal)))
        {
            index++;
            candidate = $"{stem}_{index}";
        }

        return candidate;
    }

    private static string NormalizeNodeIdStem(string typeId)
    {
        var chars = typeId
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray();
        var stem = new string(chars).Trim('_');
        return string.IsNullOrWhiteSpace(stem) ? "node" : stem;
    }

    private static Dictionary<string, GpuShaderGraphNode> BuildNodeMap(GpuShaderGraphView graph)
    {
        var result = new Dictionary<string, GpuShaderGraphNode>(StringComparer.Ordinal);
        foreach (var node in graph.Nodes ?? Array.Empty<GpuShaderGraphNode>())
        {
            if (!string.IsNullOrWhiteSpace(node.NodeId))
                result[node.NodeId] = node;
        }

        return result;
    }

    private static GpuShaderGraphNode RequireNode(
        IReadOnlyDictionary<string, GpuShaderGraphNode> nodeById,
        string nodeId,
        string paramName)
    {
        if (!nodeById.TryGetValue(nodeId, out var node))
            throw new ArgumentException($"Node '{nodeId}' does not exist.", paramName);
        return node;
    }

    private static GpuShaderGraphPort RequirePort(
        IReadOnlyList<GpuShaderGraphPort>? ports,
        string portId,
        string nodeId,
        string direction,
        string paramName)
    {
        var port = (ports ?? Array.Empty<GpuShaderGraphPort>())
            .FirstOrDefault(item => string.Equals(item.PortId, portId, StringComparison.Ordinal));
        if (port is null)
            throw new ArgumentException($"{direction} port '{portId}' does not exist on node '{nodeId}'.", paramName);
        return port;
    }

    private static bool IsSameWire(GpuShaderGraphWire wire, GpuShaderGraphEdit edit)
        => string.Equals(wire.FromNodeId, edit.FromNodeId, StringComparison.Ordinal)
           && string.Equals(wire.FromPortId, edit.FromPortId, StringComparison.Ordinal)
           && string.Equals(wire.ToNodeId, edit.ToNodeId, StringComparison.Ordinal)
           && string.Equals(wire.ToPortId, edit.ToPortId, StringComparison.Ordinal);

    private static GpuShaderGraphValidationResult ValidateShaderGraph(GpuShaderGraphView graph)
    {
        var issues = new List<GpuShaderGraphValidationIssue>();
        if (string.IsNullOrWhiteSpace(graph.GraphId))
            AddError(issues, "GraphIdRequired", "GraphId must not be null or whitespace.");

        var nodes = graph.Nodes ?? Array.Empty<GpuShaderGraphNode>();
        var wires = graph.Wires ?? Array.Empty<GpuShaderGraphWire>();
        if (nodes.Count == 0)
            AddWarning(issues, "EmptyGraph", "Shader graph has no nodes.");

        var nodeById = new Dictionary<string, GpuShaderGraphNode>(StringComparer.Ordinal);
        var inputPortsByNode = new Dictionary<string, Dictionary<string, GpuShaderGraphPort>>(StringComparer.Ordinal);
        var outputPortsByNode = new Dictionary<string, Dictionary<string, GpuShaderGraphPort>>(StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            if (node is null)
            {
                AddError(issues, "NullNode", "Shader graph nodes must not contain null entries.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(node.NodeId))
            {
                AddError(issues, "NodeIdRequired", "NodeId must not be null or whitespace.");
                continue;
            }

            if (!nodeById.TryAdd(node.NodeId, node))
            {
                AddError(issues, "DuplicateNodeId", $"Duplicate node id '{node.NodeId}'.", node.NodeId);
                continue;
            }

            if (string.IsNullOrWhiteSpace(node.TypeId))
            {
                AddError(issues, "NodeTypeRequired", "TypeId must not be null or whitespace.", node.NodeId);
            }
            else if (!ShaderNodeCatalogByType.TryGetValue(node.TypeId, out var schema))
            {
                AddError(issues, "UnknownNodeType", $"Unknown GPU shader node type '{node.TypeId}'.", node.NodeId);
            }
            else
            {
                ValidateParameters(node, issues);
            }

            var inputMap = BuildPortMap(node.NodeId, node.Inputs, "input", issues);
            var outputMap = BuildPortMap(node.NodeId, node.Outputs, "output", issues);
            inputPortsByNode[node.NodeId] = inputMap;
            outputPortsByNode[node.NodeId] = outputMap;

            if (node.TypeId is not null && ShaderNodeCatalogByType.TryGetValue(node.TypeId, out var nodeSchema))
                ValidateSchemaPorts(node, nodeSchema, inputMap, outputMap, issues);
        }

        var incomingByInput = new Dictionary<(string NodeId, string PortId), int>();
        var adjacency = nodeById.Keys.ToDictionary(nodeId => nodeId, _ => new List<string>(), StringComparer.Ordinal);

        foreach (var wire in wires)
        {
            if (wire is null)
            {
                AddError(issues, "NullWire", "Shader graph wires must not contain null entries.");
                continue;
            }

            var validEndpoint = true;
            if (!nodeById.ContainsKey(wire.FromNodeId))
            {
                AddError(issues, "UnknownFromNode", $"Wire references missing source node '{wire.FromNodeId}'.", wire.FromNodeId, wire.FromPortId);
                validEndpoint = false;
            }

            if (!nodeById.ContainsKey(wire.ToNodeId))
            {
                AddError(issues, "UnknownToNode", $"Wire references missing target node '{wire.ToNodeId}'.", wire.ToNodeId, wire.ToPortId);
                validEndpoint = false;
            }

            if (!validEndpoint)
                continue;

            if (!outputPortsByNode[wire.FromNodeId].TryGetValue(wire.FromPortId, out var fromPort))
            {
                AddError(issues, "UnknownFromPort", $"Wire references missing output port '{wire.FromPortId}'.", wire.FromNodeId, wire.FromPortId);
                validEndpoint = false;
            }

            if (!inputPortsByNode[wire.ToNodeId].TryGetValue(wire.ToPortId, out var toPort))
            {
                AddError(issues, "UnknownToPort", $"Wire references missing input port '{wire.ToPortId}'.", wire.ToNodeId, wire.ToPortId);
                validEndpoint = false;
            }

            if (!validEndpoint)
                continue;

            if (string.IsNullOrWhiteSpace(wire.KindHint))
            {
                AddError(issues, "WireKindRequired", "Wire KindHint must not be null or whitespace.", wire.ToNodeId, wire.ToPortId);
            }
            else
            {
                if (!KindMatches(fromPort!.KindHint, wire.KindHint))
                    AddError(issues, "WireOutputKindMismatch", $"Wire kind '{wire.KindHint}' does not match output kind '{fromPort.KindHint}'.", wire.FromNodeId, wire.FromPortId);
                if (!KindMatches(toPort!.KindHint, wire.KindHint))
                    AddError(issues, "WireInputKindMismatch", $"Wire kind '{wire.KindHint}' does not match input kind '{toPort.KindHint}'.", wire.ToNodeId, wire.ToPortId);
            }

            var inputKey = (wire.ToNodeId, wire.ToPortId);
            incomingByInput.TryGetValue(inputKey, out var incomingCount);
            incomingByInput[inputKey] = incomingCount + 1;
            if (!IsArrayKind(toPort!.KindHint) && incomingCount > 0)
                AddError(issues, "MultipleScalarInputs", $"Input '{wire.ToNodeId}.{wire.ToPortId}' accepts only one incoming wire.", wire.ToNodeId, wire.ToPortId);

            adjacency[wire.FromNodeId].Add(wire.ToNodeId);
        }

        foreach (var node in nodeById.Values)
        {
            var requiredInputs = node.TypeId is not null && ShaderNodeCatalogByType.TryGetValue(node.TypeId, out var schema)
                ? schema.Inputs.Where(port => port.Required)
                : inputPortsByNode[node.NodeId].Values.Where(port => port.Required);

            foreach (var input in requiredInputs)
            {
                if (!incomingByInput.ContainsKey((node.NodeId, input.PortId)))
                    AddError(issues, "MissingRequiredInput", $"Required input '{input.PortId}' is not wired.", node.NodeId, input.PortId);
            }
        }

        AddCycleIssueIfNeeded(adjacency, issues);

        return new GpuShaderGraphValidationResult(
            Succeeded: issues.All(issue => !string.Equals(issue.Severity, ErrorSeverity, StringComparison.Ordinal)),
            Issues: issues);
    }

    private static Dictionary<string, GpuShaderGraphPort> BuildPortMap(
        string nodeId,
        IReadOnlyList<GpuShaderGraphPort>? ports,
        string direction,
        List<GpuShaderGraphValidationIssue> issues)
    {
        var result = new Dictionary<string, GpuShaderGraphPort>(StringComparer.Ordinal);
        foreach (var port in ports ?? Array.Empty<GpuShaderGraphPort>())
        {
            if (port is null)
            {
                AddError(issues, "NullPort", $"{direction} ports must not contain null entries.", nodeId);
                continue;
            }

            if (string.IsNullOrWhiteSpace(port.PortId))
            {
                AddError(issues, "PortIdRequired", $"{direction} port id must not be null or whitespace.", nodeId);
                continue;
            }

            if (string.IsNullOrWhiteSpace(port.KindHint))
                AddError(issues, "PortKindRequired", $"{direction} port '{port.PortId}' KindHint must not be null or whitespace.", nodeId, port.PortId);

            if (!result.TryAdd(port.PortId, port))
                AddError(issues, "DuplicatePortId", $"Duplicate {direction} port id '{port.PortId}'.", nodeId, port.PortId);
        }

        return result;
    }

    private static void ValidateSchemaPorts(
        GpuShaderGraphNode node,
        GpuShaderNodeSchema schema,
        IReadOnlyDictionary<string, GpuShaderGraphPort> inputMap,
        IReadOnlyDictionary<string, GpuShaderGraphPort> outputMap,
        List<GpuShaderGraphValidationIssue> issues)
    {
        foreach (var input in schema.Inputs)
        {
            if (!inputMap.ContainsKey(input.PortId))
                AddError(issues, "MissingSchemaInput", $"Node type '{schema.TypeId}' requires input port '{input.PortId}'.", node.NodeId, input.PortId);
        }

        foreach (var output in schema.Outputs)
        {
            if (!outputMap.ContainsKey(output.PortId))
                AddError(issues, "MissingSchemaOutput", $"Node type '{schema.TypeId}' requires output port '{output.PortId}'.", node.NodeId, output.PortId);
        }
    }

    private static void ValidateParameters(
        GpuShaderGraphNode node,
        List<GpuShaderGraphValidationIssue> issues)
    {
        var parameters = node.Parameters ?? Array.Empty<GpuShaderGraphParameter>();
        var byKey = new Dictionary<string, GpuShaderGraphParameter>(StringComparer.Ordinal);
        foreach (var parameter in parameters)
        {
            if (parameter is null)
            {
                AddError(issues, "NullParameter", "Parameters must not contain null entries.", node.NodeId);
                continue;
            }

            if (string.IsNullOrWhiteSpace(parameter.Key))
            {
                AddError(issues, "ParameterKeyRequired", "Parameter key must not be null or whitespace.", node.NodeId);
                continue;
            }

            if (string.IsNullOrWhiteSpace(parameter.KindHint))
                AddError(issues, "ParameterKindRequired", $"Parameter '{parameter.Key}' KindHint must not be null or whitespace.", node.NodeId);

            if (!byKey.TryAdd(parameter.Key, parameter))
                AddError(issues, "DuplicateParameterKey", $"Duplicate parameter key '{parameter.Key}'.", node.NodeId);
        }

        switch (node.TypeId)
        {
            case "shader.compute":
                RequireParameterValue(node.NodeId, byKey, "shaderId", issues);
                RequireParameterValue(node.NodeId, byKey, "shaderPath", issues);
                break;

            case "buffer.storage":
                RequireUIntParameter(node.NodeId, byKey, "set", minValue: 0, issues);
                RequireUIntParameter(node.NodeId, byKey, "binding", minValue: 0, issues);
                ValidateOptionalReadback(node.NodeId, byKey, issues);
                break;

            case "dispatch.groups":
                RequireUIntParameter(node.NodeId, byKey, "x", minValue: 1, issues);
                RequireUIntParameter(node.NodeId, byKey, "y", minValue: 1, issues);
                RequireUIntParameter(node.NodeId, byKey, "z", minValue: 1, issues);
                break;
        }
    }

    private static void RequireParameterValue(
        string nodeId,
        IReadOnlyDictionary<string, GpuShaderGraphParameter> parameters,
        string key,
        List<GpuShaderGraphValidationIssue> issues)
    {
        if (!parameters.TryGetValue(key, out var parameter) || string.IsNullOrWhiteSpace(parameter.Value))
            AddError(issues, "RequiredParameterMissing", $"Required parameter '{key}' must not be empty.", nodeId);
    }

    private static void RequireUIntParameter(
        string nodeId,
        IReadOnlyDictionary<string, GpuShaderGraphParameter> parameters,
        string key,
        uint minValue,
        List<GpuShaderGraphValidationIssue> issues)
    {
        if (!parameters.TryGetValue(key, out var parameter) || string.IsNullOrWhiteSpace(parameter.Value))
        {
            AddError(issues, "RequiredParameterMissing", $"Required parameter '{key}' must not be empty.", nodeId);
            return;
        }

        if (!uint.TryParse(parameter.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < minValue)
            AddError(issues, "InvalidUIntParameter", $"Parameter '{key}' must be an unsigned integer >= {minValue}.", nodeId);
    }

    private static void ValidateOptionalReadback(
        string nodeId,
        IReadOnlyDictionary<string, GpuShaderGraphParameter> parameters,
        List<GpuShaderGraphValidationIssue> issues)
    {
        if (!parameters.TryGetValue("readbackBytes", out var readbackBytes) || string.IsNullOrWhiteSpace(readbackBytes.Value))
            return;

        if (!uint.TryParse(readbackBytes.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed == 0)
            AddError(issues, "InvalidUIntParameter", "Parameter 'readbackBytes' must be an unsigned integer >= 1 when specified.", nodeId);
        if (!parameters.TryGetValue("readbackLabel", out var readbackLabel) || string.IsNullOrWhiteSpace(readbackLabel.Value))
            AddError(issues, "RequiredParameterMissing", "Parameter 'readbackLabel' must not be empty when readbackBytes is specified.", nodeId);
    }

    private static bool KindMatches(string portKind, string wireKind)
    {
        if (string.IsNullOrWhiteSpace(portKind) || string.IsNullOrWhiteSpace(wireKind))
            return false;

        if (string.Equals(portKind, wireKind, StringComparison.Ordinal))
            return true;

        return IsArrayKind(portKind)
            && string.Equals(portKind[..^2], wireKind, StringComparison.Ordinal);
    }

    private static bool IsArrayKind(string? kind)
        => kind is not null && kind.EndsWith("[]", StringComparison.Ordinal);

    private static Dictionary<string, List<string>> BuildAdjacency(
        IEnumerable<GpuShaderGraphNode> nodes,
        IEnumerable<GpuShaderGraphWire> wires)
    {
        var adjacency = nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.NodeId))
            .ToDictionary(node => node.NodeId, _ => new List<string>(), StringComparer.Ordinal);

        foreach (var wire in wires)
        {
            if (adjacency.ContainsKey(wire.FromNodeId) && adjacency.ContainsKey(wire.ToNodeId))
                adjacency[wire.FromNodeId].Add(wire.ToNodeId);
        }

        return adjacency;
    }

    private static bool ContainsCycle(IReadOnlyDictionary<string, List<string>> adjacency)
    {
        var marks = new Dictionary<string, int>(StringComparer.Ordinal);
        return adjacency.Keys.Any(nodeId => HasCycle(nodeId, adjacency, marks));
    }

    private static void AddCycleIssueIfNeeded(
        IReadOnlyDictionary<string, List<string>> adjacency,
        List<GpuShaderGraphValidationIssue> issues)
    {
        var marks = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var nodeId in adjacency.Keys)
        {
            if (HasCycle(nodeId, adjacency, marks))
            {
                AddError(issues, "CycleDetected", "Shader graph contains a cycle.", nodeId);
                return;
            }
        }
    }

    private static bool HasCycle(
        string nodeId,
        IReadOnlyDictionary<string, List<string>> adjacency,
        Dictionary<string, int> marks)
    {
        if (marks.TryGetValue(nodeId, out var mark))
            return mark == 1;

        marks[nodeId] = 1;
        foreach (var next in adjacency[nodeId])
        {
            if (HasCycle(next, adjacency, marks))
                return true;
        }

        marks[nodeId] = 2;
        return false;
    }

    private static void AddError(
        List<GpuShaderGraphValidationIssue> issues,
        string code,
        string message,
        string? nodeId = null,
        string? portId = null)
        => issues.Add(new GpuShaderGraphValidationIssue(ErrorSeverity, code, message, nodeId, portId));

    private static void AddWarning(
        List<GpuShaderGraphValidationIssue> issues,
        string code,
        string message,
        string? nodeId = null,
        string? portId = null)
        => issues.Add(new GpuShaderGraphValidationIssue(WarningSeverity, code, message, nodeId, portId));
}
