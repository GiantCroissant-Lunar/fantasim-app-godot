using System.Collections.Generic;

namespace FantaSim.App.World;

/// <summary>
/// Describes one renderable world layer: its identity, display label, geometric kind,
/// render order, and optional resolution-band / importance metadata.
/// </summary>
/// <param name="LayerId">Globally unique layer identifier (e.g. "geosphere.plates").</param>
/// <param name="Label">Human-readable display name.</param>
/// <param name="Kind">Geometric kind: "fill", "line", "point", "annotation", etc.</param>
/// <param name="RenderOrder">Z / draw order: lower values render first.</param>
/// <param name="Importance">Importance score for density budgeting (higher = more important). Default 0.</param>
/// <param name="MinResolution">Minimum R-axis resolution at which this layer is relevant. Default 0 (no lower bound).</param>
/// <param name="MaxResolution">Maximum R-axis resolution at which this layer is relevant. Default <see cref="int.MaxValue"/> (no upper bound).</param>
/// <param name="VisibleByDefault">Whether the layer starts visible when no profile is active. Default true.</param>
public sealed record WorldLayerDescriptor(
    string LayerId,
    string Label,
    string Kind,
    int RenderOrder,
    int Importance = 0,
    int MinResolution = 0,
    int MaxResolution = int.MaxValue,
    bool VisibleByDefault = true);

/// <summary>
/// Mutable runtime state for a single layer: visibility toggle and opacity.
/// </summary>
/// <param name="LayerId">The layer this state belongs to.</param>
/// <param name="Visible">Whether the layer is currently rendered.</param>
/// <param name="Opacity">Alpha multiplier in the range [0.0, 1.0]. Default 1.0.</param>
public sealed record WorldLayerState(
    string LayerId,
    bool Visible,
    double Opacity = 1.0);

/// <summary>
/// Snapshot of a layer combining its static descriptor with current runtime state.
/// Returned by queries so consumers see both metadata and live visibility in one record.
/// </summary>
/// <param name="Descriptor">Static layer metadata.</param>
/// <param name="Visible">Current visibility.</param>
/// <param name="Opacity">Current opacity.</param>
public sealed record WorldLayerInfo(
    WorldLayerDescriptor Descriptor,
    bool Visible,
    double Opacity);

/// <summary>
/// A named preset bundle of layer states. Applying a profile sets every layer
/// in <see cref="Layers"/> to the state declared by that profile.
/// </summary>
/// <param name="ProfileId">Globally unique profile identifier.</param>
/// <param name="Label">Human-readable display name.</param>
/// <param name="Layers">Layer states this profile prescribes. Layers not listed keep their current state.</param>
public sealed record WorldPresentationProfile(
    string ProfileId,
    string Label,
    IReadOnlyList<WorldLayerState> Layers);
