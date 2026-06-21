using System.Collections.Generic;
using FantaSim.App.World.Dto;
using FantaSim.Geosphere.Crust;

namespace FantaSim.App.World.Cells;

/// <summary>
/// One materialized crust-evolution run, scoped to what <see cref="CellElevationModel"/> needs:
/// the accumulated per-cell crust state at each snapshot tick (engine <see cref="CellCrustState"/>),
/// plus each cell's unit-sphere center. Produced by
/// <see cref="Globe.GlobeReconstructor.RunCrustEvolution"/> from a single <c>CrustPipeline.RunAsync</c>.
/// </summary>
/// <param name="CellCount">Number of geodesic cells (state ids and <see cref="CellCenters"/> index by this).</param>
/// <param name="StateByTick">Accumulated per-cell crust state at each snapshot tick (ticks 0..tick folded).</param>
/// <param name="CellCenters">Unit-sphere center of each cell, indexed by cell id.</param>
public sealed record CrustStateRun(
    int CellCount,
    IReadOnlyDictionary<long, IReadOnlyDictionary<int, CellCrustState>> StateByTick,
    IReadOnlyList<GlobeVec3> CellCenters);
