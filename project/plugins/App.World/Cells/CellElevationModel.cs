using System;
using System.Collections.Generic;
using Arch.Core;
using FantaSim.App.Ecs.Cells;
using FantaSim.App.Ecs.Systems;
using FantaSim.App.World.Globe;
using FantaSim.Geosphere.Crust;
using UnifyECS;
using ArchWorld = Arch.Core.World;
using Entity = Arch.Core.Entity;

namespace FantaSim.App.World.Cells;

/// <summary>
/// Makes the app's ECS load-bearing for planet relief (sub-project B): each globe cell is an ECS
/// entity carrying its crust fields, and a per-cell <see cref="Elevation"/> is derived by
/// <see cref="CellElevationSystem"/>. Godot-free and fully unit-testable.
///
/// <para>The model owns its own <see cref="ArchSystemRunner"/> (the same UnifyEcs+Arch stack App.Ecs
/// uses — no second ECS) with <see cref="CellElevationSystem"/> registered. It is populated from a
/// <see cref="CrustStateRun"/> (the engine's <c>StateByTick</c> + cell centers): one entity per cell
/// with <see cref="CellId"/>/<see cref="PlateId"/>/<see cref="CellUnit"/> and a <see cref="CrustSample"/>
/// taken from the nearest snapshot. <see cref="UpdateForTick"/> re-samples each cell from the nearest
/// snapshot and runs the system; <see cref="GetElevations"/> returns the per-cell elevation array
/// (indexed by cell id) the relief render (C) uploads to the GPU.</para>
/// </summary>
public sealed class CellElevationModel : IDisposable
{
    private readonly ArchSystemRunner _runner;
    private readonly ArchWorld _world;
    private readonly CellElevationSystem _system;

    private readonly int _cellCount;
    private readonly IReadOnlyDictionary<long, IReadOnlyDictionary<int, CellCrustState>> _stateByTick;
    private readonly long[] _snapshotTicks;

    // One entity per cell, indexed by cell id, so per-tick re-sampling and elevation read-out are O(1)
    // per cell with no query-order assumptions.
    private readonly Entity[] _cellEntities;

    private long _currentTick = long.MinValue;

    /// <summary>
    /// Builds the cell model from a materialized crust run. Creates the ECS world + cell entities and
    /// populates them at the run's earliest snapshot tick (so <see cref="GetElevations"/> is valid
    /// immediately); call <see cref="UpdateForTick"/> to advance.
    /// </summary>
    /// <param name="run">The crust state run (StateByTick + cell centers).</param>
    /// <param name="plateIdByCell">Owning plate id per cell, indexed by cell id (length = cell count).</param>
    public CellElevationModel(CrustStateRun run, IReadOnlyList<int> plateIdByCell)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(plateIdByCell);
        if (plateIdByCell.Count != run.CellCount)
            throw new ArgumentException(
                $"plateIdByCell length {plateIdByCell.Count} != cell count {run.CellCount}.", nameof(plateIdByCell));
        if (run.CellCenters.Count != run.CellCount)
            throw new ArgumentException(
                $"CellCenters length {run.CellCenters.Count} != cell count {run.CellCount}.", nameof(run));

        _cellCount = run.CellCount;
        _stateByTick = run.StateByTick;

        var ticks = new List<long>(run.StateByTick.Keys);
        ticks.Sort();
        if (ticks.Count == 0)
            throw new ArgumentException("StateByTick is empty; need at least one snapshot tick.", nameof(run));
        _snapshotTicks = ticks.ToArray();

        // The ECS world: UnifyEcs's ArchWorld wrapper drives the runner, but entity create/set/get and
        // the system's query all run against the SAME raw Arch.Core.World (the wrapper's InnerWorld) —
        // exactly the pattern the App.Ecs field tests use, so CellElevationSystem.Execute sees the cells.
        var wrapper = (UnifyECS.ArchWorld)WorldFactory.Create(EcsBackend.Arch, new WorldConfig
        {
            Name = "cell-elevation",
            InitialEntityCapacity = _cellCount,
        });
        _runner = new ArchSystemRunner(wrapper);
        _world = _runner.InnerWorld;
        _system = new CellElevationSystem();
        _runner.Register(_system);
        _runner.Initialize();

