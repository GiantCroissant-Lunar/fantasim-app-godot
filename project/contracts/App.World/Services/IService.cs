using System.Threading;
using ServiceArchi.Contracts;
using ServiceArchi.Contracts.Attributes;
using FantaSim.App.World.Dto;

namespace FantaSim.App.World;

[ServiceContract]
public interface IService
{
    WorldOverview GetOverviewAsync();
    WorldFieldValues GetFieldValuesAsync(WorldFieldValuesRequest request);
    WorldScalarFieldValues GetScalarFieldValuesAsync(WorldScalarFieldValuesRequest request);
    WorldRenderSnapshot GetRenderSnapshotAsync();
    WorldGenerationProductsView GetGenerationProductsAsync();
    PlanetPresentationDocument GetPlanetPresentationAsync();

    /// <summary>
    /// Same as <see cref="GetPlanetPresentationAsync()"/> but builds the plate-boundary arcs at
    /// <paramref name="referenceTick"/> instead of the plate-onset tick, so a presentation binder
    /// that refreshes on a regime change gets arcs that reflect the playhead's current topology.
    /// The globe base geometry stays anchored at onset (it is the motion reference frame); only the
    /// boundary arcs are re-derived at the requested tick.
    /// </summary>
    PlanetPresentationDocument GetPlanetPresentationAsync(long referenceTick);

    /// <summary>
    /// Same as <see cref="GetPlanetPresentationAsync(long)"/> but materializes crust products at the
    /// requested tessellation frequency (clamped to [2, configured default]). D8b progressive-
    /// resolution scrub (vault/plans/2026-07-11-d8b-progressive-resolution-slice1-plan.md): low
    /// rungs follow the hand, the rest climb re-requests at higher rungs. Default implementation
    /// ignores the hint so existing fakes compile unchanged.
    /// </summary>
    PlanetPresentationDocument GetPlanetPresentationAsync(long referenceTick, int tessellationFrequency)
        => GetPlanetPresentationAsync(referenceTick);

    /// <summary>
    /// Light per-tick globe snapshot (M0, spec §3.2): returns the reassigned cell-&gt;plate globe at
    /// <paramref name="tick"/>, equivalent to <c>reconstructor.BuildGlobeAt(tick)</c>, without
    /// materializing crust. The OnsetRoster and GlobeReconstructor are cached per
    /// (seed, tessellationFrequency) so per-scrub calls stay within budget. Thread-safe.
    /// </summary>
    WorldGlobeSnapshot GetGlobeSnapshotAt(long tick);

    /// <summary>
    /// Per-cell boundary classification at <paramref name="tick"/> from the SAME reassigned
    /// membership <see cref="GetGlobeSnapshotAt"/> returns (0 = interior; 1..3 = typed frontier),
    /// equivalent to <c>reconstructor.ClassifyCellsAt(tick)</c>. Drives the M0 Continents view's
    /// frontier tint (spec D4: consistent-by-construction, no typed arc polylines). Cached and
    /// thread-safe like <see cref="GetGlobeSnapshotAt"/>.
    /// </summary>
    byte[] GetGlobeBoundaryCellsAt(long tick);

    /// <summary>
    /// Per-cell continental fraction sampled in the moving plate frame at <paramref name="tick"/>
    /// (P3 per-tick light path). Re-samples the cached crust snapshot's onset material through the
    /// <c>PlateFrameSampler</c> Lagrangian transport at the seek tick WITHOUT re-materializing crust,
    /// so the Continents view drifts smoothly between 5 M-tick snapshots instead of stepping.
    /// Tick-addressed and cached like <see cref="GetGlobeSnapshotAt"/>.
    /// </summary>
    IReadOnlyDictionary<int, double> GetContinentalFractionByCellAt(long tick);

    /// <summary>
    /// Cheap CPU thumbnail source for timeline compact track filmstrips. Implementations must use
    /// low-resolution world data (frequency 2/3 class) and must not fetch the full presentation
    /// document or trigger full-frequency crust generation just to paint a preview.
    /// Implementations MUST honor <paramref name="cancellationToken"/> promptly (per pixel row at
    /// worst): callers cancel it when a bundle severs, because a render still on a threadpool
    /// stack roots the timeline AND world ALCs past the hot-reload collection probe.
    /// </summary>
    LayerFilmstripPreviewMap? GetLayerFilmstripPreview(LayerFilmstripPreviewRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mantle x-ray view (M-A): the four isosurface meshes (translucent outer + opaque inner per
    /// polarity) of the VOLUMETRIC mantle anomaly at <paramref name="tick"/>. Adapts the presentation
    /// document's typed boundary arcs at the tick into the engine's volumetric <c>MantleAnomalyField</c>
    /// (a true T'(direction, radius, tick) — slab ribbons with dip, basal blanket, mushroom plumes),
    /// samples it on a shell-clipped grid, and extracts isosurfaces via CPU marching cubes with
    /// field-gradient normals. Pure data (floats/ints) so the resident presentation seam binds it
    /// without referencing the engine package. Any mesh may be empty at the tick. Deterministic.
    /// </summary>
    MantleIsosurfaceSet GetMantleIsosurfacesAsync(long tick);

    WorldGenerationResult RunGenerationAsync(WorldGenerationRequest request);
    IDisposable SubscribeGenerationChanged(Action<WorldGenerationChangedEvent> callback);
}
