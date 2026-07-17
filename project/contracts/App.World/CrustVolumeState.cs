using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FantaSim.App.World.Dto;

namespace FantaSim.App.World;

/// <summary>
/// Immutable, compact plate-owned crust volume definition for one materialized tick. This is the
/// single geological input shared by assembled outer-envelope rendering and exploded/cutaway volume
/// extraction. It stores no voxel grid and no render mesh.
/// </summary>
public sealed class CrustVolumeState
{
    public const string CurrentAlgorithmVersion = "crust-volume.v1";

    private readonly double[] _outerElevationsMetresByCell;
    private readonly double[] _crustThicknessMetresByCell;
    private readonly CellCrustFeature[] _featuresByCell;
    private readonly double[] _continentalFractionsByCell;
    private readonly ReadOnlyCollection<double> _outerElevationsView;
    private readonly ReadOnlyCollection<double> _crustThicknessView;
    private readonly ReadOnlyCollection<CellCrustFeature> _featuresView;
    private readonly ReadOnlyCollection<double> _continentalFractionsView;
    private readonly IReadOnlyDictionary<int, double> _continentalFractionByCell;

    private CrustVolumeState(
        long tick,
        WorldGlobeSnapshot globe,
        IReadOnlyList<PlateBoundaryArc> boundaryArcs,
        double[] outerElevationsMetresByCell,
        double[] crustThicknessMetresByCell,
        CellCrustFeature[] featuresByCell,
        double[] continentalFractionsByCell)
    {
        Tick = tick;
        Globe = globe;
        BoundaryArcs = Array.AsReadOnly(boundaryArcs.ToArray());
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
        Digest = ComputeDigest();
    }

