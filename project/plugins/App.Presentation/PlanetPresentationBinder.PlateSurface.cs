using FantaSim.App.World;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Globe;
using FantaSim.App.World.Rendering;
using FantaSim.Cartography.Globe;
using Godot;
using Microsoft.Extensions.Logging;
using WorldService = FantaSim.App.World.IService;

namespace FantaSim.App.Presentation;

// Plate-surface build/bind + Continents membership. Split from PlanetPresentationBinder
// 2026-07-11 (vault/plans/2026-07-11-planet-presentation-binder-split-plan.md). D8b maps
// resolution rungs onto the AdaptiveSubdivisionOptions built in BindPlateSurface.
internal sealed partial class PlanetPresentationBinder
{
    private GlobePlateSurfaces? _plateSurfaces;

    // M-B cached build inputs captured at plate-surface bind time so UpdateExploded can rebuild
    // byte-identical TOP DTOs (same Continents/terrain colors) without recomputing the heavy surface
    // path. The solid THICKNESS exaggeration is decoupled from the surface relief exaggeration (D3):
    // it comes from _radialProfile.CrustThicknessExaggeration, not _lastExaggeration.
    private IReadOnlyList<PlateCap>? _lastCaps;
    private double _lastExaggeration;
    private IReadOnlyList<PlateSolidCentroid>? _lastCentroids;
    private GlobeViewMode _lastViewMode;
    private bool _lastIsTerrain;
    private IReadOnlyDictionary<int, RampColor[]>? _lastPerPlateVertexColors;
    private IReadOnlyList<RampColor>? _lastPerCellColor;
    private IReadOnlyList<float>? _lastPerCellEmission;
    private VertexTintJitter? _lastJitter;
    private PlateCapMeshColorMode _lastColorMode;
    private PlateCapMeshNormalMode _lastNormalMode;
    private IReadOnlyList<RampColor>? _lastContinentsCellColors;
    private byte[]? _lastContinentsFrontier;

    private void ApplySurfaceAppearance(GlobeViewMode surfaceViewMode)
    {
        if (!PlateSurfaceViewModeTransition.ShouldRebuild(_currentSurfaceViewMode, surfaceViewMode))
        {
            _currentSurfaceViewMode = surfaceViewMode;
            return;
        }

        _currentSurfaceViewMode = surfaceViewMode;
        RebuildPlateSurface();
    }

    private void RebuildPlateSurface()
    {
        if (_currentDocument is null || _currentDocument.GlobeSnapshot is null)
            return;
        if (_activeRoot is null || !GodotObject.IsInstanceValid(_activeRoot))
            return;

        var body = _activeRoot.GetNodeOrNull<Node3D>("PlanetBody");
        if (body is null)
            return;

        if (_plateSurfaceRoot is PlateSurfaceRenderer renderer && GodotObject.IsInstanceValid(renderer))
        {
            BindPlateSurface(renderer, _currentDocument, _currentSurfaceViewMode);
            if (_explodedActive)
                RebuildExplodedCrust();
            return;
        }

        if (_plateSurfaceRoot is not null && GodotObject.IsInstanceValid(_plateSurfaceRoot))
        {
            ReleasePlateSurfaceRenderer();
            body.RemoveChild(_plateSurfaceRoot);
            _plateSurfaceRoot.QueueFree();
        }
        _plateSurfaceRoot = null;
        _plateSurfaces = null;

        _plateSurfaceRoot = BuildPlateSurface(_currentDocument, _currentSurfaceViewMode);
        body.AddChild(_plateSurfaceRoot);
        if (_explodedActive)
            RebuildExplodedCrust();
    }

