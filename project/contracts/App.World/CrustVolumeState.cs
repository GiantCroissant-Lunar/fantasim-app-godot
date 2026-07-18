using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FantaSim.App.World.Dto;
using UnifyMaths;

namespace FantaSim.App.World;

/// <summary>
/// Immutable plate-owned crust material for one generated identity. The state stores compact
/// deformed control points, never voxels, render meshes, or view transforms.
/// </summary>
public sealed class CrustVolumeState
{
    public const string CurrentAlgorithmVersion = "crust-volume.v2";

    private const double SpatialEpsilon = 1e-8;
    private static readonly (int A, int B, int C)[] TetraFaces =
    {
        (0, 1, 2),
        (0, 1, 3),
        (0, 2, 3),
        (1, 2, 3),
    };
    private static readonly (int A, int B)[] TetraEdges =
    {
        (0, 1),
        (0, 2),
        (0, 3),
        (1, 2),
        (1, 3),
        (2, 3),
    };

    private readonly double[] _outerElevationsMetresByCell;
    private readonly double[] _crustThicknessMetresByCell;
    private readonly CellCrustFeature[] _featuresByCell;
    private readonly double[] _continentalFractionsByCell;
    private readonly GlobeVec3[] _outerPointsByCellCorner;
    private readonly GlobeVec3[] _innerPointsByCellCorner;
    private readonly ReadOnlyCollection<double> _outerElevationsView;
    private readonly ReadOnlyCollection<double> _crustThicknessView;
    private readonly ReadOnlyCollection<CellCrustFeature> _featuresView;
    private readonly ReadOnlyCollection<double> _continentalFractionsView;
    private readonly IReadOnlyDictionary<int, double> _continentalFractionByCell;
    private readonly IReadOnlyDictionary<int, int[]> _cellIdsByPlate;
    private readonly int _closedContacts;
    private readonly int _openContacts;

