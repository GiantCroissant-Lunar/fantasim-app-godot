using System.Collections.Generic;
using System.Linq;

using FantaSim.Atmosphere.Genesis.Core;
using FantaSim.Atmosphere.Contracts;

namespace FantaSim.App.World.Composition;

/// <summary>
/// Code-seeded default regime schedules, until <c>world-regimes.json</c> wiring lands (R3: app-side
/// schedule first). Mirrors how <c>ResidentLayers</c> is hardcoded in the orchestrator today.
/// </summary>
public static class SphereRegimeScheduleDefaults
{
    internal const string Geosphere = "geosphere";
    internal const string Atmosphere = "atmosphere";

    /// <summary>
    /// NO-OP seed (sphere-regimes step 1): the geosphere has a SINGLE <c>mobile-plate</c> regime
    /// spanning all of time, activating every supplied resident layer. <see cref="SphereRegimeSchedule.RegimeAt"/>
    /// therefore always returns mobile-plate, so the composed field DAG equals the pre-regime boot
    /// composition byte-for-byte. Replaced by an explicit magma-ocean -&gt; stagnant-lid -&gt;
    /// mobile-plate schedule in step 3.
    /// </summary>
    public static SphereRegimeSchedule SingleMobilePlate(IEnumerable<ILayer> residentLayers) =>
        new(
            new SphereId(Geosphere),
            new[]
            {
                new SphereRegime(
                    RegimeId: "mobile-plate",
                    StartTick: 0,
                    EndTick: SphereRegime.OpenEnd,
                    ActiveLayers: residentLayers.Select(l => l.Id).ToList()),
            });

    /// <summary>End of the stylized magma-ocean regime (R1: ~1e6 ticks = ~10 Ma); mobile plates follow.
    /// The stagnant-lid regime (step 4) will later split <c>[MagmaOceanEndTick, plate-onset)</c>.</summary>
    public const long MagmaOceanEndTick = 1_000_000;

    /// <summary>Surface-hydration threshold that triggers mobile-plate onset (R: water-assisted
    /// subduction). With the default outgassing/hydration curve this yields <c>100_000_000</c>
    /// exactly; a different atmosphere forcing shifts plate onset (see <see cref="PlateOnsetTickFor"/>).</summary>
    public const double HydrationOnsetThreshold = 0.99;

    /// <summary>CAUSAL plate onset for the <see cref="AtmosphereForcing.Default"/> curve (R:
    /// water-assisted subduction). Mobile plates begin when the atmosphere has delivered enough
    /// surface water -- <c>SurfaceHydrationIndex</c> &gt;= <see cref="HydrationOnsetThreshold"/>. With
    /// the baseline curve this is exactly <c>100_000_000</c> (unchanged). A non-default forcing shifts
    /// it: see <see cref="PlateOnsetTickFor"/>. Computed once (the solver is pure + stateless).</summary>
    public static readonly long PlateOnsetTick = PlateOnsetTickFor(AtmosphereForcing.Default);

    /// <summary>The causal plate onset for a given atmosphere <paramref name="forcing"/>: the first
    /// tick at which the forcing's surface-hydration curve reaches <see cref="HydrationOnsetThreshold"/>.
    /// A stronger forcing hydrates faster -&gt; earlier onset; a weaker one -&gt; later.</summary>
    public static long PlateOnsetTickFor(AtmosphereForcing forcing)
        => ComputePlateOnsetTick(new PrimordialAtmosphereSolver(forcing));

    /// <summary>Smallest tick whose <c>SurfaceHydrationIndex</c> reaches
    /// <see cref="HydrationOnsetThreshold"/> under the given <paramref name="solver"/> (binary search;
    /// assumes monotonic non-decreasing hydration). Clamps the search to <c>[0, 1e9]</c>.</summary>
    public static long ComputePlateOnsetTick(IAtmosphereStateSolver solver)
    {
        long lo = 0, hi = 1_000_000_000;
        while (lo < hi)
        {
            long mid = lo + (hi - lo) / 2;
            if (solver.GetStateAtTick(mid).SurfaceHydrationIndex >= HydrationOnsetThreshold) hi = mid;
            else lo = mid + 1;
        }
        return lo;
    }

