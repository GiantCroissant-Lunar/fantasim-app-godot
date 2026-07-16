using System;
using System.Collections.Generic;
using System.Linq;
using FantaSim.App.World;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Dto;

namespace FantaSim.App.World.Globe;

/// <summary>
/// The narrow adapter that turns the EXISTING boundary data into <see cref="SlabJointClassification"/>
/// records for the slab-edge shaper (assembled-world slice 2). One <see cref="SlabJointClassification"/>
/// per ACTIVE plate pair: the kind and geometry come from <see cref="PlateBoundaryArc"/>; the resolved
/// subduction polarity (which side subducts, collision vs subduction) comes from the matching
/// <see cref="BoundarySectionDocument"/>.
/// </summary>
/// <remarks>
/// <para><b>This is the swap point.</b> A sibling dispatch is building a fuller joint classifier;
/// the lead session replaces this whole type at integration. The edge shaper depends only on
/// <see cref="SlabJointClassification"/>, so swapping the adapter never touches the geometry. Keep
/// this type single-purpose and easily replaced: arcs + sections in, classifications out.</para>
///
/// <para><b>Polarity source.</b> <see cref="PlateBoundaryArc"/> carries the boundary TYPE but not
/// subduction polarity; the polarity is a composition decision the crust pipeline surfaces on the
/// <see cref="BoundarySectionDocument"/> (<c>SubductingPlateId</c> / <c>IsCollision</c>). This adapter
/// reuses that already-resolved contract-tier polarity rather than re-deriving it — no second
/// classifier, no plugins-tier dependency.</para>
///
/// <para>Pure, Godot-free, deterministic. Same inputs always yield the same classifications in the
/// same order (pairs emitted in ascending (<c>PlateA</c>, <c>PlateB</c>) order).</para>
/// </remarks>
public static class SlabJointClassifier
{
    /// <summary>
    /// Builds one <see cref="SlabJointClassification"/> per active plate pair found in
    /// <paramref name="arcs"/>. A pair with multiple arc segments collapses to one classification
    /// whose <see cref="SlabJointClassification.ArcPoints"/> is the union of the segments' points.
    /// Inactive pairs are omitted.
    /// </summary>
    /// <param name="arcs">All typed boundary arcs (the complete joint inventory: every pair + kind).</param>
    /// <param name="sections">Boundary-normal section documents carrying resolved convergent polarity,
    /// keyed implicitly by plate pair. Null or empty when the pipeline produced none — convergent
    /// pairs then classify with no resolved subducting side (the shaper treats them as symmetric).</param>
    /// <returns>One classification per active pair, ascending (<c>PlateA</c>, <c>PlateB</c>) order.</returns>
    public static IReadOnlyList<SlabJointClassification> Build(
        IReadOnlyList<PlateBoundaryArc> arcs,
        IReadOnlyList<BoundarySectionDocument>? sections)
    {
        ArgumentNullException.ThrowIfNull(arcs);

        var polarityByPair = IndexPolarityByPair(sections);

        // Group arcs by ordered plate pair, preserving first-seen point order within each pair.
        var pointsByPair = new Dictionary<(int Lo, int Hi), List<GlobeVec3>>();
        var kindByPair = new Dictionary<(int Lo, int Hi), PlateBoundaryKind>();
        foreach (var arc in arcs)
        {
            if (arc.Kind == PlateBoundaryKind.Inactive) continue;
            if (arc.Points.Count == 0) continue;

            var key = arc.PlateA <= arc.PlateB ? (arc.PlateA, arc.PlateB) : (arc.PlateB, arc.PlateA);
            if (!pointsByPair.TryGetValue(key, out var points))
            {
                points = new List<GlobeVec3>();
                pointsByPair[key] = points;
                kindByPair[key] = arc.Kind;
            }
            else
            {
                kindByPair[key] = HigherPriorityKind(kindByPair[key], arc.Kind);
            }
            // De-duplicate consecutive repeats so adjacent segments do not double-stack endpoints.
            foreach (var p in arc.Points)
            {
                if (points.Count == 0 || !SameUnitDirection(points[points.Count - 1], p))
                    points.Add(p);
            }
        }

        // Emit one classification per pair in ascending (lo, hi) order — deterministic output order.
        var joints = new List<SlabJointClassification>(pointsByPair.Count);
        foreach (var key in pointsByPair.Keys.OrderBy(k => k.Lo).ThenBy(k => k.Hi))
        {
            var kind = kindByPair[key];
            int? subductingId = null;
            bool isCollision = false;
            if (kind == PlateBoundaryKind.Convergent && polarityByPair.TryGetValue(key, out var pol))
            {
                subductingId = pol.SubductingPlateId;
                isCollision = pol.IsCollision;
            }

            joints.Add(new SlabJointClassification(
                PlateA: key.Lo,
                PlateB: key.Hi,
                Kind: kind,
                SubductingPlateId: subductingId,
                IsCollision: isCollision,
                ArcPoints: pointsByPair[key]));
        }
        return joints;
    }

    private static Dictionary<(int Lo, int Hi), (int? SubductingPlateId, bool IsCollision)> IndexPolarityByPair(
        IReadOnlyList<BoundarySectionDocument>? sections)
    {
        var index = new Dictionary<(int Lo, int Hi), (int?, bool)>();
        if (sections is null) return index;
        foreach (var section in sections)
        {
            if (section.Kind != PlateBoundaryKind.Convergent) continue;
            var key = section.PlateA <= section.PlateB ? (section.PlateA, section.PlateB) : (section.PlateB, section.PlateA);
            // First section for a pair wins; later duplicates do not override (deterministic).
            if (!index.ContainsKey(key))
                index[key] = (section.SubductingPlateId, section.IsCollision);
        }
        return index;
    }

    // Matches CellBoundaryField.KindPriority: Convergent > Divergent > Transform > Inactive. A pair
    // whose arcs disagree keeps the mechanically more expressive kind at the joint.
    private static PlateBoundaryKind HigherPriorityKind(PlateBoundaryKind existing, PlateBoundaryKind candidate)
    {
        int existingPriority = KindPriority(existing);
        int candidatePriority = KindPriority(candidate);
        return candidatePriority < existingPriority ? candidate : existing;
    }

    private static int KindPriority(PlateBoundaryKind kind) => kind switch
    {
        PlateBoundaryKind.Convergent => 0,
        PlateBoundaryKind.Divergent => 1,
        PlateBoundaryKind.Transform => 2,
        _ => 3,
    };

    private static bool SameUnitDirection(GlobeVec3 a, GlobeVec3 b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        double dz = a.Z - b.Z;
        return (dx * dx) + (dy * dy) + (dz * dz) < 1e-10;
    }
}