    // M0 light refresh (spec §3.2): swap ONLY the globe snapshot to the playhead's reassigned
    // membership and rebuild the plate caps in place. No document re-fetch, no crust
    // materialization — GetGlobeSnapshotAt rides the service's cached reconstructor (~ms at
    // freq 4, see MotionGateTests), so continents glide during scrub and Play. P3: also re-sample
    // the per-cell continental fraction at the seek tick from cached sampler state so the
    // Continents coloring drifts smoothly between 5 M-tick snapshots instead of stepping.
    private void RefreshContinentsMembership(long tick)
    {
        // PERMANENT diagnostic: this seam-critical light path was inert in the exported app for two
        // arcs because every early return is silent. The entry log + per-guard logs let a windowed
        // run distinguish "never called" (the viewMode gate / seek wiring) from "called but guard
        // fired" (document null / already bound / world service unresolved). Debug level so Play does
        // not spam at info.
        _log.LogDebug(
            "RefreshContinentsMembership(tick={Tick}) entered: hasDocument={HasDocument}, boundContinentsTick={BoundTick}.",
            tick, _currentDocument is not null, _boundContinentsTick);

        if (_currentDocument is null || _boundContinentsTick == tick)
        {
            _log.LogDebug(
                "RefreshContinentsMembership(tick={Tick}) early-return: document null or tick already bound (hasDocument={HasDocument}, boundContinentsTick={BoundTick}).",
                tick, _currentDocument is not null, _boundContinentsTick);
            return;
        }

        var world = _registry.TryGet<WorldService>();
        if (world is null)
        {
            _log.LogDebug(
                "RefreshContinentsMembership(tick={Tick}) early-return: WorldService not registered (cross-ALC type-identity split or bundle not yet loaded).",
                tick);
            return;
        }

        WorldGlobeSnapshot snapshot;
        IReadOnlyDictionary<int, double> fractions;
        try
        {
            snapshot = world.GetGlobeSnapshotAt(tick);
            fractions = world.GetContinentalFractionByCellAt(tick);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Continents membership refresh failed at t={Tick}: {Message}", tick, ex.Message);
            return;
        }

        _currentDocument = _currentDocument with
        {
            GlobeSnapshot = snapshot,
            GlobeReferenceTick = tick,
            ContinentalFractionByCell = fractions,
        };
        _boundContinentsTick = tick;
        RebuildPlateSurface();
        // In-place content mutation: the bound stamp no longer describes the scene, so the next
        // regime refresh must re-bind rather than dedupe against a stale identity.
        _boundSurfaceStamp = null;
        _log.LogDebug(
            "RefreshContinentsMembership(tick={Tick}) refreshed: snapshotCells={SnapshotCells}, fractionCells={FractionCells}.",
            tick, snapshot.CellCount, fractions.Count);
    }

    private PlateSurfaceRenderer BuildPlateSurface(PlanetPresentationDocument document, GlobeViewMode viewMode)
    {
        var renderer = new PlateSurfaceRenderer();
        BindPlateSurface(renderer, document, viewMode);
        return renderer;
    }

