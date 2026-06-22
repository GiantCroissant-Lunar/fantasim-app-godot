using System;
using System.Collections.Generic;
using FantaSim.Geosphere.Asthenosphere.Convection;
using FantaSim.Geosphere.Plate.Topology;
using FantaSim.Geosphere.Plate.Topology.Contracts;
using FantaSim.Geosphere.Plate.Topology.Materializer;
using FantaSim.World.TruthStream;
using TimeDete.Time.Primitives;
using UnifyCell;
using UnifyGeometry.Spherical;
using UnifyMaths;

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
    /// <summary>
    /// The TruthStream identity used by LidFractureAtOnset / PlateTopologyEmitter when producing
    /// the onset roster. These values MUST match the engine's plate-topology stream config
    /// (TruthStreamIdentity used in fantasim-world's PlateTopologyEmitter.EmitRoster call path).
    /// If the engine config changes, update all five parts here in lockstep.
    /// </summary>
    private static readonly TruthStreamIdentity PlateTopologyStreamIdentity =
        new("default", "main", 2, "geo.plates.topology", "M0");

    private static readonly PlateTopologyState EmptyState = new();

    private readonly long _onsetTick;
    private readonly PlateTopologyState _stateAtOnset;

    // Geometry plates (same object as LidFractureAtOnset's seed list, upwelling order → IDs 0..N-1).
    // Null before onset is observed; populated by Build from the fracture step.
    private readonly IReadOnlyList<Plate> _seedPlates;

    private OnsetRoster(long onsetTick, PlateTopologyState stateAtOnset, IReadOnlyList<Plate> seedPlates)
    {
        _onsetTick = onsetTick;
        _stateAtOnset = stateAtOnset;
        _seedPlates = seedPlates;
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

        // Build the geometry seeds by mirroring LidFractureAtOnset's seed loop EXACTLY
        // (upwelling order → plate IDs 0..N-1 match PlateTopologyState integer keys).
        var structure = field.GetStructure(onsetTick);
        var seedPlates = new List<Plate>(structure.Upwellings.Count);
        for (int i = 0; i < structure.Upwellings.Count; i++)
        {
            var axis = NormalizeOrZero(structure.Upwellings[i].Position);
            seedPlates.Add(new Plate(i, ToSpherical(axis), new EulerPole(axis, 0.0)));
        }

        IReadOnlyList<ITruthEventDraft> drafts = LidFractureAtOnset.Fracture(
            tess, field, onsetTick, PlateTopologyStreamIdentity);

        var state = FoldToState(drafts);
        return new OnsetRoster(onsetTick, state, seedPlates);
    }

    /// <summary>
    /// Returns the roster at <paramref name="tick"/>:
    /// an empty (zero-plate) <see cref="PlateTopologyState"/> before the onset tick,
    /// or the N-plate state at/after.
    /// </summary>
    public PlateTopologyState PlatesAt(long tick) =>
        tick < _onsetTick ? EmptyState : _stateAtOnset;

    /// <summary>
    /// Returns the geometry <see cref="Plate"/> seed list at <paramref name="tick"/>:
    /// an empty list before the onset tick; at/after onset, the N plates produced by
    /// mirroring <c>LidFractureAtOnset</c>'s upwelling-seed loop exactly — same
    /// upwelling order → same integer IDs 0..N-1 as <see cref="PlatesAt"/>'s plate keys.
    /// These plates carry placeholder poles (<c>EulerPole(axis, 0.0)</c>); convection-driven
    /// drift is roadmap §9.2.
    /// </summary>
    public IReadOnlyList<Plate> SeedPlatesAt(long tick) =>
        tick < _onsetTick ? Array.Empty<Plate>() : _seedPlates;

    // ---------------------------------------------------------------- seed-geometry helpers
    // Mirrors LidFractureAtOnset's use of SphereMath (engine-internal) via public UnifyGeometry /
    // UnifyMaths equivalents so the plate seeds are identical to what the engine produces.

    /// <summary>Unit-length copy, or zero vector if near-zero. Mirrors SphereMath.NormalizeOrZero.</summary>
    private static Vector3D NormalizeOrZero(Vector3D v)
    {
        double len = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return len < 1e-15 ? new Vector3D(0, 0, 0) : new Vector3D(v.X / len, v.Y / len, v.Z / len);
    }

    /// <summary>Spherical point for a cartesian unit vector. Mirrors SphereMath.ToSpherical
    /// (which delegates to SphericalVectorInterop.ToSphericalPoint → SphericalOps.ToSphericalPoint).</summary>
    private static SphericalPoint ToSpherical(Vector3D v)
        => SphericalVectorInterop.ToSphericalPoint(v);

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
