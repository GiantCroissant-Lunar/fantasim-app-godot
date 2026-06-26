using System.Collections.Generic;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Dto;

namespace FantaSim.App.World;

/// <summary>
/// Data-first description of the planet surface the Stage-owned Environment scene can bind.
/// This contract carries product/layer provenance only; engine-specific nodes, materials, and
/// meshes are chosen by presentation builders in reloadable bundles.
/// </summary>
public sealed record PlanetPresentationDocument(
    string PlanetId,
    string SourceWorldId,
    long ReferenceTick,
    int Revision,
    IReadOnlyList<PlanetPresentationLayer> Layers,
    IReadOnlyList<RenderEntityDto> RenderEntities)
{
    /// <summary>
    /// Plain contract-side globe geometry to be bound by a resident Godot presentation builder.
    /// The collectible world bundle owns how this data is generated; the resident seam owns only
    /// translating the DTO into nodes/meshes under the Stage-owned Environment scene.
    /// </summary>
    public WorldGlobeSnapshot? GlobeSnapshot { get; init; }

    /// <summary>
    /// Canonical tick represented by <see cref="GlobeSnapshot"/>'s base geometry. Presentation
    /// builders apply plate motion relative to this tick so the onset snapshot does not jump when shown.
    /// </summary>
    public long GlobeReferenceTick { get; init; }

    /// <summary>Current geosphere regime schedule authored by the world bundle.</summary>
    public SphereRegimeSchedule? GeosphereSchedule { get; init; }

    /// <summary>Current atmosphere regime schedule authored by the world bundle.</summary>
    public SphereRegimeSchedule? AtmosphereSchedule { get; init; }

    /// <summary>Timeline upper bound for resident playback surfaces.</summary>
    public long MaxTick { get; init; }

    /// <summary>
    /// Contract-side generation graph family that explains the creation graph behind regimes and
    /// layers. Presentation hosts can inspect and bind this DTO without referencing the world plugin
    /// implementation assembly.
    /// </summary>
    public WorldGenerationGraphFamilyDocument? GenerationGraphFamily { get; init; }
}

/// <summary>
/// One planet layer projected from a real world-generation product address.
/// </summary>
/// <param name="GenerationGraphId">
/// Optional: the family graph id that produced this layer's product, resolved from the
/// family's <see cref="WorldLayerGraphBinding"/>. Null when no layer binding covers the
/// product's (sphere, layer) pair. Carries the layer/regime -> graph link end-to-end so
/// presentation surfaces can name the creation approach behind each layer.
/// </param>
public sealed record PlanetPresentationLayer(
    string LayerId,
    string RegimeId,
    string Variant,
    string Branch,
    string ProductDomain,
    string ProductName,
    long ProductTick,
    string ProductAddress,
    string? GenerationGraphId = null,
    string? SourceId = null,
    string? SourceKind = null,
    string? SourceLabel = null,
    string? SourceAvailability = null,
    string? RendererContract = null);

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