    private void BindPlateSurface(PlateSurfaceRenderer renderer, PlanetPresentationDocument document, GlobeViewMode viewMode)
    {
        var snapshot = document.GlobeSnapshot!;
        // P1 + W1: view mode selects cap appearance — World (composed product) and HypsometricTerrain
        // (crust diagnostic) both displace by elevation; PlateIdentity is flat. World uses a tuned
        // noise amplitude (sub-cell detail that buries the cell grid) + the WorldTerrainRamp + per-
        // vertex tint jitter; HypsometricTerrain uses the diagnostic crust palette.
        bool isTerrain = viewMode is GlobeViewMode.World or GlobeViewMode.HypsometricTerrain;
        bool isWorld = viewMode == GlobeViewMode.World;

        var relief = PlateSurfaceReliefFabric.ForView(viewMode);
        _plateSurfaces = new GlobePlateSurfaces(
            snapshot,
            noise: relief,
            detailSampler: PlateSurfaceMeshFactory.BuildTectonicDetailSampler(snapshot, document.CellFeatures, relief, viewMode, isTerrain));

        IReadOnlyList<double> elevations = isTerrain
            ? (document.CellElevations is { } cellElevations && cellElevations.Count == snapshot.CellCount
                ? cellElevations
                : new double[snapshot.CellCount])
            : new double[snapshot.CellCount];

        var projection = LayerProjectionProfileResolver.ResolveForView(
            document,
            viewMode,
            worldMetresToUnitRadius: WorldHeightScale,
            worldHeightExponent: WorldHeightExponent);
        bool useAdaptiveSurface = projection.UseAdaptiveSurface;
        var featureWeights = useAdaptiveSurface
            ? PlateSurfaceMeshFactory.BuildAdaptiveFeatureWeights(snapshot.CellCount, document.CellFeatures)
            : null;
        // Silhouette budget (north-star spec §1): planet views clamp the finalized radial
        // displacement to the profile's declared cap so the limb stays a circle. Diagnostic views
        // that do not declare a cap keep the +inf default (legacy unclamped behaviour).
        var maxDisp = projection.MaxDisplacementUnitRadius;
        var caps = useAdaptiveSurface
            ? _plateSurfaces.BuildAdaptiveSurfaces(
                elevations,
                exaggeration: projection.MetresToUnitRadius,
                options: new AdaptiveSubdivisionOptions(
                    MaxDepth: projection.AdaptiveSubdivisionMaxDepth,
                    EdgeHeightDeltaThreshold: projection.AdaptiveSubdivisionEdgeHeightDelta,
                    FeatureWeightDeltaThreshold: projection.AdaptiveSubdivisionFeatureWeightDelta),
                heightExponent: projection.HeightExponent,
                featureWeightsByCell: featureWeights,
                baseRadius: projection.BaseRadius,
                maxDisplacementUnitRadius: maxDisp)
            : _plateSurfaces.BuildSurfaces(
                elevations,
                exaggeration: projection.MetresToUnitRadius,
                heightExponent: projection.HeightExponent,
                baseRadius: projection.BaseRadius,
                maxDisplacementUnitRadius: maxDisp);

        var (perCellColor, perCellEmission) = isTerrain
            ? BuildCellAppearance(snapshot.CellCount, document, viewMode, isWorld ? snapshot.Cells : null)
            : (Array.Empty<RampColor>(), Array.Empty<float>());

        var colorMode = PlateSurfaceColorModePolicy.ForView(viewMode);
        var normalMode = PlateSurfaceNormalModePolicy.ForView(viewMode);
        // Per-vertex color envelope (world terrain): smooth per-cell ramp colours across cell AND
        // plate boundaries so terrain reads as Gouraud-shaded gradients instead of chunky per-cell
        // triangles. The crust diagnostic intentionally bypasses this smoothing and uses source-cell
        // facet colours so the dry crust stays readable from the front face, not only on the limb.
        var perPlateVertexColors = isTerrain && colorMode == PlateCapMeshColorMode.VertexEnvelope
            ? PlateSurfaceMeshFactory.BuildPerPlateVertexColors(_plateSurfaces!, perCellColor)
            : new Dictionary<int, RampColor[]>();

        var jitter = PlateSurfaceTintFabric.ForView(viewMode);
        var meshes = new List<PlateCapMeshDto>(caps.Count);

        // P2A Continents (spec §3.1/D4): color by per-cell continental fraction sampled in the
        // moving plate frame; coastline frontier tint is derived from the fraction contour itself,
        // not from plate membership, so the seam tracks land/ocean transitions as plates drift.
        var continentsCellColors = viewMode == GlobeViewMode.Continents
            ? PlateSurfaceMeshFactory.BuildContinentsCellColors(snapshot.CellCount, document.ContinentalFractionByCell)
            : Array.Empty<RampColor>();

        byte[]? continentsFrontier = viewMode == GlobeViewMode.Continents
            ? PlateSurfaceMeshFactory.BuildFractionContourFrontier(snapshot, document.ContinentalFractionByCell)
            : null;

        foreach (var cap in caps.OrderBy(c => c.PlateId))
        {
            var mesh = isTerrain
                ? PlateCapMeshBuilder.BuildTerrain(
                    cap,
                    perPlateVertexColors!,
                    perCellEmission,
                    jitter,
                    colorMode,
                    perCellColor,
                    normalMode)
                : viewMode == GlobeViewMode.Continents
                    ? PlateCapMeshBuilder.BuildContinents(
                        cap,
                        continentsCellColors,
                        continentsFrontier!)
                    : PlateCapMeshBuilder.BuildPlateIdentity(cap);
            meshes.Add(mesh);
        }

        var material = HypsoPlateMaterialOverride;
        PlateSurfaceMaterialTuning.ForView(viewMode).ApplyTo(material);
        renderer.SetMeshes(meshes, material);

        // M-B: cache the build inputs so UpdateExploded can rebuild byte-identical TOP DTOs (same
        // Continents/terrain colors + emission) and the solid thickness exaggeration matches the
        // surface relief exaggeration exactly. Centroids come from the BASE unit-sphere corners
        // (tick/relief-invariant) so the explode direction stays stable across ticks.
        _lastCaps = caps;
        _lastExaggeration = projection.MetresToUnitRadius;
        _lastCentroids = PlateSolidBuilder.ComputeCentroids(snapshot);
        _lastViewMode = viewMode;
        _lastIsTerrain = isTerrain;
        _lastPerPlateVertexColors = perPlateVertexColors;
        _lastPerCellColor = perCellColor;
        _lastPerCellEmission = perCellEmission;
        _lastJitter = jitter;
        _lastColorMode = colorMode;
        _lastNormalMode = normalMode;
        _lastContinentsCellColors = continentsCellColors;
        _lastContinentsFrontier = continentsFrontier;

        _log.LogInformation(
            "Planet plate surface bound: view={ViewMode}, subdivision={Subdivision}, plates={PlateCount}, triangles={TriangleCount}, meshVertices={VertexCount}, scale={Scale}, trueScale={TrueScale}, amplification={Amplification}x, frequency={Frequency}.",
            viewMode,
            useAdaptiveSurface ? "adaptive" : "fixed",
            caps.Count,
            caps.Sum(cap => cap.Surface.TriangleCount),
            meshes.Sum(mesh => mesh.VertexCount),
            projection.MetresToUnitRadius,
            projection.TrueScaleMetresToUnitRadius,
            projection.ReliefAmplification,
            snapshot.Frequency);
    }
}