    private CrustVolumeState(
        long tick,
        int seed,
        int graphRevision,
        string topologyDigest,
        string deformationParameterDigest,
        WorldGlobeSnapshot globe,
        IReadOnlyList<PlateBoundaryArc> boundaryArcs,
        GlobeVec3[] outerPointsByCellCorner,
        GlobeVec3[] innerPointsByCellCorner,
        double[] outerElevationsMetresByCell,
        double[] crustThicknessMetresByCell,
        CellCrustFeature[] featuresByCell,
        double[] continentalFractionsByCell)
    {
        Tick = tick;
        Seed = seed;
        GraphRevision = graphRevision;
        TopologyDigest = topologyDigest;
        DeformationParameterDigest = deformationParameterDigest;
        Globe = globe;
        BoundaryArcs = Array.AsReadOnly(boundaryArcs.ToArray());
        _outerPointsByCellCorner = outerPointsByCellCorner;
        _innerPointsByCellCorner = innerPointsByCellCorner;
        _outerElevationsMetresByCell = outerElevationsMetresByCell;
        _crustThicknessMetresByCell = crustThicknessMetresByCell;
        _featuresByCell = featuresByCell;
        _continentalFractionsByCell = continentalFractionsByCell;
        _outerElevationsView = Array.AsReadOnly(_outerElevationsMetresByCell);
        _crustThicknessView = Array.AsReadOnly(_crustThicknessMetresByCell);
        _featuresView = Array.AsReadOnly(_featuresByCell);
        _continentalFractionsView = Array.AsReadOnly(_continentalFractionsByCell);
        _continentalFractionByCell = new ReadOnlyDictionary<int, double>(
            Enumerable.Range(0, continentalFractionsByCell.Length)
                .ToDictionary(cellId => cellId, cellId => continentalFractionsByCell[cellId]));
        _cellIdsByPlate = new ReadOnlyDictionary<int, int[]>(
            globe.Cells
                .GroupBy(cell => cell.PlateId)
                .OrderBy(group => group.Key)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(cell => cell.CellId).OrderBy(id => id).ToArray()));
        Digest = ComputeDigest();
        ValidateMaterialWedges();
        (_closedContacts, _openContacts) = ValidateClosedContacts();
    }

    public long Tick { get; }
    public int Seed { get; }
    public int GraphRevision { get; }
    public string TopologyDigest { get; }
    public string DeformationParameterDigest { get; }
    public string AlgorithmVersion => CurrentAlgorithmVersion;
    public string Digest { get; }
    public WorldGlobeSnapshot Globe { get; }
    public IReadOnlyList<PlateBoundaryArc> BoundaryArcs { get; }
    public IReadOnlyList<double> OuterElevationsMetresByCell => _outerElevationsView;
    public IReadOnlyList<double> CrustThicknessMetresByCell => _crustThicknessView;
    public IReadOnlyList<CellCrustFeature> FeaturesByCell => _featuresView;
    public IReadOnlyList<double> ContinentalFractionsByCell => _continentalFractionsView;
    public IReadOnlyDictionary<int, double> ContinentalFractionByCell => _continentalFractionByCell;
    public int CellCount => Globe.CellCount;

    /// <summary>
    /// Number of cross-plate outer-contact corners that passed the closed-envelope predicate
    /// (vault/specs/2026-07-17 §7.1). Populated at construction by <see cref="ValidateClosedContacts"/>.
    /// </summary>
    public int ClosedContacts => _closedContacts;

    /// <summary>
    /// Number of cross-plate outer-contact corners that FAILED the closed-envelope predicate.
    /// Always zero for a successfully constructed state — <see cref="ValidateClosedContacts"/>
    /// throws on the first open contact. Exposed for the construction log marker.
    /// </summary>
    public int OpenContacts => _openContacts;

    public static CrustVolumeState Create(
        long tick,
        int seed,
        int graphRevision,
        string topologyDigest,
        string deformationParameterDigest,
        WorldGlobeSnapshot globe,
        IReadOnlyList<PlateBoundaryArc> boundaryArcs,
        IReadOnlyList<GlobeVec3> outerPointsByCellCorner,
        IReadOnlyList<GlobeVec3> innerPointsByCellCorner,
        IReadOnlyList<double> outerElevationsMetresByCell,
        IReadOnlyList<double> crustThicknessMetresByCell,
        IReadOnlyList<CellCrustFeature> featuresByCell,
        IReadOnlyDictionary<int, double> continentalFractionByCell)
    {
        ArgumentNullException.ThrowIfNull(globe);
        ArgumentNullException.ThrowIfNull(boundaryArcs);
        ArgumentNullException.ThrowIfNull(outerPointsByCellCorner);
        ArgumentNullException.ThrowIfNull(innerPointsByCellCorner);
        ArgumentNullException.ThrowIfNull(outerElevationsMetresByCell);
        ArgumentNullException.ThrowIfNull(crustThicknessMetresByCell);
        ArgumentNullException.ThrowIfNull(featuresByCell);
        ArgumentNullException.ThrowIfNull(continentalFractionByCell);
        if (tick < 0)
            throw new ArgumentOutOfRangeException(nameof(tick));
        if (graphRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(graphRevision));
        if (string.IsNullOrWhiteSpace(topologyDigest))
            throw new ArgumentException("A topology digest is required.", nameof(topologyDigest));
        if (string.IsNullOrWhiteSpace(deformationParameterDigest))
            throw new ArgumentException(
                "A deformation parameter digest is required.",
                nameof(deformationParameterDigest));
        if (globe.Cells.Count != globe.CellCount)
            throw new ArgumentException(
                "Globe cell collection must match CellCount.",
                nameof(globe));
        if (outerPointsByCellCorner.Count != globe.CellCount * 3)
            throw new ArgumentException(
                "Outer material controls must contain three points per cell.",
                nameof(outerPointsByCellCorner));
        if (innerPointsByCellCorner.Count != globe.CellCount * 3)
            throw new ArgumentException(
                "Inner material controls must contain three points per cell.",
                nameof(innerPointsByCellCorner));
        if (outerElevationsMetresByCell.Count != globe.CellCount)
            throw new ArgumentException(
                "Outer elevations must contain one value per cell.",
                nameof(outerElevationsMetresByCell));
        if (crustThicknessMetresByCell.Count != globe.CellCount)
            throw new ArgumentException(
                "Crust thickness must contain one value per cell.",
                nameof(crustThicknessMetresByCell));
        if (featuresByCell.Count != globe.CellCount)
            throw new ArgumentException(
                "Crust features must contain one value per cell.",
                nameof(featuresByCell));

        var elevations = outerElevationsMetresByCell.ToArray();
        var thickness = crustThicknessMetresByCell.ToArray();
        var features = featuresByCell.ToArray();
        var fractions = new double[globe.CellCount];
        for (int cellId = 0; cellId < globe.CellCount; cellId++)
        {
            if (globe.Cells[cellId].CellId != cellId)
                throw new ArgumentException(
                    "Globe cells must be stored in CellId order.",
                    nameof(globe));
            if (!IsFinite(elevations[cellId]))
                throw new ArgumentOutOfRangeException(nameof(outerElevationsMetresByCell));
            if (!IsFinite(thickness[cellId]) || thickness[cellId] < 0.0)
                throw new ArgumentOutOfRangeException(nameof(crustThicknessMetresByCell));
            double fraction = continentalFractionByCell.TryGetValue(cellId, out double value)
                ? value
                : 0.0;
            if (!IsFinite(fraction) || fraction < 0.0 || fraction > 1.0)
                throw new ArgumentOutOfRangeException(nameof(continentalFractionByCell));
            fractions[cellId] = fraction;
        }

        foreach (var arc in boundaryArcs)
        {
            ArgumentNullException.ThrowIfNull(arc);
            if (arc.Kind == PlateBoundaryKind.Convergent)
            {
                if (arc.IsCollision && arc.SubductingPlateId is not null)
                    throw new ArgumentException(
                        "Collision arcs cannot carry a subducting plate.",
                        nameof(boundaryArcs));
                if (!arc.IsCollision
                    && arc.SubductingPlateId != arc.PlateA
                    && arc.SubductingPlateId != arc.PlateB)
                {
                    throw new ArgumentException(
                        "A subduction arc must name one plate from its pair.",
                        nameof(boundaryArcs));
                }
            }
            else if (arc.IsCollision || arc.SubductingPlateId is not null)
            {
                throw new ArgumentException(
                    "Non-convergent arcs cannot carry convergent mechanics.",
                    nameof(boundaryArcs));
            }
        }

        var outerPoints = outerPointsByCellCorner.ToArray();
        var innerPoints = innerPointsByCellCorner.ToArray();
        for (int i = 0; i < outerPoints.Length; i++)
        {
            if (!IsFinite(outerPoints[i]) || !IsFinite(innerPoints[i]))
                throw new ArgumentOutOfRangeException(
                    nameof(outerPointsByCellCorner),
                    "Every material control point must be finite.");
        }

        var state = new CrustVolumeState(
            tick,
            seed,
            graphRevision,
            topologyDigest,
            deformationParameterDigest,
            globe,
            boundaryArcs,
            outerPoints,
            innerPoints,
            elevations,
            thickness,
            features,
            fractions);
        return state;
    }

    public int PlateIdAtCell(int cellId)
    {
        ValidateCellId(cellId);
        return Globe.Cells[cellId].PlateId;
    }

    public GlobeVec3 OuterPointAtCellCorner(int cellId, int cornerIndex)
    {
        ValidateCellCorner(cellId, cornerIndex);
        return _outerPointsByCellCorner[(cellId * 3) + cornerIndex];
    }

    public GlobeVec3 InnerPointAtCellCorner(int cellId, int cornerIndex)
    {
        ValidateCellCorner(cellId, cornerIndex);
        return _innerPointsByCellCorner[(cellId * 3) + cornerIndex];
    }

    public GlobeVec3 MapMaterialPoint(
        int cellId,
        double weight0,
        double weight1,
        double weight2,
        double depthFraction)
    {
        ValidateCellId(cellId);
        if (!IsFinite(weight0) || !IsFinite(weight1) || !IsFinite(weight2))
            throw new ArgumentOutOfRangeException(nameof(weight0));
        if (Math.Abs((weight0 + weight1 + weight2) - 1.0) > 1e-8
            || weight0 < 0.0
            || weight1 < 0.0
            || weight2 < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(weight0),
                "Barycentric weights must be non-negative and sum to one.");
        }
        if (!IsFinite(depthFraction) || depthFraction < 0.0 || depthFraction > 1.0)
            throw new ArgumentOutOfRangeException(nameof(depthFraction));

        Span<int> order = stackalloc int[3] { 0, 1, 2 };
        GetCanonicalCornerOrder(cellId, order);
        Span<double> weights = stackalloc double[3] { weight0, weight1, weight2 };
        double w0 = weights[order[0]];
        double w1 = weights[order[1]];
        double w2 = weights[order[2]];
        Vector3D outer0 = ToVector(OuterPointAtCellCorner(cellId, order[0]));
        Vector3D outer1 = ToVector(OuterPointAtCellCorner(cellId, order[1]));
        Vector3D outer2 = ToVector(OuterPointAtCellCorner(cellId, order[2]));
        Vector3D inner0 = ToVector(InnerPointAtCellCorner(cellId, order[0]));
        Vector3D inner1 = ToVector(InnerPointAtCellCorner(cellId, order[1]));
        Vector3D inner2 = ToVector(InnerPointAtCellCorner(cellId, order[2]));

        Vector3D mapped;
        if (depthFraction <= w2)
        {
            mapped =
                (outer0 * w0)
              + (outer1 * w1)
              + (outer2 * (w2 - depthFraction))
              + (inner2 * depthFraction);
        }
        else if (depthFraction >= w1 + w2)
        {
            mapped =
                (outer0 * (1.0 - depthFraction))
              + (inner0 * (depthFraction - w1 - w2))
              + (inner1 * w1)
              + (inner2 * w2);
        }
        else
        {
            mapped =
                (outer0 * w0)
              + (outer1 * (w1 + w2 - depthFraction))
              + (inner1 * (depthFraction - w2))
              + (inner2 * w2);
        }
        return ToGlobe(mapped);
    }

    public bool ContainsWorldPoint(int plateId, GlobeVec3 point)
    {
        if (!_cellIdsByPlate.TryGetValue(plateId, out int[]? cellIds))
            return false;
        Vector3D p = ToVector(point);
        foreach (int cellId in cellIds)
        {
            GetWedge(cellId, out var o0, out var o1, out var o2, out var i0, out var i1, out var i2);
            if (PointInTetra(p, o0, o1, o2, i2)
                || PointInTetra(p, o0, o1, i1, i2)
                || PointInTetra(p, o0, i0, i1, i2))
            {
                return true;
            }
        }
        return false;
    }

    public IReadOnlyList<(int PlateId, double EnterDistance, double ExitDistance)> TraceRay(
        GlobeVec3 origin,
        GlobeVec3 direction,
        double maxDistance = 4.0)
    {
        ValidateRay(origin, direction, maxDistance);
        var cells = TraceCellIntervals(origin, direction, maxDistance);
        var raw = cells
            .Select(hit => (
                hit.PlateId,
                hit.EnterDistance,
                hit.ExitDistance))
            .OrderBy(hit => hit.PlateId)
            .ThenBy(hit => hit.EnterDistance)
            .ToArray();
        var merged = new List<(int PlateId, double EnterDistance, double ExitDistance)>();
        foreach (var current in raw)
        {
            if (merged.Count > 0
                && merged[^1].PlateId == current.PlateId
                && current.EnterDistance <= merged[^1].ExitDistance + SpatialEpsilon)
            {
                var previous = merged[^1];
                merged[^1] = (
                    previous.PlateId,
                    previous.EnterDistance,
                    Math.Max(previous.ExitDistance, current.ExitDistance));
            }
            else
            {
                merged.Add(current);
            }
        }
        merged.Sort(static (left, right) =>
        {
            int byEnter = left.EnterDistance.CompareTo(right.EnterDistance);
            return byEnter != 0 ? byEnter : left.PlateId.CompareTo(right.PlateId);
        });
        return merged;
    }

    public bool TryGetOutermostInterval(
        GlobeVec3 origin,
        GlobeVec3 direction,
        out (int PlateId, double EnterDistance, double ExitDistance) interval)
    {
        var intervals = TraceRay(origin, direction);
        if (intervals.Count == 0)
        {
            interval = default;
            return false;
        }
        interval = intervals[0];
        return true;
    }

    public (GlobeVec3 Min, GlobeVec3 Max) CellBounds(int cellId)
    {
        ValidateCellId(cellId);
        return BoundsForCells(new[] { cellId });
    }

    public (GlobeVec3 Min, GlobeVec3 Max) PlateBounds(int plateId)
    {
        if (!_cellIdsByPlate.TryGetValue(plateId, out int[]? cellIds))
            throw new ArgumentOutOfRangeException(nameof(plateId));
        return BoundsForCells(cellIds);
    }

    public bool TryFindConvergentUnderlapProof(
        out (
            int BoundaryArcIndex,
            int OverridingPlateId,
            int SubductingPlateId,
            int SubductingCellId,
            GlobeVec3 RayOrigin,
            GlobeVec3 RayDirection,
            double OverridingEnter,
            double OverridingExit,
            double SubductingEnter,
            double SubductingExit) proof)
    {
        proof = default;
        const double candidateHalfWidthRad = 0.30;
        const double nearSideMaxDistance = 1.20;
        double minimumArcDot = Math.Cos(candidateHalfWidthRad);
        for (int arcIndex = 0; arcIndex < BoundaryArcs.Count; arcIndex++)
        {
            var arc = BoundaryArcs[arcIndex];
            if (arc.Kind != PlateBoundaryKind.Convergent
                || arc.IsCollision
                || arc.SubductingPlateId is not int subductingPlateId)
            {
                continue;
            }

            int overridingPlateId =
                arc.PlateA == subductingPlateId ? arc.PlateB : arc.PlateA;
            foreach (var cell in Globe.Cells
                         .Where(cell => cell.PlateId == overridingPlateId)
                         .OrderBy(cell => cell.CellId))
            {
                Vector3D centre = (
                    ToVector(cell.C0) + ToVector(cell.C1) + ToVector(cell.C2)
                ).Normalize();
                double nearestArcDot = -1.0;
                foreach (var point in arc.Points)
                {
                    nearestArcDot = Math.Max(
                        nearestArcDot,
                        Vector3D.Dot(centre, ToVector(point).Normalize()));
                }
                if (nearestArcDot < minimumArcDot)
                    continue;

                var origin = ToGlobe(centre * 1.75);
                var direction = ToGlobe(centre * -1.0);
                var intervals = TraceCellIntervals(origin, direction, nearSideMaxDistance);
                for (int first = 0; first < intervals.Count; first++)
                {
                    var overriding = intervals[first];
                    if (overriding.PlateId != overridingPlateId)
                        continue;
                    for (int second = first + 1; second < intervals.Count; second++)
                    {
                        var downGoing = intervals[second];
                        if (downGoing.PlateId != subductingPlateId
                            || !CellTouchesArc(Globe.Cells[downGoing.CellId], arc)
                            || downGoing.EnterDistance
                                <= overriding.ExitDistance + SpatialEpsilon)
                        {
                            continue;
                        }

                        proof = (
                            arcIndex,
                            overridingPlateId,
                            subductingPlateId,
                            downGoing.CellId,
                            origin,
                            direction,
                            overriding.EnterDistance,
                            overriding.ExitDistance,
                            downGoing.EnterDistance,
                            downGoing.ExitDistance);
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private IReadOnlyList<(
        int CellId,
        int PlateId,
        double EnterDistance,
        double ExitDistance)> TraceCellIntervals(
            GlobeVec3 origin,
            GlobeVec3 direction,
            double maxDistance)
    {
        ValidateRay(origin, direction, maxDistance);
        Vector3D rayOrigin = ToVector(origin);
        Vector3D rayDirection = ToVector(direction).Normalize();
        var raw = new List<(
            int CellId,
            int PlateId,
            double EnterDistance,
            double ExitDistance)>();
        foreach (var cell in Globe.Cells)
        {
            GetWedge(cell.CellId, out var o0, out var o1, out var o2, out var i0, out var i1, out var i2);
            Add(o0, o1, o2, i2);
            Add(o0, o1, i1, i2);
            Add(o0, i0, i1, i2);

            void Add(Vector3D a, Vector3D b, Vector3D c, Vector3D d)
            {
                if (TryTraceTetra(
                    rayOrigin,
                    rayDirection,
                    maxDistance,
                    a,
                    b,
                    c,
                    d,
                    out double enter,
                    out double exit))
                {
                    raw.Add((cell.CellId, cell.PlateId, enter, exit));
                }
            }
        }

        raw.Sort(static (left, right) =>
        {
            int byCell = left.CellId.CompareTo(right.CellId);
            return byCell != 0
                ? byCell
                : left.EnterDistance.CompareTo(right.EnterDistance);
        });
        var merged = new List<(
            int CellId,
            int PlateId,
            double EnterDistance,
            double ExitDistance)>();
        foreach (var current in raw)
        {
            if (merged.Count > 0
                && merged[^1].CellId == current.CellId
                && current.EnterDistance <= merged[^1].ExitDistance + SpatialEpsilon)
            {
                var previous = merged[^1];
                merged[^1] = (
                    previous.CellId,
                    previous.PlateId,
                    previous.EnterDistance,
                    Math.Max(previous.ExitDistance, current.ExitDistance));
            }
            else
            {
                merged.Add(current);
            }
        }
        merged.Sort(static (left, right) =>
            left.EnterDistance.CompareTo(right.EnterDistance));
        return merged;
    }

    private void ValidateMaterialWedges()
    {
        foreach (var cell in Globe.Cells)
        {
            GetWedge(cell.CellId, out var o0, out var o1, out var o2, out var i0, out var i1, out var i2);
            var order = new[] { 0, 1, 2 };
            GetCanonicalCornerOrder(cell.CellId, order);
            Vector3D ro0 = ToVector(OriginalCorner(cell, order[0])).Normalize();
            Vector3D ro1 = ToVector(OriginalCorner(cell, order[1])).Normalize();
            Vector3D ro2 = ToVector(OriginalCorner(cell, order[2])).Normalize();
            Vector3D ri0 = ro0 * 0.9;
            Vector3D ri1 = ro1 * 0.9;
            Vector3D ri2 = ro2 * 0.9;

            ValidateTetraOrientation(cell.CellId, 0, o0, o1, o2, i2, ro0, ro1, ro2, ri2);
            ValidateTetraOrientation(cell.CellId, 1, o0, o1, i1, i2, ro0, ro1, ri1, ri2);
            ValidateTetraOrientation(cell.CellId, 2, o0, i0, i1, i2, ro0, ri0, ri1, ri2);
            ValidateOppositeSides(cell.CellId, "T0/T1", o0, o1, i2, o2, i1);
            ValidateOppositeSides(cell.CellId, "T1/T2", o0, i1, i2, o1, i0);
            if (TetrahedraHaveInteriorOverlap(
                new[] { o0, o1, o2, i2 },
                new[] { o0, i0, i1, i2 }))
            {
                throw new ArgumentException(
                    $"Cell {cell.CellId} has non-injective T0/T2 material overlap.");
            }
        }
    }

    // V1 closed-contacts predicate B (vault/specs/2026-07-17 §7.1, §11). Defense-in-depth on top of
    // GlobePlateSurfaces.ValidateClosedOuterContacts: the weld already enforces exact cross-plate
    // equality at every shared corner, but that predicate lives in the rendering contract and could
    // be bypassed by a future caller that builds a CrustVolumeState without going through the weld.
    // This state-side predicate walks every convergent arc's shared corners directly off the stored
    // controls and confirms the outer envelope is closed (both plates write the same outer point at
    // a shared hinge). Returns (closedCount, openCount); throws on the first open contact so the
    // construction log marker can distinguish "passed" from "passed with N open contacts".
    private (int ClosedContacts, int OpenContacts) ValidateClosedContacts()
    {
        const double contactEpsilon = 1e-6;
        int closed = 0;
        int open = 0;
        foreach (var arc in BoundaryArcs)
        {
            if (arc.Kind != PlateBoundaryKind.Convergent
                || arc.IsCollision
                || arc.SubductingPlateId is not int subductingPlateId)
            {
                continue;
            }

            int overridingPlateId =
                arc.PlateA == subductingPlateId ? arc.PlateB : arc.PlateA;
            if (arc.Points.Count < 2)
                continue;

            // For every cell pair straddling the arc at a shared corner, compare the two plates'
            // outer controls at that corner. A shared corner is a corner whose original unit
            // direction matches an arc endpoint pair (CellTouchesArc semantics).
            foreach (var subductingCell in Globe.Cells
                         .Where(c => c.PlateId == subductingPlateId)
                         .OrderBy(c => c.CellId))
            {
                if (!CellTouchesArc(subductingCell, arc))
                    continue;

                foreach (var overridingCell in Globe.Cells
                             .Where(c => c.PlateId == overridingPlateId)
                             .OrderBy(c => c.CellId))
                {
                    if (!CellTouchesArc(overridingCell, arc))
                        continue;

                    for (int sCorner = 0; sCorner < 3; sCorner++)
                    {
                        var sOriginal = OriginalCorner(subductingCell, sCorner);
                        for (int oCorner = 0; oCorner < 3; oCorner++)
                        {
                            var oOriginal = OriginalCorner(overridingCell, oCorner);
                            if (!SameDirection(sOriginal, oOriginal))
                                continue;

                            Vector3D sOuter = ToVector(
                                OuterPointAtCellCorner(subductingCell.CellId, sCorner));
                            Vector3D oOuter = ToVector(
                                OuterPointAtCellCorner(overridingCell.CellId, oCorner));
                            if ((sOuter - oOuter).Length() <= contactEpsilon)
                            {
                                closed++;
                            }
                            else
                            {
                                open++;
                                throw new ArgumentException(
                                    $"Outer contact at shared corner of cells "
                                  + $"{subductingCell.CellId}/{overridingCell.CellId} on convergent "
                                  + $"arc {arc.PlateA}-{arc.PlateB} is open: "
                                  + $"{(sOuter - oOuter).Length():G6} > {contactEpsilon:G6}.");
                            }
                        }
                    }
                }
            }
        }
        return (closed, open);
    }

    private static void ValidateTetraOrientation(
        int cellId,
        int tetraIndex,
        Vector3D a,
        Vector3D b,
        Vector3D c,
        Vector3D d,
        Vector3D referenceA,
        Vector3D referenceB,
        Vector3D referenceC,
        Vector3D referenceD)
    {
        double actual = SixVolume(a, b, c, d);
        double reference = SixVolume(referenceA, referenceB, referenceC, referenceD);
        if (Math.Abs(actual) < SpatialEpsilon
            || Math.Abs(reference) < SpatialEpsilon
            || Math.Sign(actual) != Math.Sign(reference))
        {
            throw new ArgumentException(
                $"Cell {cellId} tetrahedron {tetraIndex} is degenerate or inverted.");
        }
    }

    private static void ValidateOppositeSides(
        int cellId,
        string sharedFace,
        Vector3D a,
        Vector3D b,
        Vector3D c,
        Vector3D leftOpposite,
        Vector3D rightOpposite)
    {
        Vector3D normal = Vector3D.Cross(b - a, c - a);
        double left = Vector3D.Dot(leftOpposite - a, normal);
        double right = Vector3D.Dot(rightOpposite - a, normal);
        if (Math.Abs(left) < SpatialEpsilon
            || Math.Abs(right) < SpatialEpsilon
            || Math.Sign(left) == Math.Sign(right))
        {
            throw new ArgumentException(
                $"Cell {cellId} folds across shared material face {sharedFace}.");
        }
    }

    private static bool TetrahedraHaveInteriorOverlap(
        IReadOnlyList<Vector3D> left,
        IReadOnlyList<Vector3D> right)
    {
        foreach (var face in TetraFaces)
        {
            if (IsSeparatingAxis(
                    Vector3D.Cross(
                        left[face.B] - left[face.A],
                        left[face.C] - left[face.A]),
                    left,
                    right)
                || IsSeparatingAxis(
                    Vector3D.Cross(
                        right[face.B] - right[face.A],
                        right[face.C] - right[face.A]),
                    left,
                    right))
            {
                return false;
            }
        }

        foreach (var leftEdge in TetraEdges)
        {
            Vector3D leftDirection = left[leftEdge.B] - left[leftEdge.A];
            foreach (var rightEdge in TetraEdges)
            {
                if (IsSeparatingAxis(
                    Vector3D.Cross(
                        leftDirection,
                        right[rightEdge.B] - right[rightEdge.A]),
                    left,
                    right))
                {
                    return false;
                }
            }
        }
        return true;
    }

    private static bool IsSeparatingAxis(
        Vector3D axis,
        IReadOnlyList<Vector3D> left,
        IReadOnlyList<Vector3D> right)
    {
        if (axis.Length() < SpatialEpsilon)
            return false;
        double leftMin = double.PositiveInfinity;
        double leftMax = double.NegativeInfinity;
        double rightMin = double.PositiveInfinity;
        double rightMax = double.NegativeInfinity;
        for (int i = 0; i < 4; i++)
        {
            double leftProjection = Vector3D.Dot(left[i], axis);
            double rightProjection = Vector3D.Dot(right[i], axis);
            leftMin = Math.Min(leftMin, leftProjection);
            leftMax = Math.Max(leftMax, leftProjection);
            rightMin = Math.Min(rightMin, rightProjection);
            rightMax = Math.Max(rightMax, rightProjection);
        }
        return leftMax <= rightMin + SpatialEpsilon
            || rightMax <= leftMin + SpatialEpsilon;
    }

    private static double SixVolume(
        Vector3D a,
        Vector3D b,
        Vector3D c,
        Vector3D d)
        => Vector3D.Dot(b - a, Vector3D.Cross(c - a, d - a));

    private static bool PointInTetra(
        Vector3D point,
        Vector3D v0,
        Vector3D v1,
        Vector3D v2,
        Vector3D v3)
    {
        if (!TryBarycentric(point - v0, v1 - v0, v2 - v0, v3 - v0, out var b))
            return false;
        double w0 = 1.0 - b.X - b.Y - b.Z;
        return w0 >= -SpatialEpsilon
            && b.X >= -SpatialEpsilon
            && b.Y >= -SpatialEpsilon
            && b.Z >= -SpatialEpsilon;
    }

    private static bool TryTraceTetra(
        Vector3D origin,
        Vector3D direction,
        double maxDistance,
        Vector3D v0,
        Vector3D v1,
        Vector3D v2,
        Vector3D v3,
        out double enter,
        out double exit)
    {
        enter = 0.0;
        exit = maxDistance;
        if (!TryBarycentric(
            origin - v0,
            v1 - v0,
            v2 - v0,
            v3 - v0,
            out var atOrigin)
            || !TryBarycentric(
                direction,
                v1 - v0,
                v2 - v0,
                v3 - v0,
                out var alongRay))
        {
            return false;
        }

        Span<double> intercept = stackalloc double[4]
        {
            1.0 - atOrigin.X - atOrigin.Y - atOrigin.Z,
            atOrigin.X,
            atOrigin.Y,
            atOrigin.Z,
        };
        Span<double> slope = stackalloc double[4]
        {
            -alongRay.X - alongRay.Y - alongRay.Z,
            alongRay.X,
            alongRay.Y,
            alongRay.Z,
        };
        for (int i = 0; i < 4; i++)
        {
            if (Math.Abs(slope[i]) < SpatialEpsilon)
            {
                if (intercept[i] < -SpatialEpsilon)
                    return false;
                continue;
            }

            double crossing = (-SpatialEpsilon - intercept[i]) / slope[i];
            if (slope[i] > 0.0)
                enter = Math.Max(enter, crossing);
            else
                exit = Math.Min(exit, crossing);
            if (enter > exit)
                return false;
        }
        return exit >= 0.0 && enter <= maxDistance;
    }

    private static bool TryBarycentric(
        Vector3D rightHandSide,
        Vector3D column0,
        Vector3D column1,
        Vector3D column2,
        out (double X, double Y, double Z) value)
    {
        double determinant = Vector3D.Dot(column0, Vector3D.Cross(column1, column2));
        if (Math.Abs(determinant) < SpatialEpsilon)
        {
            value = default;
            return false;
        }
        value = (
            Vector3D.Dot(rightHandSide, Vector3D.Cross(column1, column2)) / determinant,
            Vector3D.Dot(column0, Vector3D.Cross(rightHandSide, column2)) / determinant,
            Vector3D.Dot(column0, Vector3D.Cross(column1, rightHandSide)) / determinant);
        return true;
    }

    private (GlobeVec3 Min, GlobeVec3 Max) BoundsForCells(IReadOnlyList<int> cellIds)
    {
        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;
        double maxZ = double.NegativeInfinity;
        foreach (int cellId in cellIds)
        {
            for (int corner = 0; corner < 3; corner++)
            {
                Accumulate(OuterPointAtCellCorner(cellId, corner));
                Accumulate(InnerPointAtCellCorner(cellId, corner));
            }
        }
        return (
            new GlobeVec3((float)minX, (float)minY, (float)minZ),
            new GlobeVec3((float)maxX, (float)maxY, (float)maxZ));

        void Accumulate(GlobeVec3 point)
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            minZ = Math.Min(minZ, point.Z);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
            maxZ = Math.Max(maxZ, point.Z);
        }
    }

    private void GetWedge(
        int cellId,
        out Vector3D outer0,
        out Vector3D outer1,
        out Vector3D outer2,
        out Vector3D inner0,
        out Vector3D inner1,
        out Vector3D inner2)
    {
        Span<int> order = stackalloc int[3] { 0, 1, 2 };
        GetCanonicalCornerOrder(cellId, order);
        outer0 = ToVector(OuterPointAtCellCorner(cellId, order[0]));
        outer1 = ToVector(OuterPointAtCellCorner(cellId, order[1]));
        outer2 = ToVector(OuterPointAtCellCorner(cellId, order[2]));
        inner0 = ToVector(InnerPointAtCellCorner(cellId, order[0]));
        inner1 = ToVector(InnerPointAtCellCorner(cellId, order[1]));
        inner2 = ToVector(InnerPointAtCellCorner(cellId, order[2]));
    }

    private void GetCanonicalCornerOrder(int cellId, Span<int> order)
    {
        var cell = Globe.Cells[cellId];
        for (int i = 1; i < order.Length; i++)
        {
            int value = order[i];
            int j = i - 1;
            while (j >= 0
                   && CompareOriginalCorner(
                       OriginalCorner(cell, value),
                       OriginalCorner(cell, order[j])) < 0)
            {
                order[j + 1] = order[j];
                j--;
            }
            order[j + 1] = value;
        }
    }

    private static GlobeVec3 OriginalCorner(GlobeCell cell, int cornerIndex)
        => cornerIndex switch
        {
            0 => cell.C0,
            1 => cell.C1,
            _ => cell.C2,
        };

    private static int CompareOriginalCorner(GlobeVec3 left, GlobeVec3 right)
    {
        int byX = left.X.CompareTo(right.X);
        if (byX != 0)
            return byX;
        int byY = left.Y.CompareTo(right.Y);
        return byY != 0 ? byY : left.Z.CompareTo(right.Z);
    }

    private static bool CellTouchesArc(GlobeCell cell, PlateBoundaryArc arc)
    {
        if (arc.Points.Count < 2)
            return false;
        Span<GlobeVec3> corners = stackalloc GlobeVec3[3]
        {
            cell.C0,
            cell.C1,
            cell.C2,
        };
        GlobeVec3 first = arc.Points[0];
        GlobeVec3 last = arc.Points[^1];
        for (int edge = 0; edge < 3; edge++)
        {
            GlobeVec3 a = corners[edge];
            GlobeVec3 b = corners[(edge + 1) % 3];
            if ((SameDirection(a, first) && SameDirection(b, last))
                || (SameDirection(a, last) && SameDirection(b, first)))
            {
                return true;
            }
        }
        return false;
    }

    private static bool SameDirection(GlobeVec3 left, GlobeVec3 right)
    {
        Vector3D a = ToVector(left).Normalize();
        Vector3D b = ToVector(right).Normalize();
        return Vector3D.Dot(a, b) >= 1.0 - 1e-10;
    }

    private string ComputeDigest()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(CurrentAlgorithmVersion);
            writer.Write(Tick);
            writer.Write(Seed);
            writer.Write(GraphRevision);
            writer.Write(TopologyDigest);
            writer.Write(DeformationParameterDigest);
            writer.Write(Globe.Frequency);
            writer.Write(Globe.CellCount);
            writer.Write(Globe.PlateCount);
            writer.Write(Globe.TicksPerAnchor);
            foreach (var cell in Globe.Cells)
            {
                writer.Write(cell.CellId);
                writer.Write(cell.PlateId);
                Write(writer, cell.C0);
                Write(writer, cell.C1);
                Write(writer, cell.C2);
            }
            foreach (var plate in Globe.Plates)
            {
                writer.Write(plate.PlateId);
                Write(writer, plate.Axis);
                writer.Write(plate.RatePerTick);
            }
            writer.Write(BoundaryArcs.Count);
            foreach (var arc in BoundaryArcs)
            {
                writer.Write(arc.PlateA);
                writer.Write(arc.PlateB);
                writer.Write((int)arc.Kind);
                writer.Write(arc.SubductingPlateId ?? int.MinValue);
                writer.Write(arc.IsCollision);
                writer.Write(arc.Points.Count);
                foreach (var point in arc.Points)
                    Write(writer, point);
            }
            for (int cellId = 0; cellId < CellCount; cellId++)
            {
                writer.Write(_outerElevationsMetresByCell[cellId]);
                writer.Write(_crustThicknessMetresByCell[cellId]);
                writer.Write(_featuresByCell[cellId].Kind);
                writer.Write(_featuresByCell[cellId].Magnitude);
                writer.Write(_continentalFractionsByCell[cellId]);
                for (int corner = 0; corner < 3; corner++)
                {
                    Write(writer, OuterPointAtCellCorner(cellId, corner));
                    Write(writer, InnerPointAtCellCorner(cellId, corner));
                }
            }
        }
        return Convert.ToHexString(
                SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))))
            .ToLowerInvariant();
    }

    private static void Write(BinaryWriter writer, GlobeVec3 point)
    {
        writer.Write(point.X);
        writer.Write(point.Y);
        writer.Write(point.Z);
    }

    private static void ValidateRay(
        GlobeVec3 origin,
        GlobeVec3 direction,
        double maxDistance)
    {
        if (!IsFinite(origin) || !IsFinite(direction))
            throw new ArgumentOutOfRangeException(nameof(origin));
        if (!IsFinite(maxDistance) || maxDistance <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(maxDistance));
        if (ToVector(direction).Length() < SpatialEpsilon)
            throw new ArgumentOutOfRangeException(nameof(direction));
    }

    private void ValidateCellCorner(int cellId, int cornerIndex)
    {
        ValidateCellId(cellId);
        if ((uint)cornerIndex >= 3u)
            throw new ArgumentOutOfRangeException(nameof(cornerIndex));
    }

    private void ValidateCellId(int cellId)
    {
        if ((uint)cellId >= (uint)CellCount)
            throw new ArgumentOutOfRangeException(nameof(cellId));
    }

    private static Vector3D ToVector(GlobeVec3 point)
        => new(point.X, point.Y, point.Z);

    private static GlobeVec3 ToGlobe(Vector3D point)
        => new((float)point.X, (float)point.Y, (float)point.Z);

    private static bool IsFinite(GlobeVec3 point)
        => IsFinite(point.X) && IsFinite(point.Y) && IsFinite(point.Z);

    private static bool IsFinite(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);
}
