using System.Collections.Generic;

namespace FantaSim.App.NodeGraph;

/// <summary>Generic manifest describing an iii-backed external tool and its node-graph capabilities.</summary>
public sealed record ExternalToolManifest(
    string ToolId,
    string ToolVersion,
    string Provider,
    string? License,
    string? SourceUrl,
    IReadOnlyList<ExternalToolFunctionManifest> Functions);

/// <summary>One function exposed by an external tool in the node graph.</summary>
public sealed record ExternalToolFunctionManifest(
    string FunctionId,
    string Label,
    string Category,
    string Summary,
    bool IsSideEffect,
    bool IsExpensive,
    IReadOnlyList<ExternalToolPortManifest> Inputs,
    IReadOnlyList<ExternalToolPortManifest> Outputs,
    IReadOnlyList<ExternalToolParameterManifest>? Parameters = null,
    ExternalToolStateManifest? State = null);

/// <summary>One input or output port on an external-tool function.</summary>
public sealed record ExternalToolPortManifest(
    string PortId,
    string Label,
    string Kind,
    bool Required);

/// <summary>One editable parameter on an external-tool function.</summary>
public sealed record ExternalToolParameterManifest(
    string Key,
    string Label,
    string Kind,
    string DefaultValue,
    string? Unit = null,
    string? Description = null);

/// <summary>Runtime state metadata exposed by an external-tool function.</summary>
public sealed record ExternalToolStateManifest(
    bool Progress,
    bool Logs,
    bool Artifacts,
    bool Warnings);
