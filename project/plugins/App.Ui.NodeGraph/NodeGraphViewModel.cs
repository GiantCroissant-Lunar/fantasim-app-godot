using System.Collections.Generic;

namespace FantaSim.App.Ui.NodeGraph;

/// <summary>Shared MVVM records for the node-graph view. The resident BoomHud binder + enhancer
/// (App.Ui.Seam) reflect over these by property name. Reused by every node-graph view so the
/// rendering contract is not duplicated per domain.</summary>

public sealed record PortItem(string PortId, string Label, string KindHint, bool Required);

public sealed record NodeItem(
    string NodeId,
    string TypeId,
    int InputCount,
    int OutputCount,
    string Category,
    string TypeKey,
    string Summary,
    string Detail,
    bool IsSideEffect,
    bool IsExpensive,
    IReadOnlyList<PortItem> Inputs,
    IReadOnlyList<PortItem> Outputs,
    IReadOnlyList<string>? ParameterLines = null,
    IReadOnlyList<ParameterItem>? Parameters = null,
    int PreviewWidth = 0,
    int PreviewHeight = 0,
    byte[]? PreviewRgba = null);

public sealed record ParameterItem(string Key, string Label, string Value, string KindHint);

public sealed record WireItem(
    string FromNodeId,
    int FromSlot,
    string ToNodeId,
    int ToSlot,
    string FromPortId,
    string ToPortId,
    string KindHint);

public sealed record AnnotationBoundsItem(float X, float Y, float Width, float Height);

public sealed record AnnotationItem(
    string AnnotationId,
    string Kind,
    string Label,
    AnnotationBoundsItem Bounds,
    IReadOnlyList<string> NodeIds,
    string? Text = null,
    string? Color = null);