    /// <summary>Window past <see cref="PlateOnsetTick"/> over which plate features (boundary lines,
    /// subduction/rift/transform marks) fade in instead of popping at full strength (step 5
    /// crossfade). ~5 Ma = 5% of the onset tick.</summary>
    public const long PlateFeatureFadeInTicks = 5_000_000;

    /// <summary>
    /// The LIVE geosphere schedule (sphere-regimes steps 3-4) for a given plate <paramref name="onsetTick"/>:
    /// molten <c>magma-ocean</c> (surface-temperature) -&gt; immobile <c>stagnant-lid</c> (heat-flow) -&gt;
    /// <c>mobile-plate</c> (plate + crust, default plate coloring). Crust thickness is authored
    /// C0-continuous across the lid-&gt;plate boundary (which sits at <paramref name="onsetTick"/>).
    /// </summary>
    public static SphereRegimeSchedule GeosphereFor(long onsetTick) =>
        new(
            new SphereId(Geosphere),
            new[]
            {
                new SphereRegime(
                    RegimeId: "magma-ocean",
                    StartTick: 0,
                    EndTick: MagmaOceanEndTick,
                    ActiveLayers: new[] { new LayerId("geosphere.magma-ocean") },
                    DefaultColorByField: GeosphereFieldCatalog.SurfaceTemperature.Value,
                    ShowsPlateFeatures: false),
                new SphereRegime(
                    RegimeId: "stagnant-lid",
                    StartTick: MagmaOceanEndTick,
                    EndTick: onsetTick,
                    ActiveLayers: new[] { new LayerId("geosphere.stagnant-lid") },
                    DefaultColorByField: GeosphereFieldCatalog.HeatFlow.Value,
                    ShowsPlateFeatures: false),
                new SphereRegime(
                    RegimeId: "mobile-plate",
                    StartTick: onsetTick,
                    EndTick: SphereRegime.OpenEnd,
                    ActiveLayers: new[] { new LayerId("geosphere.plate"), new LayerId("geosphere.crust") }),
            });

    /// <summary>
    /// The atmosphere regime schedule for a given plate <paramref name="onsetTick"/>: a first-class
    /// sphere parallel to the geosphere. Phase boundaries align with the geosphere regime windows and
    /// mirror the world library's <c>AtmospherePhaseScheduleDefaults</c> (primordial-steam -&gt;
    /// secondary-co2 -&gt; coupled-climate). Each atmosphere regime activates <c>atmosphere.bulk</c>;
    /// the composed field DAG stays the same (atmosphere.bulk is active across all of time), just
    /// sourced from its own schedule.
    /// </summary>
    public static SphereRegimeSchedule AtmosphereFor(long onsetTick) =>
        new(
            new SphereId(Atmosphere),
            new[]
            {
                new SphereRegime("primordial-steam", 0,                 MagmaOceanEndTick, new[] { new LayerId("atmosphere.bulk") }),
                new SphereRegime("secondary-co2",    MagmaOceanEndTick, onsetTick,         new[] { new LayerId("atmosphere.bulk") }),
                new SphereRegime("coupled-climate",  onsetTick,         SphereRegime.OpenEnd, new[] { new LayerId("atmosphere.bulk"), new LayerId("atmosphere.coupled") }),
            });

    /// <summary>The geosphere schedule for the <see cref="AtmosphereForcing.Default"/> curve
    /// (onset at <see cref="PlateOnsetTick"/>). Kept for back-compat (tests + label fallbacks).</summary>
    public static SphereRegimeSchedule GeosphereDefault => GeosphereFor(PlateOnsetTick);

    /// <summary>The atmosphere schedule for the <see cref="AtmosphereForcing.Default"/> curve
    /// (onset at <see cref="PlateOnsetTick"/>). Kept for back-compat (tests + label fallbacks).</summary>
    public static SphereRegimeSchedule AtmosphereDefault => AtmosphereFor(PlateOnsetTick);
}
