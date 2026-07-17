using System;
using System.Collections.Generic;
using FantaSim.App.World;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Dto;

namespace FantaSim.App.World.Globe;

/// <summary>
/// The motion-derived kind of a slab JOINT between two adjacent plates (assembled-world slice 2,
/// vault/specs/2026-07-16-assembled-world-northstar.md). Mirrors <see cref="PlateBoundaryKind"/> on
/// the joint surface so the joint-mechanics geometry (sibling dispatch) can switch on a single
/// contracts-tier enum without referencing the boundary-arc contract.
/// </summary>
public enum SlabJointKind
{
    /// <summary>Plates collide: one subducts under the other (or continent-continent collision).</summary>
    Convergent,

    /// <summary>Plates part: a spreading ridge / rift along the joint.</summary>
    Divergent,

    /// <summary>Boundary-parallel shear: a transform fault along the joint.</summary>
    Transform,

    /// <summary>No significant relative motion; renderers typically omit the joint.</summary>
    Inactive,
}

/// <summary>
/// Per-convergent-pair SUBDUCTION POLARITY input to <see cref="SlabJointClassifier"/> — a
/// contracts-tier projection of the existing <c>ConvergentBoundaryPolarity</c> the crust pipeline
/// produces (via <c>ConvergentPolarity.Derive</c>). This is NOT a parallel polarity DATA SOURCE: the
/// subducting plate id and collision flag are the exact values the existing derivation produced; this
/// record only carries them across the contract boundary so the classifier stays Godot-free and
/// contracts-tier (the engine <c>ConvergentBoundaryPolarity</c> lives in the App.World plugin).
/// </summary>
/// <param name="PlateA">The lower plate id of the pair (<c>PlateA &lt; PlateB</c>), matching the
/// <see cref="PlateBoundaryArc"/> ordering.</param>
/// <param name="PlateB">The higher plate id of the pair.</param>
/// <param name="SubductingPlateId">The plate id that subducts (dives under) for a convergent
/// non-collision boundary; <c>null</c> for a continent-continent collision (<see cref="IsCollision"/>)
/// or when the pair is not convergent. The caller MUST ensure this is one of
/// <paramref name="PlateA"/>/<paramref name="PlateB"/> for a non-collision convergent pair.</param>
/// <param name="IsCollision"><c>true</c> for a continent-continent collision (no subduction).</param>
public sealed record SlabJointPolarity(
    int PlateA,
    int PlateB,
    int? SubductingPlateId,
    bool IsCollision);

/// <summary>
/// One classified slab JOINT between two adjacent plates (assembled-world slice 2). One record per
/// adjacent plate pair that shares a boundary — the complete, deterministic joint list the
/// joint-mechanics geometry (sibling dispatch) consumes to express subduction underride, trenches,
/// ridges, and transform shear at the slab interfaces.
/// </summary>
/// <param name="PlateA">The lower plate id of the pair (<c>PlateA &lt; PlateB</c>).</param>
/// <param name="PlateB">The higher plate id of the pair.</param>
/// <param name="Kind">The motion-derived joint kind (from the existing boundary TYPE data source —
/// <see cref="PlateBoundaryArc.Kind"/>).</param>
/// <param name="SubductingPlateId">For <see cref="SlabJointKind.Convergent"/> non-collision joints:
/// the plate id that subducts (from the existing polarity data source). <c>null</c> for collision
/// joints and for non-convergent kinds.</param>
/// <param name="IsCollision">For convergent joints: <c>true</c> if this is a continent-continent
/// collision (no subduction). <c>false</c> otherwise.</param>
/// <param name="Path">The ordered unit-sphere points along the shared boundary between the two
/// plates, in a stable winding order. Points are the merged, deduplicated path of every
/// <see cref="PlateBoundaryArc"/> segment the pair shares, in arc-arrival order with overlapping
/// segment-junction endpoints collapsed. Every point lies on the shared edge frontier — each is a
/// corner of a cell of <see cref="PlateA"/> AND a cell of <see cref="PlateB"/>.</param>
public sealed record SlabJointClassification(
    int PlateA,
    int PlateB,
    SlabJointKind Kind,
    int? SubductingPlateId,
    bool IsCollision,
    IReadOnlyList<GlobeVec3> Path);

