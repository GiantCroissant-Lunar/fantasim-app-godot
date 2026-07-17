using System.Collections.Generic;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Dto;

namespace FantaSim.App.World;

/// <summary>Derived render-surface subdivision mode for the planet presentation mesh.</summary>
public enum SurfaceSubdivisionMode
{
    Fixed = 0,
    Adaptive = 1,
}

/// <summary>Contract-side projection product used to turn one world layer into render geometry.</summary>
public enum PlanetLayerProjectionKind
{
    GlobeSurface = 0,
    BoundarySection = 1,
}

/// <summary>
/// Declares how a world layer's truth units are projected into render geometry. This is presentation
/// metadata only: it does not create cells, change plate ownership, or replace the truth grid.
/// </summary>
public sealed record PlanetLayerProjectionProfile(
    string LayerId,
    PlanetLayerProjectionKind ProjectionKind,
    string SourceGrid,
    string SourceUnit,
    string DisplacementUnit,
    double BaseRadius,
    double MetresToUnitRadius,
    double HeightExponent,
    SurfaceSubdivisionMode SurfaceSubdivision,
    int AdaptiveSubdivisionMaxDepth,
    /// <summary>
    /// Unit-sphere radius displacement delta that decides whether an edge is split by adaptive
    /// subdivision. Coupled to the lens parameters (height exponent, etc.) that map metres to unit radius.
    /// </summary>
    double AdaptiveSubdivisionEdgeHeightDelta,
    double AdaptiveSubdivisionFeatureWeightDelta,
    bool PreservesCellProvenance,
    double PlanetRadiusMetres = 6_371_000.0,
    /// <summary>
    /// Silhouette budget (north-star spec §1): maximum absolute radial displacement in unit-radius
    /// units, applied as a pure clamp on the FINALIZED displacement. Default +inf preserves the
    /// legacy unclamped behaviour for callers that do not declare a budget.
    /// </summary>
    double MaxDisplacementUnitRadius = double.PositiveInfinity,
    /// <summary>
    /// The relief (metres) the projection expects at the top of its working range: envelope plus
    /// the strongest fabric belt. The resolver FITS the declared metre-to-unit-radius scale so this
    /// relief maps to at most <see cref="MaxDisplacementUnitRadius"/> — the clamp then only guards
    /// outliers instead of saturating the whole field.
    /// </summary>
    double ReferenceMaxReliefMetres = 18_000.0)
{
    public const string CrustLayerId = "geosphere.crust";
    public const string MantleLayerId = "geosphere.mantle";
    public const string UnifyCellGeodesicSourceGrid = "UnifyCell.GeodesicSphereTessellation";
    public const string PhysicalMetresUnit = "physical-metres";
    public const string UnitSphereDisplacementUnit = "unit-sphere-displacement";
    public const double EarthLikePlanetRadiusMetres = 6_371_000.0;

    /// <summary>North-star spec §1: the stylized silhouette allowance (0.5% of base radius).</summary>
    public const double DefaultSilhouetteBudgetUnitRadius = 0.005;

    /// <summary>Default <see cref="ReferenceMaxReliefMetres"/>: ±500 m envelope + strongest fabric belt.</summary>
    public const double DefaultReferenceMaxReliefMetres = 18_000.0;

    /// <summary>Non-amplified physical metre scale for the declared planet radius.</summary>
    public double TrueScaleMetresToUnitRadius => 1.0 / PlanetRadiusMetres;

    /// <summary>Linear relief amplification relative to true scale for this projection.</summary>
    public double ReliefAmplification => MetresToUnitRadius / TrueScaleMetresToUnitRadius;

    public static PlanetLayerProjectionProfile Crust(
        double metresToUnitRadius,
        SurfaceSubdivisionMode surfaceSubdivision,
        int adaptiveSubdivisionMaxDepth,
        double adaptiveSubdivisionEdgeHeightDelta,
        double adaptiveSubdivisionFeatureWeightDelta = 0.25,
        double baseRadius = 1.0,
        double planetRadiusMetres = EarthLikePlanetRadiusMetres,
        double maxDisplacementUnitRadius = DefaultSilhouetteBudgetUnitRadius,
        double referenceMaxReliefMetres = DefaultReferenceMaxReliefMetres)
    {
        if (planetRadiusMetres <= 0.0 || double.IsNaN(planetRadiusMetres) || double.IsInfinity(planetRadiusMetres))
            throw new ArgumentOutOfRangeException(nameof(planetRadiusMetres), planetRadiusMetres, "Planet radius must be positive and finite.");
        if (referenceMaxReliefMetres <= 0.0 || double.IsNaN(referenceMaxReliefMetres) || double.IsInfinity(referenceMaxReliefMetres))
            throw new ArgumentOutOfRangeException(nameof(referenceMaxReliefMetres), referenceMaxReliefMetres, "Reference max relief must be positive and finite.");

        return new(
            LayerId: CrustLayerId,
            ProjectionKind: PlanetLayerProjectionKind.GlobeSurface,
            SourceGrid: UnifyCellGeodesicSourceGrid,
            SourceUnit: PhysicalMetresUnit,
            DisplacementUnit: UnitSphereDisplacementUnit,
            BaseRadius: baseRadius,
            MetresToUnitRadius: metresToUnitRadius,
            HeightExponent: 1.0,
            SurfaceSubdivision: surfaceSubdivision,
            AdaptiveSubdivisionMaxDepth: adaptiveSubdivisionMaxDepth,
            AdaptiveSubdivisionEdgeHeightDelta: adaptiveSubdivisionEdgeHeightDelta,
            AdaptiveSubdivisionFeatureWeightDelta: adaptiveSubdivisionFeatureWeightDelta,
            PreservesCellProvenance: true,
            PlanetRadiusMetres: planetRadiusMetres,
            MaxDisplacementUnitRadius: maxDisplacementUnitRadius,
            ReferenceMaxReliefMetres: referenceMaxReliefMetres);
    }
}

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
    private IReadOnlyList<PlateBoundaryArc>? _legacyBoundaryArcs;
    private IReadOnlyList<double>? _legacyCellCrustThickness;
    private IReadOnlyList<double>? _legacyCellElevations;
    private IReadOnlyList<CellCrustFeature>? _legacyCellFeatures;
    private IReadOnlyDictionary<int, double>? _legacyContinentalFractionByCell;

    /// <summary>
    /// Plain contract-side globe geometry to be bound by a resident Godot presentation builder.
    /// The collectible world bundle owns how this data is generated; the resident seam owns only
    /// translating the DTO into nodes/meshes under the Stage-owned Environment scene.
    /// </summary>
    public WorldGlobeSnapshot? GlobeSnapshot { get; init; }

    /// <summary>
    /// Canonical plate-owned crust volume state shared by assembled and exploded/cutaway
    /// presentations. Null in regimes with no solid crust.
    /// </summary>
    public CrustVolumeState? CrustVolume { get; init; }

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
    public IReadOnlyList<PlateBoundaryArc>? BoundaryArcs
    {
        get => CrustVolume?.BoundaryArcs ?? _legacyBoundaryArcs;
        init => _legacyBoundaryArcs = value;
    }

    /// <summary>
    /// Boundary-normal section panels for representative convergent/divergent/transform arcs at the
    /// presentation reference tick. These are not the radial cutaway wedge; they are focused views of
    /// plate-boundary mechanics derived from the same boundary profile data that shapes terrain.
    /// </summary>
    public IReadOnlyList<BoundarySectionDocument>? BoundarySections { get; init; }

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
    /// Render projection profiles for focused world layers. These profiles name scale/provenance
    /// explicitly so a host can render the crust with a different visual lens and subdivision policy
    /// while preserving the source cell grid as truth.
    /// </summary>
    public IReadOnlyList<PlanetLayerProjectionProfile> LayerProjectionProfiles { get; init; } =
        Array.Empty<PlanetLayerProjectionProfile>();

    /// <summary>
    /// Per-cell crust THICKNESS in metres at <see cref="ReferenceTick"/>, indexed by cell id (length =
    /// CellCount). Null when crust-thickness products have not flowed for this snapshot (the cutaway
    /// falls back to the declared default). Drives the cutaway's outer CRUST stratum band — the one
    /// honest band on the cut faces (§5c, W3a). Mirrors the <see cref="CellElevations"/> precedent:
    /// plumbed from the world's <c>crust-thickness-m</c> field (<see cref="FantaSim.App.World.Composition.GeosphereFieldCatalog.CrustThickness"/>)
    /// onto the document so the cut-face renderer reads truth, not a buried constant.
    /// </summary>
    public IReadOnlyList<double>? CellCrustThickness
    {
        get => CrustVolume?.CrustThicknessMetresByCell ?? _legacyCellCrustThickness;
        init => _legacyCellCrustThickness = value;
    }

    /// <summary>
    /// Declared exaggeration (scale rule S1) for the cutaway STRATUM bands on the cut faces —
    /// SEPARATE from the world view's sqrt height lens (<see cref="VerticalExaggeration"/> and the
    /// non-linear lens in §5c-i). The cut faces use their own labeled exaggeration so the two do not
    /// entangle. The S2 indicator names this (<c>CutawayStratumProfile.FormatExaggerationIndicator</c>)
    /// when the cutaway is active. Default 1.0 (honest scale; look-dev tunes it).
    /// </summary>
    public double CutawayExaggeration { get; init; } = 1.0;

    /// <summary>
    /// Per-cell crust elevation in metres at <see cref="ReferenceTick"/>, indexed by cell id (length =
    /// <see cref="WorldGlobeSnapshot"/>'s CellCount). Null when crust products have not flowed for
    /// this snapshot (the host falls back to flat-zero). Drives BOTH the mesh displacement (A1) and
    /// the hypsometric vertex-color tint (A2) off the SAME field, so color and relief stay coherent.
    /// </summary>
    public IReadOnlyList<double>? CellElevations
    {
        get => CrustVolume?.OuterElevationsMetresByCell ?? _legacyCellElevations;
        init => _legacyCellElevations = value;
    }

    /// <summary>
    /// Per-cell typed crust feature at <see cref="ReferenceTick"/>, indexed by cell id (length =
    /// CellCount; cells with no feature carry <see cref="CellCrustFeature"/> default = Kind 0 / 0.0).
    /// Null when features have not been derived; the host renders the hypsometric tint without accents.
    /// The typed boundary POLYLINES already carry boundary-type color; these surface-level accents
    /// complement them (volcanic vent glow, trench darkening, ridge brightening), not duplicate them.
    /// </summary>
    public IReadOnlyList<CellCrustFeature>? CellFeatures
    {
        get => CrustVolume?.FeaturesByCell ?? _legacyCellFeatures;
        init => _legacyCellFeatures = value;
    }

    /// <summary>
    /// The resolved continental-plate id set the crust pipeline used to designate land vs ocean
    /// (M0, spec D2). Single source of truth: populated from
    /// <see cref="FantaSim.App.World.Crust.WorldCrustRunSpec.ContinentalPlateIdsForPresentation"/>
    /// so the Continents view and the crust materialization pipeline never disagree. Default empty
    /// when no crust run spec flowed for this snapshot.
    /// </summary>
    [Obsolete("Patch-based seeding makes land/ocean a per-cell fraction; prefer ContinentalFractionByCell. Kept for A3 compat.")]
    public IReadOnlySet<int> ContinentalPlateIds { get; init; } = new HashSet<int>();

    /// <summary>
    /// Per-cell continental fraction in [0,1] at <see cref="ReferenceTick"/>, sampled in the moving
    /// plate frame (P2A). Values are indexed by cell id (length = CellCount); ≥ 0.5 renders as land
    /// and &lt; 0.5 as ocean in the Continents view. Null when crust products have not flowed.
    /// </summary>
    public IReadOnlyDictionary<int, double>? ContinentalFractionByCell
    {
        get => CrustVolume?.ContinentalFractionByCell ?? _legacyContinentalFractionByCell;
        init => _legacyContinentalFractionByCell = value;
    }

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
    /// on-screen scale indicator (rule S2) when the hypsometric terrain view is active. Default 3e-5
    /// (matches <c>WorldGenerationRenderOptions.DefaultVerticalExaggeration</c>).
    /// </summary>
    public double VerticalExaggeration { get; init; } = 0.00003;

    /// <summary>
    /// Render-facing surface subdivision mode. This is derived presentation geometry, not simulation
    /// truth; the world bundle resolves it from authored render options and the presentation binder
    /// decides whether to feed adaptive caps to the mesh seam.
    /// </summary>
    public SurfaceSubdivisionMode SurfaceSubdivision { get; init; } = SurfaceSubdivisionMode.Fixed;

    /// <summary>Maximum adaptive subdivision depth for derived globe caps. Current implementation supports depth 1.</summary>
    public int AdaptiveSubdivisionMaxDepth { get; init; } = 1;

    /// <summary>
    /// Height-delta threshold, in post-exaggeration unit-sphere displacement, that decides whether an
    /// edge is split by adaptive subdivision. Coupled to the lens parameters (height exponent, etc.)
    /// that map metres to unit radius.
    /// </summary>
    public double AdaptiveSubdivisionEdgeHeightDelta { get; init; } = 0.02;

    /// <summary>
    /// Feature-weight threshold that refines adaptive render geometry around typed crust features even
    /// when the current height envelope is flat.
    /// </summary>
    public double AdaptiveSubdivisionFeatureWeightDelta { get; init; } = 0.25;
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