        _cellEntities = new Entity[_cellCount];
        for (int cell = 0; cell < _cellCount; cell++)
        {
            var center = run.CellCenters[cell];
            _cellEntities[cell] = _world.Create(
                new CellId(cell),
                new PlateId(cell < plateIdByCell.Count ? plateIdByCell[cell] : -1),
                new CellUnit(center.X, center.Y, center.Z),
                default(CrustSample),
                default(Elevation));
        }

        // Populate at the earliest snapshot so the world holds a valid elevation before any UpdateForTick.
        UpdateForTick(_snapshotTicks[0]);
    }

    /// <summary>Number of cell entities (= cell count). The elevation array has this length.</summary>
    public int CellCount => _cellCount;

    /// <summary>
    /// Re-sample every cell's <see cref="CrustSample"/> from the snapshot nearest <paramref name="tick"/>,
    /// then run <see cref="CellElevationSystem"/> so the ECS holds the live elevation for that tick.
    /// Idempotent for a repeated tick.
    /// </summary>
    public void UpdateForTick(long tick)
    {
        if (tick == _currentTick) return;

        long nearest = NearestSnapshot(tick);
        var snapshot = _stateByTick[nearest];

        for (int cell = 0; cell < _cellCount; cell++)
        {
            var sample = snapshot.TryGetValue(cell, out var s)
                ? new CrustSample(s.ContinentalFraction, s.OrogenicPressure, s.VolcanicActivity, s.CrustAgeTicks)
                : default; // Cell absent from the snapshot (no crust contributions): zeroed sample.
            _world.Set(_cellEntities[cell], sample);
        }

        _runner.Update(0f); // Drives CellElevationSystem.Execute over the world.
        _currentTick = tick;
    }

    /// <summary>
    /// The derived per-cell elevation, indexed by cell id (length = <see cref="CellCount"/>). This is the
    /// array sub-project C uploads to the GPU. Reflects the most recent <see cref="UpdateForTick"/>.
    /// </summary>
    public double[] GetElevations()
    {
        var result = new double[_cellCount];
        for (int cell = 0; cell < _cellCount; cell++)
            result[cell] = _world.Get<Elevation>(_cellEntities[cell]).Value;
        return result;
    }

    private long NearestSnapshot(long tick)
    {
        long best = _snapshotTicks[0];
        long bestDelta = Math.Abs(best - tick);
        for (int i = 1; i < _snapshotTicks.Length; i++)
        {
            long delta = Math.Abs(_snapshotTicks[i] - tick);
            if (delta < bestDelta) { best = _snapshotTicks[i]; bestDelta = delta; }
        }
        return best;
    }

    public void Dispose()
    {
        _runner.Dispose();
        _world.Dispose();
    }

    /// <summary>
    /// Convenience: run the crust pipeline via <paramref name="reconstructor"/> over
    /// <paramref name="snapshotTicks"/> and build a populated model. The plate ids come from the
    /// reconstructor's globe snapshot (cell id → plate id).
    /// </summary>
    public static CellElevationModel Build(GlobeReconstructor reconstructor, IReadOnlyList<long> snapshotTicks)
    {
        ArgumentNullException.ThrowIfNull(reconstructor);
        ArgumentNullException.ThrowIfNull(snapshotTicks);

        var globe = reconstructor.BuildGlobe();
        var plateIdByCell = new int[globe.CellCount];
        foreach (var c in globe.Cells)
            if (c.CellId >= 0 && c.CellId < plateIdByCell.Length)
                plateIdByCell[c.CellId] = c.PlateId;

        var run = reconstructor.RunCrustEvolution(snapshotTicks);
        return new CellElevationModel(run, plateIdByCell);
    }
}
