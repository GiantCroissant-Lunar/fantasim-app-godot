using System;
using System.Collections.Generic;
using FantaSim.App.World;
using FantaSim.App.World.Dto;
using FantaSim.Geosphere.Crust;
using UnifyMaths;

namespace FantaSim.App.World.Topography;

/// <summary>
/// For a convergent boundary, which plate subducts (down-going) vs overrides, and whether it is a
/// continent–continent collision (no subduction). The boundary TYPE (convergent) comes from the topology
/// classifier's relative motion; SUBDUCTION POLARITY is a composition decision the crust pipeline already
/// makes and surfaces as the per-cell <see cref="CrustFeatureKind.Trench"/> feature (assigned to the
/// oceanic/down-going side — see <c>CrustFeatures.Derive</c> in the engine). This type aggregates that
/// per-cell signal back to a per-boundary polarity, with a ContinentalFraction fallback that uses the SAME
/// physical rule as the engine's <c>CrustSideClassifier</c> (denser/oceanic crust subducts). Polarity is
/// NEVER guessed from elevation.
/// </summary>
public readonly record struct ConvergentBoundaryPolarity(int SubductingPlateId, int OverridingPlateId, bool IsCollision);

/// <summary>
/// Derives per-convergent-boundary subduction polarity from the crust feature distribution. See
/// <see cref="ConvergentBoundaryPolarity"/> for the polarity source rationale.
/// </summary>
public static class ConvergentPolarity
{
    /// <summary>
    /// Builds a per-plate-pair polarity map for every Convergent arc. The map is keyed by the ordered pair
    /// <c>(min(PlateA,PlateB), max(PlateA,PlateB))</c> matching <see cref="PlateBoundaryArc"/>.
    /// </summary>
    /// <param name="arcs">All typed boundary arcs (only Convergent arcs are polaritised; others are ignored).</param>
    /// <param name="cells">The globe's cells (centroid = the unit centroid of the three corners).</param>
    /// <param name="features">Per-cell crust features at the snapshot tick (carries Trench on the down-going side).
    /// Null when the pipeline produced none — the composition fallback then decides polarity.</param>
    /// <param name="state">Per-cell crust state at the snapshot tick (ContinentalFraction drives the fallback).</param>
    /// <param name="nearRadiusRad">A cell counts as "near" a boundary if its centroid is within this great-circle
    /// distance of any arc point. Default 0.15 rad (~1.5 cells at frequency 3).</param>
    /// <param name="continentalThreshold">ContinentalFraction at/above which a side is continental (matches the
    /// engine's <c>CrustFeatureThresholds.ContinentalThreshold</c> default 0.5).</param>
    public static IReadOnlyDictionary<(int PlateA, int PlateB), ConvergentBoundaryPolarity> Derive(
        IReadOnlyList<PlateBoundaryArc> arcs,
        IReadOnlyList<GlobeCell> cells,
        IReadOnlyDictionary<int, CrustFeature>? features,
        IReadOnlyDictionary<int, CellCrustState> state,
        double nearRadiusRad = 0.15,
        double continentalThreshold = 0.5)
    {
        ArgumentNullException.ThrowIfNull(arcs);
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(state);

        var pointsByPair = new Dictionary<(int Lo, int Hi), List<GlobeVec3>>();
        foreach (var arc in arcs)
        {
            if (arc.Kind != PlateBoundaryKind.Convergent) continue;
            if (arc.Points.Count == 0) continue;

            var (lo, hi) = arc.PlateA <= arc.PlateB ? (arc.PlateA, arc.PlateB) : (arc.PlateB, arc.PlateA);
            if (!pointsByPair.TryGetValue((lo, hi), out var points))
            {
                points = new List<GlobeVec3>();
                pointsByPair[(lo, hi)] = points;
            }
            points.AddRange(arc.Points);
        }

        var polarity = new Dictionary<(int, int), ConvergentBoundaryPolarity>(pointsByPair.Count);
        foreach (var ((lo, hi), points) in pointsByPair)
        {
            polarity[(lo, hi)] = ClassifyOne(
                points,
                lo,
                hi,
                cells,
                features,
                state,
                nearRadiusRad,
                continentalThreshold);
        }
        return polarity;
    }