/// <summary>
/// Pure, Godot-free, contracts-tier classifier for the slab JOINTS of the assembled World (slice 2).
/// Consumes the SAME render-input data the slab assembly build's upstream seam already produces:
/// the typed <see cref="PlateBoundaryArc"/> set (boundary TYPE + ordered unit-sphere path points —
/// from <c>GlobeReconstructor.BuildBoundaryArcsAt</c>) and the per-convergent-pair
/// <see cref="SlabJointPolarity"/> (subduction polarity — a contracts-tier projection of
/// <c>ConvergentPolarity.Derive</c>'s output). No parallel data source is invented.
///
/// <para><b>Output.</b> A deterministic, complete list of <see cref="SlabJointClassification"/>
/// records — one per adjacent plate pair that shares a boundary. Each record carries the stable
/// (lower-id-first) plate pair, the joint kind, the subducting plate id for convergent non-collision
/// joints, and the merged+deduplicated ordered unit-sphere path along the shared boundary.</para>
///
/// <para><b>Determinism.</b> The joint SET is keyed by the unordered plate pair (stable lower-id
/// first). Within a pair, the path is the concatenation of the pair's arc point lists in
/// arc-arrival order, with consecutive segments' overlapping junction endpoints collapsed by
/// exact struct equality on <see cref="GlobeVec3"/>. Two calls with identical inputs produce
/// bit-identical output (same joint order, same per-joint fields, same path point order and
/// coordinates). Shuffled arc input yields the same per-pair joint SET with the same per-pair path.</para>
///
/// <para><b>Scope.</b> This classifier only CLASSIFIES joints — it does not compute joint-mechanics
/// geometry (trench depth, underride offset, ridge swell). That is the sibling dispatch's job; this
/// type hands it the complete, typed joint list to geometry over.</para>
/// </summary>
public static class SlabJointClassifier
{
    /// <summary>
    /// Classifies every slab joint between adjacent plates from the typed boundary arcs and the
    /// per-convergent-pair subduction polarity.
    /// </summary>
    /// <param name="arcs">All typed plate-boundary arcs at the snapshot tick — the existing source
    /// of boundary TYPE and ordered path points (from
    /// <c>GlobeReconstructor.BuildBoundaryArcsAt</c>). Arcs are grouped by unordered plate pair;
    /// every arc of a pair must carry the same <see cref="PlateBoundaryKind"/> (the topology
    /// classifier's per-pair kind).</param>
    /// <param name="polarity">Per-convergent-pair subduction polarity, keyed by the ordered pair
    /// <c>(min(PlateA,PlateB), max(PlateA,PlateB))</c> — a contracts-tier projection of
    /// <c>ConvergentPolarity.Derive</c>'s output. Entries for non-convergent pairs are ignored.</param>
    /// <returns>One <see cref="SlabJointClassification"/> per adjacent plate pair, in ascending
    /// <see cref="SlabJointClassification.PlateA"/> then <see cref="SlabJointClassification.PlateB"/>
    /// order. Each record's <see cref="SlabJointClassification.Path"/> is the merged+deduplicated
    /// ordered unit-sphere path along the pair's shared boundary.</returns>
    public static IReadOnlyList<SlabJointClassification> Classify(
        IReadOnlyList<PlateBoundaryArc> arcs,
        IReadOnlyDictionary<(int PlateA, int PlateB), SlabJointPolarity> polarity)
    {
        ArgumentNullException.ThrowIfNull(arcs);
        ArgumentNullException.ThrowIfNull(polarity);

        // Group arcs by unordered plate pair (stable lower-id first). A pair normally has multiple
        // arc segments (one per shared tessellation edge); they merge into ONE joint.
        var byPair = new SortedDictionary<(int Lo, int Hi), List<PlateBoundaryArc>>();
        for (int i = 0; i < arcs.Count; i++)
        {
            var arc = arcs[i];
            var key = OrderPair(arc.PlateA, arc.PlateB);
            if (!byPair.TryGetValue(key, out var list))
            {
                list = new List<PlateBoundaryArc>();
                byPair[key] = list;
            }
            list.Add(arc);
        }

        var result = new List<SlabJointClassification>(byPair.Count);
        foreach (var kvp in byPair)
        {
            var (lo, hi) = kvp.Key;
            var pairArcs = kvp.Value;

            // A pair's arcs CAN disagree (a long boundary can be transform in one section and
            // convergent in another). Resolve to the mechanically most expressive kind, matching
            // the engine's CellBoundaryField.KindPriority: Convergent > Divergent > Transform.
            var arcKind = pairArcs[0].Kind;
            for (int i = 1; i < pairArcs.Count; i++)
            {
                arcKind = HigherPriorityKind(arcKind, pairArcs[i].Kind);
            }
            var kind = MapKind(arcKind);

            // Polarity attaches only to convergent joints. For a non-collision convergent joint the
            // subducting plate id comes from the polarity input; for a collision it is null.
            int? subductingPlateId = null;
            bool isCollision = false;
            if (kind == SlabJointKind.Convergent && polarity.TryGetValue((lo, hi), out var pol))
            {
                isCollision = pol.IsCollision;
                subductingPlateId = isCollision ? null : pol.SubductingPlateId;

                if (!isCollision && subductingPlateId.HasValue)
                {
                    int s = subductingPlateId.Value;
                    if (s != lo && s != hi)
                    {
                        throw new InvalidOperationException(
                            $"Polarity for plate pair ({lo}, {hi}) names a subducting plate id {s} "
                            + "that is not one of the pair's plate ids. The polarity input is malformed.");
                    }
                }
            }

            var path = MergePath(pairArcs);

            result.Add(new SlabJointClassification(
                PlateA: lo,
                PlateB: hi,
                Kind: kind,
                SubductingPlateId: subductingPlateId,
                IsCollision: isCollision,
                Path: path));
        }
        return result;
    }

