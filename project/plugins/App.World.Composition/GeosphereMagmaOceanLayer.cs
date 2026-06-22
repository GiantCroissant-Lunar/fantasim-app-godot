using System;
using System.Collections.Generic;

using FantaSim.App.World;   // WorldGlobeGeometry, GeoPoint, BoundaryGeoSegment

namespace FantaSim.App.World.Composition;

/// <summary>
/// The geosphere's MAGMA-OCEAN regime layer (sphere-regimes step 3): the molten early planet, before
/// any crust or plates. A STYLIZED thermal model (NOT geophysics) producing two per-cell fields:
/// <list type="bullet">
/// <item><c>surface-temperature-k</c> -- global exponential cooling from molten (Genesis) toward an
/// ambient near-solidus surface, plus retained heat near (future) plate boundaries.</item>
/// <item><c>melt-fraction</c> -- 0..1 solidification state derived from the temperature
/// (1 = fully molten at Genesis, → 0 as the surface solidifies).</item>
/// </list>
/// <para>
/// CONTINUITY (R4, shared-seed derived): this consumes nothing and instead reads the reconstructed
/// GEOMETRY (cells + boundary segments) handed to every producer. That geometry comes from the same
/// seed-derived plate topology the mobile-plate regime later renders, so the late cooling pattern
/// FORESHADOWS where boundaries appear -- the "shared seed" effect achieved through shared geometry,
/// with no seed plumbing. The cooling timescale is absolute (tick-based), so the layer stays
/// schedule-agnostic; the regime schedule decides only WHEN this layer is the active producer.
/// </para>
/// </summary>
public sealed class GeosphereMagmaOceanLayer : IFieldProducer
{
    // Stylized thermal constants (NOT geophysics; the magnitude is a creative knob -- R1).
    private const double GenesisSurfaceK = 2000.0;     // fully molten at Genesis
    private const double AmbientK        = 1300.0;     // cooled basaltic surface (~ solidus)
    private const double LiquidusK       = 1500.0;     // fully molten at/above this
    private const double SolidusK        = 1300.0;     // fully solid at/below this
    private const double CoolingTauTicks = 300_000.0;  // e-folding cooling time (~3 ka); mostly solid by ~1e6 (R1)
    private const double BoundaryHotspotK = 350.0;     // extra heat retained near (future) boundaries
    private const double HotspotScaleM    = 1_500_000.0; // hotspot decays over ~1500 km from a boundary
    private const double NoBoundaryDistanceM = 4 * HotspotScaleM; // worlds with no segments -> no hotspot

    // Reference retained heat = the default cloud's accreted heat, so the default world yields exactly
    // GenesisSurfaceK (continuity: the live default is unchanged because total accreted mass is currently
    // constant). More/less retained heat shifts genesis hotter/cooler -- the causal effect of formation.
    // PRUNED: was BodyFormationProducer.SpecificAccretionHeatJPerKg * BodyFormationProducer.TotalMassKg
    //         (body formation is deferred; inlined from the ref constants: 1.0e7 J/kg * 5.972e24 kg).
    private const double ReferenceHeatJ = 5.972e31;

    public LayerId  Id     { get; } = new("geosphere.magma-ocean");
    public SphereId Sphere { get; } = new("geosphere");
    public LayerFieldBinding Fields { get; } = new(
        new LayerId("geosphere.magma-ocean"),
        Produces: new[] { GeosphereFieldCatalog.SurfaceTemperature, GeosphereFieldCatalog.MeltFraction },
        Consumes: Array.Empty<FieldConsumption>());

    public void Produce(IFieldComputeContext context)
    {
        var cells = context.Geometry.Cells;
        var segments = context.Geometry.BoundarySegments;
        int n = context.CellCount;

        // Body→sphere handoff (IFieldHandoffComputeContext) is DEFERRED (body formation out of scope).
        // Fall back to the stylized constant genesis temperature — identical to pre-body-formation behavior.
        double genesisK = GenesisSurfaceK;

        // Global cooling: exponential decay from the genesis temperature toward ambient. 1 at Genesis -> 0 as t grows.
        double cooled = Math.Exp(-context.Tick / CoolingTauTicks);
        double globalK = AmbientK + (genesisK - AmbientK) * cooled;

        var temperature = new double[n];
        var melt = new double[n];
        for (int i = 0; i < n; i++)
        {
            var centroid = GeosphereFieldMath.Centroid(cells[i].OuterRing);
            double dist = NearestBoundaryMeters(centroid, segments);

            // Boundary hotspot is RETAINED heat, so it fades with the global cooling too (× cooled).
            double hotspot = BoundaryHotspotK * Math.Exp(-dist / HotspotScaleM) * cooled;
            double t = globalK + hotspot;

            temperature[i] = t;
            melt[i] = Math.Clamp((t - SolidusK) / (LiquidusK - SolidusK), 0.0, 1.0);
        }

        context.SetScalar(GeosphereFieldCatalog.SurfaceTemperature, temperature);
        context.SetScalar(GeosphereFieldCatalog.MeltFraction, melt);
    }

    /// <summary>
    /// Great-circle distance (m) from <paramref name="from"/> to the nearest boundary-segment
    /// midpoint. Worlds with no segments return a large distance (→ no hotspot), keeping the field
    /// always defined (resolver invariant: every owned output is written).
    /// </summary>
    private static double NearestBoundaryMeters(GeoPoint from, IReadOnlyList<BoundaryGeoSegment> segments)
    {
        double best = double.PositiveInfinity;
        foreach (var seg in segments)
        {
            var mid = GeosphereFieldMath.Midpoint(seg.Start, seg.End);
            double d = GeosphereFieldMath.GreatCircleMeters(from, mid);
            if (d < best) best = d;
        }
        return double.IsPositiveInfinity(best) ? NoBoundaryDistanceM : best;
    }
}