    private static ConvergentBoundaryPolarity ClassifyOne(
        IReadOnlyList<GlobeVec3> arcPoints,
        int plateLo,
        int plateHi,
        IReadOnlyList<GlobeCell> cells,
        IReadOnlyDictionary<int, CrustFeature>? features,
        IReadOnlyDictionary<int, CellCrustState> state,
        double nearRadiusRad,
        double continentalThreshold)
    {
        // Gather near cells on each side, tally Trench features, and accumulate ContinentalFraction.
        int trenchLo = 0, trenchHi = 0;
        double cfSumLo = 0.0, cfSumHi = 0.0;
        int cfCountLo = 0, cfCountHi = 0;

        double cosNear = Math.Cos(nearRadiusRad);
        var arcVecs = ToVector3D(arcPoints);

        foreach (var cell in cells)
        {
            if (cell.PlateId != plateLo && cell.PlateId != plateHi) continue;
            if (!IsNearArc(cell, arcVecs, cosNear)) continue;

            bool hasTrench = features is not null
                && features.TryGetValue(cell.CellId, out var f)
                && f.Kind == CrustFeatureKind.Trench;

            if (cell.PlateId == plateLo)
            {
                if (hasTrench) trenchLo++;
                if (state.TryGetValue(cell.CellId, out var s)) { cfSumLo += s.ContinentalFraction; cfCountLo++; }
            }
            else
            {
                if (hasTrench) trenchHi++;
                if (state.TryGetValue(cell.CellId, out var s)) { cfSumHi += s.ContinentalFraction; cfCountHi++; }
            }
        }

        // Primary: the crust pipeline's Trench feature marks the down-going side.
        if (trenchLo > 0 && trenchHi == 0)
            return new ConvergentBoundaryPolarity(plateLo, plateHi, IsCollision: false);
        if (trenchHi > 0 && trenchLo == 0)
            return new ConvergentBoundaryPolarity(plateHi, plateLo, IsCollision: false);

        // Fallback: composition rule (same as the engine's CrustSideClassifier). Both continental ⇒ collision;
        // otherwise the lower-mean-ContinentalFraction (more oceanic/denser) side subducts.
        double meanLo = cfCountLo > 0 ? cfSumLo / cfCountLo : 0.0;
        double meanHi = cfCountHi > 0 ? cfSumHi / cfCountHi : 0.0;

        if (meanLo > continentalThreshold && meanHi > continentalThreshold)
            return new ConvergentBoundaryPolarity(plateLo, plateHi, IsCollision: true);

        // No state for a side treats it as oceanic (mean 0) — it subducts. Strictly-greater tie ⇒ lower id subducts.
        bool loSubducts = meanLo <= meanHi;
        return loSubducts
            ? new ConvergentBoundaryPolarity(plateLo, plateHi, IsCollision: false)
            : new ConvergentBoundaryPolarity(plateHi, plateLo, IsCollision: false);
    }

    private static bool IsNearArc(GlobeCell cell, Vector3D[] arcVecs, double cosNear)
    {
        var centroid = Centroid(cell);
        foreach (var p in arcVecs)
        {
            if (Vector3D.Dot(centroid, p) >= cosNear)
                return true;
        }
        return false;
    }

    private static Vector3D Centroid(GlobeCell cell)
    {
        var v = new Vector3D(cell.C0.X + cell.C1.X + cell.C2.X,
                             cell.C0.Y + cell.C1.Y + cell.C2.Y,
                             cell.C0.Z + cell.C1.Z + cell.C2.Z);
        double len = v.Length();
        return len < 1e-15 ? new Vector3D(1, 0, 0) : v * (1.0 / len);
    }

    private static Vector3D[] ToVector3D(IReadOnlyList<GlobeVec3> points)
    {
        var result = new Vector3D[points.Count];
        for (int i = 0; i < points.Count; i++)
            result[i] = new Vector3D(points[i].X, points[i].Y, points[i].Z);
        return result;
    }
}