    /// <summary>
    /// Classifies slab joints reading subduction polarity directly from the boundary SECTION
    /// documents (contracts-tier; no plugins-tier polarity projection needed). Sections carry the
    /// engine-derived <c>SubductingPlateId</c>/<c>IsCollision</c> per convergent pair.
    /// </summary>
    public static IReadOnlyList<SlabJointClassification> Classify(
        IReadOnlyList<PlateBoundaryArc> arcs,
        IReadOnlyList<BoundarySectionDocument>? sections)
    {
        ArgumentNullException.ThrowIfNull(arcs);
        var polarity = new Dictionary<(int PlateA, int PlateB), SlabJointPolarity>();
        if (sections is not null)
        {
            foreach (var section in sections)
            {
                if (section.Kind != PlateBoundaryKind.Convergent) continue;
                var key = section.PlateA <= section.PlateB
                    ? (PlateA: section.PlateA, PlateB: section.PlateB)
                    : (PlateA: section.PlateB, PlateB: section.PlateA);
                // First section for a pair wins; later duplicates do not override (deterministic).
                if (!polarity.ContainsKey(key))
                {
                    polarity[key] = new SlabJointPolarity(
                        key.PlateA, key.PlateB,
                        section.IsCollision ? null : section.SubductingPlateId,
                        section.IsCollision);
                }
            }
        }
        return Classify(arcs, polarity);
    }

    // Matches CellBoundaryField.KindPriority: Convergent > Divergent > Transform > Inactive. A pair
    // whose arcs disagree keeps the mechanically more expressive kind at the joint.
    private static PlateBoundaryKind HigherPriorityKind(PlateBoundaryKind existing, PlateBoundaryKind candidate)
        => KindPriority(candidate) < KindPriority(existing) ? candidate : existing;

    private static int KindPriority(PlateBoundaryKind kind) => kind switch
    {
        PlateBoundaryKind.Convergent => 0,
        PlateBoundaryKind.Divergent => 1,
        PlateBoundaryKind.Transform => 2,
        _ => 3,
    };

    // Map the boundary-arc contract kind onto the joint kind enum. The two enums mirror each other
    // 1:1 today; this indirection keeps the joint surface self-contained so the joint-mechanics
    // geometry dispatch does not need to reference the boundary-arc contract to switch on kind.
    private static SlabJointKind MapKind(PlateBoundaryKind kind) => kind switch
    {
        PlateBoundaryKind.Convergent => SlabJointKind.Convergent,
        PlateBoundaryKind.Divergent => SlabJointKind.Divergent,
        PlateBoundaryKind.Transform => SlabJointKind.Transform,
        _ => SlabJointKind.Inactive,
    };

    private static (int Lo, int Hi) OrderPair(int a, int b)
        => a <= b ? (a, b) : (b, a);

    // Merge a pair's arc segments into ONE ordered, deduplicated path. Each arc's Points list is
    // endpoints-inclusive (the boundary sampler subdivides that way), so consecutive segments share
    // their junction endpoint; the shared point is emitted ONCE. The merge is in arc-arrival order
    // so the output is deterministic w.r.t. the input arc order. Exact struct equality on
    // GlobeVec3 (float X/Y/Z) dedupes — the boundary sampler emits identical float coordinates for
    // the same shared tessellation vertex, so exact equality is correct here.
    private static IReadOnlyList<GlobeVec3> MergePath(List<PlateBoundaryArc> pairArcs)
    {
        if (pairArcs.Count == 1)
            return pairArcs[0].Points;

        var merged = new List<GlobeVec3>();
        GlobeVec3? last = null;
        foreach (var arc in pairArcs)
        {
            var pts = arc.Points;
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                // Skip a point that exactly repeats the last emitted point — the shared junction
                // endpoint of two consecutive segments.
                if (last.HasValue && last.Value.Equals(p))
                    continue;
                merged.Add(p);
                last = p;
            }
        }
        return merged;
    }
}