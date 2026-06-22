using System;
using System.Collections.Generic;
using FantaSim.Geosphere.Asthenosphere.Convection;
using FantaSim.Geosphere.Plate.Topology;
using FantaSim.Geosphere.Plate.Topology.Contracts;
using FantaSim.Geosphere.Plate.Topology.Materializer;
using FantaSim.World.TruthStream;
using TimeDete.Time.Primitives;
using UnifyCell;

namespace FantaSim.App.World.Composition;

/// <summary>
/// Pure helper: builds the plate roster at the hydration-derived onset tick (via
/// <see cref="LidFractureAtOnset.Fracture"/>) and gates access by tick.
/// Before <see cref="Build"/>'s <c>onsetTick</c> the roster is empty (zero plates);
/// at/after onset it is the deterministic N-plate fold produced by
/// <see cref="PlateTopologyMaterializer.Apply"/>.
/// </summary>
/// <remarks>
/// The fold adapter (<see cref="FakeEvent"/>) and the materializer loop are lifted verbatim
/// from the engine's <c>OnsetRosterFoldTests.cs</c> (READ-ONLY reference in fantasim-world).
/// </remarks>
public sealed class OnsetRoster
{
    private static readonly PlateTopologyState EmptyState = new();

    private readonly long _onsetTick;
    private readonly PlateTopologyState _stateAtOnset;

    private OnsetRoster(long onsetTick, PlateTopologyState stateAtOnset)
    {
        _onsetTick = onsetTick;
        _stateAtOnset = stateAtOnset;
    }

    /// <summary>
    /// Builds a roster by:
    /// <list type="number">
    ///   <item>Constructing a <see cref="ConvectionFieldGenerator"/> from <paramref name="worldSeed"/>.</item>
    ///   <item>Fracturing the lid at <paramref name="onsetTick"/> via <see cref="LidFractureAtOnset.Fracture"/>.</item>
    ///   <item>Folding the resulting <see cref="ITruthEventDraft"/>s into a <see cref="PlateTopologyState"/>
    ///         via <see cref="PlateTopologyMaterializer.Apply"/> (proven engine fold, not re-derived here).</item>
    /// </list>
    /// Pure/deterministic: same inputs produce the same roster every call.
    /// </summary>
    public static OnsetRoster Build(int worldSeed, long onsetTick, int tessellationFrequency)
    {
        var field = new ConvectionFieldGenerator(new ConvectionFieldConfig { Seed = worldSeed });
        var tess = new GeodesicSphereTessellation(tessellationFrequency);
        var stream = new TruthStreamIdentity("default", "main", 2, "geo.plates.topology", "M0");

        IReadOnlyList<ITruthEventDraft> drafts = LidFractureAtOnset.Fracture(tess, field, onsetTick, stream);

        var state = FoldToState(drafts);
        return new OnsetRoster(onsetTick, state);
    }

    /// <summary>
    /// Returns the roster at <paramref name="tick"/>:
    /// an empty (zero-plate) <see cref="PlateTopologyState"/> before the onset tick,
    /// or the N-plate state at/after.
    /// </summary>
    public PlateTopologyState PlatesAt(long tick) =>
        tick < _onsetTick ? EmptyState : _stateAtOnset;

    // ---------------------------------------------------------------- fold adapter

    /// <summary>
    /// Folds ITruthEventDraft items into a PlateTopologyState using the engine's
    /// PlateTopologyMaterializer.Apply — identical pattern to OnsetRosterFoldTests.cs
    /// in fantasim-world (READ-ONLY). The FakeEvent shim bridges ITruthEventDraft
    /// (what Fracture emits) to ITruthEvent (what the materializer consumes).
    /// </summary>
    private static PlateTopologyState FoldToState(IReadOnlyList<ITruthEventDraft> drafts)
    {
        var state = new PlateTopologyState();
        foreach (var d in drafts)
            PlateTopologyMaterializer.Apply(state, new FakeEvent(d));
        return state;
    }

    /// <summary>
    /// Minimal <see cref="ITruthEvent"/> adapter over <see cref="ITruthEventDraft"/>.
    /// Lifted verbatim from the engine's <c>OnsetRosterFoldTests.FakeEvent</c> (READ-ONLY).
    /// </summary>
    private sealed class FakeEvent : ITruthEvent
    {
        private readonly ITruthEventDraft _d;
        public FakeEvent(ITruthEventDraft d) => _d = d;
        public Guid EventId => Guid.Empty;
        public CanonicalTick Tick => _d.Tick;
        public long Sequence => 0;
        public TruthStreamIdentity StreamIdentity => _d.Stream;
        public ReadOnlyMemory<byte> PreviousHash => default;
        public ReadOnlyMemory<byte> Hash => default;
        public string EventType => _d.EventType;
        public ReadOnlyMemory<byte> Payload => _d.Payload;
    }
}