    public long Tick { get; }

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
    /// Creates the canonical compact state. Production construction belongs to
    /// <c>WorldCrustMaterialization.BuildVolumeState</c>; this contract factory owns validation and
    /// deterministic identity.
    /// </summary>
    public static CrustVolumeState Create(
        long tick,
        WorldGlobeSnapshot globe,
        IReadOnlyList<PlateBoundaryArc> boundaryArcs,
        IReadOnlyList<double> outerElevationsMetresByCell,
        IReadOnlyList<double> crustThicknessMetresByCell,
        IReadOnlyList<CellCrustFeature> featuresByCell,
        IReadOnlyDictionary<int, double> continentalFractionByCell)
    {
        ArgumentNullException.ThrowIfNull(globe);
        ArgumentNullException.ThrowIfNull(boundaryArcs);
        ArgumentNullException.ThrowIfNull(outerElevationsMetresByCell);
        ArgumentNullException.ThrowIfNull(crustThicknessMetresByCell);
        ArgumentNullException.ThrowIfNull(featuresByCell);
        ArgumentNullException.ThrowIfNull(continentalFractionByCell);
        if (tick < 0)
            throw new ArgumentOutOfRangeException(nameof(tick), tick, "Crust volume tick cannot be negative.");
        if (globe.Cells.Count != globe.CellCount)
            throw new ArgumentException("Globe cell collection must match CellCount.", nameof(globe));
        if (outerElevationsMetresByCell.Count != globe.CellCount)
            throw new ArgumentException("Outer elevations must contain exactly one value per globe cell.", nameof(outerElevationsMetresByCell));
        if (crustThicknessMetresByCell.Count != globe.CellCount)
            throw new ArgumentException("Crust thickness must contain exactly one value per globe cell.", nameof(crustThicknessMetresByCell));
        if (featuresByCell.Count != globe.CellCount)
            throw new ArgumentException("Crust features must contain exactly one value per globe cell.", nameof(featuresByCell));

        var elevations = outerElevationsMetresByCell.ToArray();
        var thickness = crustThicknessMetresByCell.ToArray();
        var features = featuresByCell.ToArray();
        var fractions = new double[globe.CellCount];

        for (int cellId = 0; cellId < globe.CellCount; cellId++)
        {
            if (!IsFinite(elevations[cellId]))
                throw new ArgumentOutOfRangeException(nameof(outerElevationsMetresByCell), "Outer elevations must be finite.");
            if (!IsFinite(thickness[cellId]) || thickness[cellId] < 0.0)
                throw new ArgumentOutOfRangeException(nameof(crustThicknessMetresByCell), "Crust thickness must be finite and non-negative.");

            double fraction = continentalFractionByCell.TryGetValue(cellId, out var value)
                ? value
                : 0.0;
            if (!IsFinite(fraction) || fraction < 0.0 || fraction > 1.0)
                throw new ArgumentOutOfRangeException(nameof(continentalFractionByCell), "Continental fractions must be finite values in [0,1].");
            fractions[cellId] = fraction;
        }

        foreach (var arc in boundaryArcs)
        {
            ArgumentNullException.ThrowIfNull(arc);
            if (arc.Kind == PlateBoundaryKind.Convergent)
            {
                if (arc.IsCollision && arc.SubductingPlateId is not null)
                    throw new ArgumentException("Collision arcs cannot carry a subducting plate.", nameof(boundaryArcs));
                if (!arc.IsCollision
                    && arc.SubductingPlateId != arc.PlateA
                    && arc.SubductingPlateId != arc.PlateB)
                {
                    throw new ArgumentException(
                        "Every subduction arc must name one plate from its own pair as subducting.",
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

        return new CrustVolumeState(
            tick,
            globe,
            boundaryArcs,
            elevations,
            thickness,
            features,
            fractions);
    }

    public int PlateIdAtCell(int cellId)
    {
        ValidateCellId(cellId);
        return Globe.Cells[cellId].PlateId;
    }

    public double OuterRadiusMetresAtCell(int cellId, double planetRadiusMetres)
    {
        ValidateRadius(planetRadiusMetres);
        ValidateCellId(cellId);
        return planetRadiusMetres + _outerElevationsMetresByCell[cellId];
    }

    public double InnerRadiusMetresAtCell(int cellId, double planetRadiusMetres)
        => OuterRadiusMetresAtCell(cellId, planetRadiusMetres) - _crustThicknessMetresByCell[cellId];

    /// <summary>
    /// Signed radial density for the plate-owned cell column: positive inside the crust volume,
    /// zero on its outer/inner surface, negative outside. Boundary-local bending replaces this radial
    /// baseline in later slices without changing the state authority.
    /// </summary>
    public double SignedDensityAtCellRadius(
        int cellId,
        double radiusMetres,
        double planetRadiusMetres)
    {
        if (!IsFinite(radiusMetres) || radiusMetres < 0.0)
            throw new ArgumentOutOfRangeException(nameof(radiusMetres), radiusMetres, "Sample radius must be finite and non-negative.");

        double outer = OuterRadiusMetresAtCell(cellId, planetRadiusMetres);
        double inner = outer - _crustThicknessMetresByCell[cellId];
        return Math.Min(radiusMetres - inner, outer - radiusMetres);
    }

    private string ComputeDigest()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(CurrentAlgorithmVersion);
            writer.Write(Tick);
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
            }
        }

        return Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))))
            .ToLowerInvariant();
    }

    private static void Write(BinaryWriter writer, GlobeVec3 point)
    {
        writer.Write(point.X);
        writer.Write(point.Y);
        writer.Write(point.Z);
    }

    private void ValidateCellId(int cellId)
    {
        if ((uint)cellId >= (uint)CellCount)
            throw new ArgumentOutOfRangeException(nameof(cellId), cellId, "Cell id is outside the crust volume state.");
    }

    private static void ValidateRadius(double planetRadiusMetres)
    {
        if (!IsFinite(planetRadiusMetres) || planetRadiusMetres <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(planetRadiusMetres), planetRadiusMetres, "Planet radius must be positive and finite.");
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
