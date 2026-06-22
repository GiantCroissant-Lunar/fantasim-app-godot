using System;
using System.Collections.Generic;

namespace FantaSim.App.World.Composition;

/// <summary>
/// PLACEHOLDER crust layer. Its ONLY job is to prove field composition + the field-value runtime (a
/// real producer-&gt;consumer edge that actually flows data), NOT geology -- the values are crude and
/// synthetic. Replace this with the package-backed producer when a corrected Geosphere.Crust.* 0.3.x
/// is restored into C: fantasim-world. The visibly-fake name is intentional so the swap is obvious.
/// <para>
/// It produces two fields per cell (= plate, v1):
/// </para>
/// <list type="bullet">
/// <item><c>crust-thickness-m</c> = a baseline plus a gain on the CONSUMED
/// <c>plate-boundary-distance-m</c> -- so the DAG edge is real and observable (thicker crust toward
/// plate interiors). Approximately static, like its input.</item>
/// <item><c>elevation-m</c> = synthetic uplift from the per-plate CONVERGENT boundary fraction minus
/// subsidence from the DIVERGENT fraction, both measured from the geometry's per-tick boundary
/// CLASSIFICATION. Boundary type is reclassified every tick as plates rotate, so this term -- and
/// therefore the globe coloring -- visibly changes as the timeline is scrubbed (review P1).</item>
/// </list>
/// </summary>
public sealed class SyntheticCrustLayer : IFieldProducer
{
    // Synthetic constants (NOT geology). Named so the swap to a real producer is obvious.
    // internal so the stagnant-lid regime can converge its crust thickness to the SAME mobile-plate
    // value at the regime boundary (cross-regime C0 continuity, sphere-regimes step 4). When this
    // placeholder is replaced by a package-backed producer, the lid's C0 target must follow these.
    internal const double BaseCrustThicknessM  = 30_000.0;   // ~30 km baseline crust
    internal const double CrustDistanceGain    = 0.01;       // extra metres of crust per metre of boundary distance
    private const double ConvergentUpliftM    = 8_000.0;    // uplift at a fully-convergent margin
    private const double DivergentSubsidenceM = 4_000.0;    // subsidence at a fully-divergent margin

    // PRUNED: was using FantaSim.App.World.Ecs (BoundaryMotionClassifier -- FantaSim.App.World.Ecs not
    // present in App.World.Composition). Boundary-type strings inlined from their canonical values.
    private const string BoundaryConvergent = "convergent";
    private const string BoundaryDivergent  = "divergent";

    public LayerId  Id     { get; } = new("geosphere.crust");
    public SphereId Sphere { get; } = new("geosphere");
    public LayerFieldBinding Fields { get; } = new(
        new LayerId("geosphere.crust"),
        Produces: new[] { GeosphereFieldCatalog.Elevation, GeosphereFieldCatalog.CrustThickness },
        Consumes: new[] { new FieldConsumption(GeosphereFieldCatalog.PlateBoundaryDistance, Required: true) });

    /// <summary>
    /// VALUE-compute (layer-stack step 4a-ii). Reads the consumed plate-boundary distance (the real
    /// DAG edge) for crust thickness, and the geometry's per-tick boundary classification for the
    /// tick-varying elevation. Only reads its declared input -- resolver invariant (declared reads).
    /// </summary>
    public void Produce(IFieldComputeContext context)
    {
        var consumedDistance = context.GetScalar(GeosphereFieldCatalog.PlateBoundaryDistance);
        var cells = context.Geometry.Cells;
        int n = context.CellCount;

        // Per-plate boundary-type tallies AT THIS TICK. Boundary classification is recomputed per
        // tick in reconstruction (kinematics.GetRotationAt(plate, tick)), so these fractions move as
        // the plates rotate -- this is what makes elevation tick-varying.
        var convergent = new Dictionary<string, int>(StringComparer.Ordinal);
        var divergent  = new Dictionary<string, int>(StringComparer.Ordinal);
        var total      = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var seg in context.Geometry.BoundarySegments)
        {
            Tally(seg.PlateAId, seg.BoundaryType, convergent, divergent, total);
            Tally(seg.PlateBId, seg.BoundaryType, convergent, divergent, total);
        }

        var thickness = new double[n];
        var elevation = new double[n];
        for (int i = 0; i < n; i++)
        {
            // Crust thickness rises with the CONSUMED plate-boundary distance (the real DAG edge).
            thickness[i] = BaseCrustThicknessM + CrustDistanceGain * consumedDistance[i];

            var plate = cells[i].PlateId;
            int tot = total.TryGetValue(plate, out var t) ? t : 0;
            double convFrac = tot > 0 && convergent.TryGetValue(plate, out var c) ? (double)c / tot : 0.0;
            double divFrac  = tot > 0 && divergent.TryGetValue(plate, out var d)  ? (double)d / tot : 0.0;

            // Elevation rises with the convergent fraction, falls with the divergent fraction (both
            // per-tick) -> scrubbing the timeline reclassifies boundaries and recolors the globe.
            elevation[i] = ConvergentUpliftM * convFrac - DivergentSubsidenceM * divFrac;
        }

        context.SetScalar(GeosphereFieldCatalog.CrustThickness, thickness);
        context.SetScalar(GeosphereFieldCatalog.Elevation, elevation);
    }

    private static void Tally(
        string? plateId,
        string boundaryType,
        Dictionary<string, int> convergent,
        Dictionary<string, int> divergent,
        Dictionary<string, int> total)
    {
        if (string.IsNullOrEmpty(plateId))
            return;

        total[plateId] = (total.TryGetValue(plateId, out var t) ? t : 0) + 1;
        if (string.Equals(boundaryType, BoundaryConvergent, StringComparison.Ordinal))
            convergent[plateId] = (convergent.TryGetValue(plateId, out var c) ? c : 0) + 1;
        else if (string.Equals(boundaryType, BoundaryDivergent, StringComparison.Ordinal))
            divergent[plateId] = (divergent.TryGetValue(plateId, out var d) ? d : 0) + 1;
    }
}
