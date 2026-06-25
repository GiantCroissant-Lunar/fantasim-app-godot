using System;
using System.Collections.Generic;

namespace FantaSim.App.NodeGraph;

/// <summary>High-level status of a graph run. Provider-neutral: the same states apply to
/// iii pipelines, World generation recipes, or any future graph executor consumer.</summary>
public enum GraphRunStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled,
}

/// <summary>Per-node status during a graph run.</summary>
public enum GraphNodeRuntimeStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled,
}

/// <summary>Runtime summary for an entire graph run.</summary>
public sealed record GraphRunState(
    string RunId,
    GraphRunStatus Status,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? EndedAt = null,
    string? ErrorMessage = null);

/// <summary>Runtime summary for a single node during a graph run. JSON summaries are intentionally
/// strings so the UI can render compact previews without owning the node payload schema.</summary>
public sealed record GraphNodeRuntimeState(
    string NodeId,
    GraphNodeRuntimeStatus Status,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? EndedAt = null,
    string? InputsJson = null,
    string? OutputsJson = null,
    string? LogsJson = null,
    string? ArtifactsJson = null,
    double Progress = 0);

/// <summary>Optional extension for sources that can expose live runtime state for the graph UI.
/// Implementations are typically backed by the current <see cref="GraphExecutor"/> run or by a
/// domain runner that mirrors the same states.</summary>
public interface IGraphRuntimeStateSource
{
    GraphRunState RunState { get; }
    IReadOnlyDictionary<string, GraphNodeRuntimeState> NodeStates { get; }
    event Action? RuntimeStateChanged;
}
