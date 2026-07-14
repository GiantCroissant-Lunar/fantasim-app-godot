using System;
using System.Collections.Generic;
using FantaSim.Geosphere.Asthenosphere.Convection;
using FantaSim.Geosphere.Plate.Topology;
using FantaSim.Geosphere.Plate.Topology.Contracts;
using FantaSim.Geosphere.Plate.Topology.Materializer;
using FantaSim.World.Contracts.Units;
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
    /// Calibrated default plate angular-drift rate in rad/Ma — the single source of truth behind
    /// the <c>spinRateRadiansPerMegaAnnum</c> world knob (user decision 2026-07-11: ONE adjustable
    /// property end-to-end, no scattered constants). Calibrated 2026-07-07 against real plate-stage
    /// rates from Cao et al. 2024 (1.8 Ga GPlates model): 0.0035 rad/Ma ≈ 0.20°/Ma is the
    /// Phanerozoic movers MEDIAN. The previous 0.02 rad/Ma was the movers p90 (~5.7× too fast for
    /// a default). See tools/rates/2026-07-07-rate-calibration-report.md (quaternion stage-rate
    /// analysis of the real .rot files). The lively upper below is kept as a documented
    /// alternative, not wired.
    /// </summary>
    public const double DefaultAngularDriftPerMegaAnnum = 0.0035;
    // ~p90 of real plates — "lively" option, see tools/rates/2026-07-07-rate-calibration-report.md.
    private const double LivelyUpperAngularDriftPerMegaAnnum = 0.017;

    /// <summary>
    /// The TruthStream identity used by LidFractureAtOnset / PlateTopologyEmitter when producing
    /// the onset roster. Minted by <see cref="WorldStreamVocabulary.PlateTopologyTruth"/> — the
    /// engine-lockstep contract lives on that factory.
    /// </summary>
    private static readonly TruthStreamIdentity PlateTopologyStreamIdentity =
        WorldStreamVocabulary.PlateTopologyTruth();

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
    /// <para><paramref name="angularDriftPerMegaAnnum"/> is the world spin-rate knob
    /// (<c>spinRateRadiansPerMegaAnnum</c>), authored in rad/Ma and converted to rad/tick here —
    /// the declared conversion point; everything downstream is tick-native.</para>
    /// </summary>
    public static OnsetRoster Build(
        int worldSeed,
        long onsetTick,
        int tessellationFrequency,
        double angularDriftPerMegaAnnum = DefaultAngularDriftPerMegaAnnum)
    {
        if (double.IsNaN(angularDriftPerMegaAnnum)
            || double.IsInfinity(angularDriftPerMegaAnnum)
            || angularDriftPerMegaAnnum < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(angularDriftPerMegaAnnum),
                angularDriftPerMegaAnnum,
                "Plate angular drift must be a finite, non-negative rad/Ma value.");
        }

        double angularDriftPerTick =
            UnitConverter.RadiansPerMegaAnnumToRadiansPerTick(angularDriftPerMegaAnnum);
        var field = new ConvectionFieldGenerator(new ConvectionFieldConfig
        {
            Seed = worldSeed,
            AngularDriftPerTick = angularDriftPerTick,
        });
        var tess = new GeodesicSphereTessellation(tessellationFrequency);

        // Build the geometry seeds by mirroring LidFractureAtOnset's seed loop EXACTLY
        // (upwelling order -> plate IDs 0..N-1 match PlateTopologyState integer keys).
        // Motion is derived from the same convection center's one-tick drift, so the pole/rate lives
        // in the world DTO instead of being a renderer-only animation.
        var structure = field.GetStructure(onsetTick);
        var nextStructure = field.GetStructure(checked(onsetTick + 1));
        var seedPlates = new List<Plate>(structure.Upwellings.Count);
        for (int i = 0; i < structure.Upwellings.Count; i++)
        {
            var axis = NormalizeOrZero(structure.Upwellings[i].Position);
            var nextAxis = i < nextStructure.Upwellings.Count
                ? NormalizeOrZero(nextStructure.Upwellings[i].Position)
                : axis;
            seedPlates.Add(new Plate(i, ToSpherical(axis), PoleFromCenterDrift(axis, nextAxis, angularDriftPerTick)));
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
    /// These plates carry convection-drift poles derived from the same upwelling center at the onset tick.
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

    private static EulerPole PoleFromCenterDrift(Vector3D current, Vector3D next, double fallbackRatePerTick)
    {
        var cross = Cross(current, next);
        var crossLength = Length(cross);
        if (crossLength < 1e-15)
            return new EulerPole(FallbackPoleAxis(current), fallbackRatePerTick);

        var axis = new Vector3D(cross.X / crossLength, cross.Y / crossLength, cross.Z / crossLength);
        var dot = Math.Clamp(Dot(current, next), -1.0, 1.0);
        var rate = Math.Atan2(crossLength, dot);
        return new EulerPole(axis, rate <= 0.0 ? fallbackRatePerTick : rate);
    }

    private static Vector3D FallbackPoleAxis(Vector3D current)
    {
        var seed = Math.Abs(current.Z) < 0.9 ? new Vector3D(0, 0, 1) : new Vector3D(0, 1, 0);
        return NormalizeOrZero(Cross(current, seed));
    }

    private static Vector3D Cross(Vector3D a, Vector3D b)
        => new(
            (a.Y * b.Z) - (a.Z * b.Y),
            (a.Z * b.X) - (a.X * b.Z),
            (a.X * b.Y) - (a.Y * b.X));

    private static double Dot(Vector3D a, Vector3D b)
        => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    private static double Length(Vector3D v)
        => Math.Sqrt((v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z));

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
