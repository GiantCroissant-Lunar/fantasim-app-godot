using System.Collections.Generic;

namespace FantaSim.App.NodeGraph;

/// <summary>Generic manifest describing a provider-backed tool and its node-graph capabilities.
/// The optional <see cref="ProviderMetadata"/> keeps execution origin as metadata rather than
/// a separate node or data identity.</summary>
public sealed record ExternalToolManifest(
    string ToolId,
    string ToolVersion,
    string Provider,
    string? License,
    string? SourceUrl,
    IReadOnlyList<ExternalToolFunctionManifest> Functions,
    FunctionProviderMetadata? ProviderMetadata = null);

/// <summary>One function exposed by an external tool in the node graph.
/// The optional <see cref="ExecutionTraits"/> carry scheduling and runtime policy.</summary>
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
    ExternalToolStateManifest? State = null,
    FunctionExecutionTraits? ExecutionTraits = null);

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
