using System.Collections.Generic;
using FantaSim.App.World.Dto;
using FantaSim.Geosphere.Crust;

namespace FantaSim.App.World.Topography;

/// <summary>
/// Single composition point for the per-cell boundary-profile elevation contribution (P4). Wires together
/// <see cref="ConvergentPolarity.Derive"/> (subduction polarity from crust features) →
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

        var polarity = ConvergentPolarity.Derive(arcs, globe.Cells, features, state);
        var field = CellBoundaryField.Build(globe.Cells, arcs, polarity);

        var contributions = new double[globe.CellCount];
        for (int c = 0; c < contributions.Length; c++)
            contributions[c] = BoundaryProfileShape.Contribution(field[c], parameters);
        return contributions;
    }
}
