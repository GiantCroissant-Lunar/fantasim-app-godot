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
    /// Light per-tick globe snapshot (M0, spec §3.2): returns the reassigned cell-&gt;plate globe at
    /// <paramref name="tick"/>, equivalent to <c>reconstructor.BuildGlobeAt(tick)</c>, without
    /// materializing crust. The OnsetRoster and GlobeReconstructor are cached per
    /// (seed, tessellationFrequency) so per-scrub calls stay within budget. Thread-safe.
    /// </summary>
    WorldGlobeSnapshot GetGlobeSnapshotAt(long tick);

    WorldGenerationResult RunGenerationAsync(WorldGenerationRequest request);
    IDisposable SubscribeGenerationChanged(Action<WorldGenerationChangedEvent> callback);
}
