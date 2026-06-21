using System.Collections.Generic;

namespace FantaSim.App.GpuShader;

/// <summary>
/// App-facing, ALC-safe description of a GPU shader graph. It is an authoring
/// document, not GPU execution state; Godot shader resources, buffers, and native RIDs
/// stay in the resident seam.
/// </summary>
public sealed record GpuShaderGraphView(
    string GraphId,
    string Label,
    string Description,
    IReadOnlyList<GpuShaderGraphNode> Nodes,
    IReadOnlyList<GpuShaderGraphWire> Wires);

/// <summary>One typed node in a GPU shader graph.</summary>
public sealed record GpuShaderGraphNode(
    string NodeId,
    string TypeId,
    string Label,
    string Category,
    bool IsSideEffect,
    bool IsExpensive,
    IReadOnlyList<GpuShaderGraphPort> Inputs,
    IReadOnlyList<GpuShaderGraphPort> Outputs,
    IReadOnlyList<GpuShaderGraphParameter>? Parameters = null);

/// <summary>One typed input or output port on a GPU shader graph node.</summary>
public sealed record GpuShaderGraphPort(
    string PortId,
    string Label,
    string KindHint,
    bool Required);

/// <summary>One wire between typed output and input ports.</summary>
public sealed record GpuShaderGraphWire(
    string FromNodeId,
    string FromPortId,
    string ToNodeId,
    string ToPortId,
    string KindHint);

/// <summary>One editable parameter on a shader graph node (string-encoded value).</summary>
public sealed record GpuShaderGraphParameter(
    string Key,
    string Label,
    string Value,
    string KindHint);

/// <summary>A node type available for shader graph authoring.</summary>
public sealed record GpuShaderNodeSchema(
    string TypeId,
    string Label,
    string Category,
    bool IsSideEffect,
    bool IsExpensive,
    IReadOnlyList<GpuShaderGraphPort> Inputs,
    IReadOnlyList<GpuShaderGraphPort> Outputs,
    string Summary,
    IReadOnlyList<GpuShaderGraphParameter>? Parameters = null);

/// <summary>
/// One authoring edit against the current GPU shader graph. Kind selects the operation;
/// unused fields stay null. Kinds: "add-node" (TypeId required, NodeId optional - service
/// assigns a unique id when omitted), "remove-node" (NodeId), "add-wire" / "remove-wire"
/// (FromNodeId+FromPortId+ToNodeId+ToPortId), "set-param" (NodeId + ParamKey +
/// ParamValue), "reset-default" (no fields), "clear" (no fields).
/// </summary>
public sealed record GpuShaderGraphEdit(
    string Kind,
    string? NodeId = null,
    string? TypeId = null,
    string? FromNodeId = null,
    string? FromPortId = null,
    string? ToNodeId = null,
    string? ToPortId = null,
    string? ParamKey = null,
    string? ParamValue = null);

/// <summary>Validation result for a shader graph document.</summary>
public sealed record GpuShaderGraphValidationResult(
    bool Succeeded,
    IReadOnlyList<GpuShaderGraphValidationIssue> Issues);

/// <summary>One shader graph validation issue. Severity values are "error" or "warning".</summary>
public sealed record GpuShaderGraphValidationIssue(
    string Severity,
    string Code,
    string Message,
    string? NodeId = null,
    string? PortId = null);
