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

    /// <summary>
    /// Typed plate-boundary arcs (smooth great-circle unit-sphere points) derived from plate-topology
    /// truth at <see cref="GlobeReferenceTick"/>. Null when the world bundle produced no arcs
    /// (e.g. pre-onset). Hosts render them as polylines coloured by <see cref="PlateBoundaryKind"/>;
    /// the arcs are rebuilt when the document is rebound, so boundaries appear/retire on regime change.
    /// </summary>
    /// <remarks>
    /// Per-tick type reclassification across the playhead is a future increment (it needs a retained
    /// reconstructor plus a tick-parametric service query); the arcs here are authoritative for the
    /// reference tick and the cell-cap colouring is unaffected.
    /// </remarks>
    public IReadOnlyList<PlateBoundaryArc>? BoundaryArcs { get; init; }

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

    /// <summary>
    /// Per-cell crust elevation in metres at <see cref="ReferenceTick"/>, indexed by cell id (length =
    /// <see cref="WorldGlobeSnapshot"/>'s CellCount). Null when crust products have not flowed for
    /// this snapshot (the host falls back to flat-zero). Drives BOTH the mesh displacement (A1) and
    /// the hypsometric vertex-color tint (A2) off the SAME field, so color and relief stay coherent.
    /// </summary>
    public IReadOnlyList<double>? CellElevations { get; init; }

    /// <summary>
    /// Per-cell typed crust feature at <see cref="ReferenceTick"/>, indexed by cell id (length =
    /// CellCount; cells with no feature carry <see cref="CellCrustFeature"/> default = Kind 0 / 0.0).
    /// Null when features have not been derived; the host renders the hypsometric tint without accents.
    /// The typed boundary POLYLINES already carry boundary-type color; these surface-level accents
    /// complement them (volcanic vent glow, trench darkening, ridge brightening), not duplicate them.
    /// </summary>
    public IReadOnlyList<CellCrustFeature>? CellFeatures { get; init; }

    /// <summary>
    /// Crust snapshot ticks that are currently available (generated) for the mobile-plate regime.
    /// Empty when no crust products have been generated. Sub-project B consumes this set to render
    /// the snapshot cache strip and to know which ticks still need generation.
    /// </summary>
    public IReadOnlyList<CrustSnapshotTickState> CrustSnapshotTicks { get; init; } = Array.Empty<CrustSnapshotTickState>();

    /// <summary>
    /// Vertical exaggeration (scale rule S1): the factor mapping crust elevation (metres on the
    /// <c>CellElevationSystem</c> scale) to unit-globe radius displacement in the crust view. The host
    /// applies this when displacing plate caps instead of a buried constant, and surfaces it as the
    /// on-screen scale indicator (rule S2) when the hypsometric terrain view is active. Default 1e-5
    /// (matches <c>WorldGenerationRenderOptions.DefaultVerticalExaggeration</c>).
    /// </summary>
    public double VerticalExaggeration { get; init; } = 0.00001;
}

/// <summary>
/// One crust snapshot tick exposed on the presentation document. Sub-project B uses the
/// <see cref="Available"/> flag to render a cache strip (dark = pending, bright = ready).
/// </summary>
public sealed record CrustSnapshotTickState(long Tick, bool Available);

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
