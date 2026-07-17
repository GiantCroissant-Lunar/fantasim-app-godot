using System.Collections.Generic;
using FantaSim.App.World.Dto;
using FantaSim.Geosphere.Crust;
using UnifyCell;
using UnifyGeometry.Spherical;

namespace FantaSim.App.World.Topography;

/// <summary>
/// Single composition point for the per-cell boundary-profile elevation contribution (P4). Wires together
/// <see cref="ConvergentPolarity.Attach"/> (subduction mechanics on canonical arcs) →
/// <see cref="CellBoundaryField.Build"/> (per-cell nearest-boundary field) →
/// <see cref="BoundaryProfileShape.Contribution"/> (the profile shape). The returned array is added on top of
/// <c>CellElevationSystem.Derive</c> in the crust-surface-data path. Used by both the Service and the
/// Godot-free integration tests.
/// </summary>
public static class BoundaryProfileContribution
{
    /// <summary>
    /// The per-cell boundary-profile elevation contribution (length = <paramref name="globe"/>.CellCount).
    /// Zero for every cell when <paramref name="parameters"/> has zeroed amplitudes
    /// (<see cref="BoundaryProfileParameters.Zero"/>), so the contribution is purely additive.
    /// </summary>
    public static double[] Build(
        WorldGlobeSnapshot globe,
        IReadOnlyList<PlateBoundaryArc> arcs,
        IReadOnlyDictionary<int, CellCrustState> state,
        IReadOnlyDictionary<int, CrustFeature>? features,
        BoundaryProfileParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(globe);
        ArgumentNullException.ThrowIfNull(arcs);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(parameters);

        var resolvedArcs = ConvergentPolarity.Attach(arcs, globe.Cells, features, state);
        var field = CellBoundaryField.Build(globe.Cells, resolvedArcs);

        var contributions = new double[globe.CellCount];
        for (int c = 0; c < contributions.Length; c++)
            contributions[c] = BoundaryProfileShape.Contribution(field[c], parameters);
        return contributions;
    }

    public static double[] Build(
        GeodesicSphereTessellation tessellation,
        IReadOnlyList<PlateBoundaryArc> arcs,
        IReadOnlyDictionary<int, CellCrustState> state,
        IReadOnlyDictionary<int, CrustFeature>? features,
        BoundaryProfileParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(tessellation);
        ArgumentNullException.ThrowIfNull(arcs);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(parameters);

        int n = tessellation.CellCount;
        int freq = tessellation.Frequency;
        var cells = new List<GlobeCell>(n);
        for (int cell = 0; cell < n; cell++)
        {
            var boundary = tessellation.GetBoundary(new GeodesicCoord(cell, freq));
            cells.Add(new GlobeCell(
                cell,
                -1,
                ToVec3(boundary[0]),
                ToVec3(boundary[1]),
                ToVec3(boundary[2])));
        }

        var snapshot = new WorldGlobeSnapshot(
            tessellation.Frequency,
            n,
            0,
            0,
            cells,
            Array.Empty<GlobePlate>());

        return Build(snapshot, arcs, state, features, parameters);
    }

    private static GlobeVec3 ToVec3(SphericalPoint p)
    {
        var v = p.ToVector3D();
        return new GlobeVec3((float)v.X, (float)v.Y, (float)v.Z);
    }
}
