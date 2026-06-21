using Arch.Core;
using FantaSim.App.Ecs.Cells;
using UnifyECS;
using ArchWorld = Arch.Core.World;

namespace FantaSim.App.Ecs.Systems;

/// <summary>
/// Derives a per-cell <see cref="Elevation"/> scalar from each cell-entity's <see cref="CrustSample"/>
/// (app T3). Primitive in/out (no engine types), so this system is ALWAYS compiled — it is the
/// load-bearing ECS step the relief render (sub-project C) consumes via the per-cell elevation array.
///
/// <para>The derivation is one tunable step. The SPEC is the resulting ORDERING (constants chosen to
/// satisfy it); over a 0..100-anchor crust run the elevation tiers separate as
/// <c>collision &gt; continental-interior &gt; 0 &gt; young-ocean &gt; old-ocean</c>:</para>
/// <list type="bullet">
/// <item><c>base   = (ContinentalFraction − SeaLevel) × ContinentalAmp</c> — land up / ocean down.</item>
/// <item><c>uplift = OrogenicPressure × OrogenicGain</c> — collision mountains.</item>
/// <item><c>volcano = VolcanicActivity × VolcanoGain</c> — arcs / ridges.</item>
/// <item><c>oceanDepth = −(CrustAgeTicks / CrustAgeReferenceTicks) × OceanDeepening</c>, oceanic cells
///       only (<c>ContinentalFraction &lt; SeaLevel</c>) — older seafloor sinks deeper.</item>
/// <item><c>Elevation = base + uplift + volcano + oceanDepth</c>.</item>
/// </list>
/// Implements the backend-neutral <see cref="IUnifySystem"/> marker and the Arch-specific
/// <see cref="IArchSystem"/> so <c>ArchSystemRunner.Update</c> dispatches to <see cref="Execute"/>.
/// </summary>
public sealed class CellElevationSystem : IUnifySystem, IArchSystem
{
    // Sea level on the [0,1] continental-fraction axis: cf above is land, below is ocean.
    public const double SeaLevel = 0.5;

    // Continental amplitude: maps the ±0.5 land/ocean offset to ±500 of base relief.
    public const double ContinentalAmp = 1000.0;

    // Orogenic gain: collision pressure ~100 ⇒ +2000 uplift, so a collision cell clears the
    // continental interior (+500) by a wide margin (continent–continent collision sits highest).
    public const double OrogenicGain = 20.0;

    // Volcano gain: volcanism ~50 ⇒ +200 — a secondary arc/ridge contribution, never enough to
    // lift an oceanic cell above sea level on its own.
    public const double VolcanoGain = 4.0;

    // Ocean deepening + age reference: a fully-aged oceanic cell (age ≈ CrustAgeReferenceTicks)
    // sinks an extra −1000 below its −500 oceanic base ⇒ old abyssal ocean sits lowest (~−1500).
    public const double OceanDeepening = 1000.0;
    public const double CrustAgeReferenceTicks = 1.0e7;

    private readonly QueryDescription _query = new QueryDescription().WithAll<CrustSample, Elevation>();

    public void Execute(ArchWorld world, float deltaTime)
    {
        world.Query(in _query, (ref CrustSample sample, ref Elevation elevation) =>
        {
            elevation = new Elevation(Derive(sample));
        });
    }

    /// <summary>
    /// Pure elevation derivation for one cell — exposed so tests can pin the ordering without an
    /// Arch world, and so the formula has a single definition.
    /// </summary>
    public static double Derive(in CrustSample sample)
    {
        double @base = (sample.ContinentalFraction - SeaLevel) * ContinentalAmp;
        double uplift = sample.OrogenicPressure * OrogenicGain;
        double volcano = sample.VolcanicActivity * VolcanoGain;

        // Only oceanic crust (below sea level) deepens with age; continental cells ignore age.
        double oceanDepth = 0.0;
        if (sample.ContinentalFraction < SeaLevel)
        {
            double ageNormalised = sample.CrustAgeTicks / CrustAgeReferenceTicks;
            oceanDepth = -ageNormalised * OceanDeepening;
        }

        return @base + uplift + volcano + oceanDepth;
    }
}
