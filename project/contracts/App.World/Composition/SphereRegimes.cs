using System.Collections.Generic;

namespace FantaSim.App.World.Composition;

/// <summary>
/// One REGIME of a sphere: a contiguous tick window <c>[StartTick, EndTick)</c> during which a fixed
/// set of layers is active. Regimes are the TEMPORAL axis of the layer stack -- e.g. the geosphere
/// passes through magma-ocean -&gt; stagnant-lid -&gt; mobile-plate. A layer is a stateless executable
/// model; the regime owns the time bounds. See <c>vault/architecture/sphere-regimes.md</c>.
/// </summary>
/// <param name="RegimeId">Stable id, unique within a sphere's schedule (e.g. "mobile-plate").</param>
/// <param name="StartTick">First tick the regime is active (INCLUSIVE). Genesis = 0.</param>
/// <param name="EndTick">First tick the regime is NO LONGER active (EXCLUSIVE). Open-ended = <see cref="OpenEnd"/>.</param>
/// <param name="ActiveLayers">The layers this regime activates (composed into the field DAG while current).</param>
/// <param name="DefaultColorByField">
/// Optional: the field id the globe colors by while this regime is current and the user has not
/// chosen an explicit color-by (e.g. magma-ocean -&gt; "surface-temperature-k" reveals the molten
/// pre-plate phase). Null = the regime's default identity/plate coloring. (Code-seeded for now;
/// the JSON loader does not parse it yet.)
/// </param>
/// <param name="ShowsPlateFeatures">
/// Whether plate features (boundary lines, junctions, subduction/rift/transform schematics, the
/// phenomena slab overlay) are rendered while this regime is current. False for the pre-plate
/// regimes (magma-ocean, stagnant-lid) -- a planet with no plates yet shows none.
/// </param>
public sealed record SphereRegime(
    string RegimeId,
    long StartTick,
    long EndTick,
    IReadOnlyList<LayerId> ActiveLayers,
    string? DefaultColorByField = null,
    bool ShowsPlateFeatures = true)
{
    /// <summary>Open-ended end sentinel: the final regime runs to the end of time.</summary>
    public const long OpenEnd = long.MaxValue;

    /// <summary>True when <paramref name="tick"/> falls in the half-open window <c>[StartTick, EndTick)</c>.</summary>
    public bool Contains(long tick) => tick >= StartTick && tick < EndTick;
}

/// <summary>
/// A sphere's ordered regime schedule -- the TIME axis for one sphere. Regimes should be ordered,
/// contiguous, and non-overlapping (the loader validates this); <see cref="RegimeAt"/> returns the
/// regime current at a tick. Separate from the layer-stack manifest (which layers exist / opinion
/// strength): this says WHICH regime -- hence which layers -- is active WHEN.
/// </summary>
public sealed record SphereRegimeSchedule(SphereId Sphere, IReadOnlyList<SphereRegime> Regimes)
{
    /// <summary>
    /// The regime active at <paramref name="tick"/>, or <c>null</c> if no regime covers it. Linear
    /// scan (schedules have a handful of regimes); the first matching regime wins, so a well-formed
    /// (non-overlapping) schedule yields exactly one.
    /// </summary>
    public SphereRegime? RegimeAt(long tick)
    {
        for (int i = 0; i < Regimes.Count; i++)
        {
            if (Regimes[i].Contains(tick))
                return Regimes[i];
        }
        return null;
    }
}
