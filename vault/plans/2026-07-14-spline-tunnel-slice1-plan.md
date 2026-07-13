# Spline Tunnel Slice 1 — Bent Bore Implementation Plan

> **For agentic workers:** Implement this plan task-by-task. Prefer orchestrating bounded
> per-task agents (see the `orchestrate-before-implementing` rule + the `external-agent-delegation`
> skill); otherwise execute inline with a review checkpoint per task. Steps use checkbox
> (`- [ ]`) syntax for tracking.

**Goal:** Replace the tunnel's straight far-field with a deterministic, gently curved spline bore
while keeping the interactive near-field exactly straight and untouched.

**Architecture:** A new Godot-free pure module (`TunnelBoreSpline`) maps signed depth from the
current-tick plane to a position + parallel-transport frame; a segment planner chops depth bands
into short rigid segments placed on those frames. Only three binder placement sites change
(corridor walls, filmstrip frames, dark shell). A ray-pick depth guard pins the invariant
"interactive window ⊆ straight window" so no input mapper needs to understand curvature.

**Tech Stack:** C# net8.0, UnifyMaths (`Vector3D`), Godot 4 only inside the existing binder
partials, xUnit.

**Spec:** `vault/specs/2026-07-14-spline-tunnel-branch-fork-design.md` (§3, §4).

## Global Constraints

- New geometry math is Godot-free and uses UnifyMaths types; Godot appears only in
  `TunnelPresentationBinder*` partials (house T4 rule).
- Determinism: no `Random`, no wall-clock, no `Guid.NewGuid()` in any new code path. Curvature is
  a pure function of `(seed, depth)`.
- The interactive near-field is EXACTLY straight: for depth ≤ `TunnelBoreContract.StraightRadius`
  the bore is bit-identical to today's straight cylinder.
- Do not modify: `TunnelCameraFraming` constants/behavior, the instrument hierarchy
  (`TunnelPresentationBinder.Rings.cs`), input relays/policies (except the single additive depth
  guard in Task 4), scrub semantics, the 2D face, any csproj, `collectible-bundles.json`.
- Existing suites must stay green: `App.Presentation.Tests`, `App.Timeline.Tests`.
- Do NOT commit, stage, or push — leave changes in the working tree; the lead reviews and commits.
- Existing depth vocabulary (verified in code): `TunnelCameraFraming.TunnelRadius = 5.0f`,
  `CurrentPlaneZ = -5.0f`, `ThroatZ = -20.0f`, `TimelineDepth = 15.0f`; corridor walls are built
  in 4 depth bands (`TunnelCorridorDepthPolicy.Plan`); filmstrip Z comes from
  `TunnelCameraFraming.TryTickToZ`; the dark shell is banded by `TunnelShellDepthPolicy.Plan`.
  **Depth convention in this plan:** `depth = CurrentPlaneZ - z` (0 at the current plane,
  increasing toward the throat; throat depth = 15).

---

### Task 1: TunnelBoreContract + TunnelBoreSpline pure module

**Files:**
- Create: `project/plugins/App.Presentation/Tunnel/TunnelBoreSpline.cs`
- Test: `project/tests/App.Presentation.Tests/TunnelBoreSplineTests.cs`

**Interfaces:**
- Produces (later tasks rely on these exact shapes):
  - `internal static class TunnelBoreContract` with
    `internal const double StraightRadius = 7.5;`
    `internal const double CurvatureCapRadPerUnit = 0.12;`
    `internal const double RampLength = 1.5;`
    `internal const double MaxSegmentLength = 1.25;`
  - `internal readonly record struct TunnelBoreFrame(Vector3D Position, Vector3D Forward, Vector3D Right, Vector3D Up);`
  - `internal sealed class TunnelBoreSpline` with
    `internal static TunnelBoreSpline Create(long seed, double straightRadius, double curvatureCapRadPerUnit, double maxDepth)` and
    `internal TunnelBoreFrame Evaluate(double depth)` (input clamped to `[0, maxDepth]`).
  - Straight-region output: `Position = (0, 0, -depth)`, `Forward = (0,0,-1)`,
    `Right = (1,0,0)`, `Up = (0,1,0)` — positions are RELATIVE to the current plane; the binder
    adds `CurrentPlaneZ`.

- [ ] **Step 1: Write the failing tests**

```csharp
using FantaSim.App.Presentation.Tunnel;
using UnifyMaths;
using Xunit;

namespace FantaSim.App.Presentation.Tests;

public sealed class TunnelBoreSplineTests
{
    private const double MaxDepth = 15.0;

    private static TunnelBoreSpline Create(long seed = 1234)
        => TunnelBoreSpline.Create(
            seed,
            TunnelBoreContract.StraightRadius,
            TunnelBoreContract.CurvatureCapRadPerUnit,
            MaxDepth);

    [Theory]
    [InlineData(0.0)]
    [InlineData(3.0)]
    [InlineData(7.5)]
    public void Near_field_is_exactly_straight(double depth)
    {
        var frame = Create().Evaluate(depth);

        Assert.Equal(0.0, frame.Position.X, 12);
        Assert.Equal(0.0, frame.Position.Y, 12);
        Assert.Equal(-depth, frame.Position.Z, 12);
        Assert.Equal(-1.0, frame.Forward.Z, 12);
        Assert.Equal(1.0, frame.Right.X, 12);
        Assert.Equal(1.0, frame.Up.Y, 12);
    }

    [Fact]
    public void Same_seed_is_deterministic_across_instances()
    {
        var a = Create(seed: 77);
        var b = Create(seed: 77);
        for (double d = 0.0; d <= MaxDepth; d += 0.5)
        {
            var fa = a.Evaluate(d);
            var fb = b.Evaluate(d);
            Assert.Equal(fa.Position.X, fb.Position.X, 12);
            Assert.Equal(fa.Position.Y, fb.Position.Y, 12);
            Assert.Equal(fa.Position.Z, fb.Position.Z, 12);
            Assert.Equal(fa.Forward.X, fb.Forward.X, 12);
            Assert.Equal(fa.Forward.Y, fb.Forward.Y, 12);
            Assert.Equal(fa.Forward.Z, fb.Forward.Z, 12);
        }
    }

    [Fact]
    public void Different_seeds_diverge_beyond_the_straight_window()
    {
        var a = Create(seed: 1).Evaluate(MaxDepth);
        var b = Create(seed: 2).Evaluate(MaxDepth);
        var separation =
            System.Math.Abs(a.Position.X - b.Position.X)
            + System.Math.Abs(a.Position.Y - b.Position.Y);
        Assert.True(separation > 1e-3, $"expected lateral divergence, got {separation}");
    }

    [Fact]
    public void Curvature_cap_is_honored()
    {
        var spline = Create();
        const double h = 0.25;
        for (double d = h; d <= MaxDepth; d += h)
        {
            var f0 = spline.Evaluate(d - h).Forward;
            var f1 = spline.Evaluate(d).Forward;
            var dot = System.Math.Clamp(
                (f0.X * f1.X) + (f0.Y * f1.Y) + (f0.Z * f1.Z), -1.0, 1.0);
            var anglePerUnit = System.Math.Acos(dot) / h;
            Assert.True(
                anglePerUnit <= TunnelBoreContract.CurvatureCapRadPerUnit + 1e-6,
                $"turn {anglePerUnit} rad/unit at depth {d} exceeds cap");
        }
    }

    [Fact]
    public void Frames_stay_orthonormal_and_unrolled()
    {
        var spline = Create();
        for (double d = 0.0; d <= MaxDepth; d += 0.5)
        {
            var f = spline.Evaluate(d);
            Assert.Equal(1.0, Length(f.Forward), 9);
            Assert.Equal(1.0, Length(f.Right), 9);
            Assert.Equal(1.0, Length(f.Up), 9);
            Assert.Equal(0.0, Dot(f.Forward, f.Right), 9);
            Assert.Equal(0.0, Dot(f.Forward, f.Up), 9);
            Assert.Equal(0.0, Dot(f.Right, f.Up), 9);
            // Parallel transport with a bounded cap cannot flip the vertical.
            Assert.True(f.Up.Y > 0.5, $"up vector rolled at depth {d}: {f.Up.Y}");
        }
    }

    [Fact]
    public void Depth_advances_monotonically_along_the_axis()
    {
        var spline = Create();
        var previousZ = double.PositiveInfinity;
        for (double d = 0.0; d <= MaxDepth; d += 0.25)
        {
            var z = spline.Evaluate(d).Position.Z;
            Assert.True(z < previousZ, $"Z not strictly decreasing at depth {d}");
            previousZ = z;
        }
    }

    [Fact]
    public void Transition_at_the_straight_boundary_is_c1_continuous()
    {
        var spline = Create();
        const double s = 7.5;
        const double eps = 0.05;
        var before = spline.Evaluate(s - eps);
        var after = spline.Evaluate(s + eps);
        var positionJump = Length(new Vector3D(
            after.Position.X - before.Position.X,
            after.Position.Y - before.Position.Y,
            after.Position.Z - before.Position.Z));
        Assert.InRange(positionJump, 0.0, (2 * eps) + 1e-3);

        var dot = System.Math.Clamp(Dot(before.Forward, after.Forward), -1.0, 1.0);
        var headingJump = System.Math.Acos(dot);
        Assert.True(
            headingJump <= TunnelBoreContract.CurvatureCapRadPerUnit * 2 * eps + 1e-6,
            $"heading jump {headingJump} at the boundary");
    }

    private static double Length(Vector3D v)
        => System.Math.Sqrt((v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z));

    private static double Dot(Vector3D a, Vector3D b)
        => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test project/tests/App.Presentation.Tests/App.Presentation.Tests.csproj --nologo -v minimal --filter "FullyQualifiedName~TunnelBoreSplineTests"`
Expected: FAIL — `TunnelBoreSpline` / `TunnelBoreContract` not defined.

- [ ] **Step 3: Implement the module**

```csharp
using System;
using UnifyMaths;

namespace FantaSim.App.Presentation.Tunnel;

/// <summary>
/// Shared constants for the bent bore. StraightRadius covers the interactive near-field (the
/// first two corridor depth bands: 2 x TimelineDepth/4 = 7.5 units), so every shipped input
/// path operates on exactly the straight geometry it was written against.
/// </summary>
internal static class TunnelBoreContract
{
    internal const double StraightRadius = 7.5;
    internal const double CurvatureCapRadPerUnit = 0.12;
    internal const double RampLength = 1.5;
    internal const double MaxSegmentLength = 1.25;
}

internal readonly record struct TunnelBoreFrame(
    Vector3D Position,
    Vector3D Forward,
    Vector3D Right,
    Vector3D Up);

/// <summary>
/// Deterministic bent-bore centerline. Depth is measured from the current-tick plane (0) toward
/// the throat; positions are relative to the current plane (straight bore: (0,0,-depth)). The
/// near-field is exactly straight through StraightRadius; beyond it, curvature ramps in
/// C1-continuously (smoothstep over RampLength), is capped, and is integrated with
/// parallel-transported frames so no roll accumulates. Everything is a pure function of the
/// seed — no runtime randomness, no wall-clock.
/// </summary>
internal sealed class TunnelBoreSpline
{
    private const double Step = 0.05;
    private readonly double _straightRadius;
    private readonly double _maxDepth;
    private readonly TunnelBoreFrame[] _samples; // index i = depth straightRadius + i*Step

    private TunnelBoreSpline(double straightRadius, double maxDepth, TunnelBoreFrame[] samples)
    {
        _straightRadius = straightRadius;
        _maxDepth = maxDepth;
        _samples = samples;
    }

    internal static TunnelBoreSpline Create(
        long seed,
        double straightRadius,
        double curvatureCapRadPerUnit,
        double maxDepth)
    {
        if (!double.IsFinite(straightRadius) || straightRadius < 0.0)
            throw new ArgumentOutOfRangeException(nameof(straightRadius));
        if (!double.IsFinite(curvatureCapRadPerUnit) || curvatureCapRadPerUnit < 0.0)
            throw new ArgumentOutOfRangeException(nameof(curvatureCapRadPerUnit));
        if (!double.IsFinite(maxDepth) || maxDepth <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(maxDepth));

        // SplitMix64: deterministic phase/frequency derivation from the seed.
        ulong state = unchecked((ulong)seed);
        double NextUnit()
        {
            state += 0x9E3779B97F4A7C15UL;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (z >> 11) * (1.0 / (1UL << 53));
        }

        double yawFreq = 0.03 + (0.03 * NextUnit());   // cycles per depth unit
        double pitchFreq = 0.03 + (0.03 * NextUnit());
        double yawPhase = NextUnit();
        double pitchPhase = NextUnit();

        var curvedSpan = Math.Max(0.0, maxDepth - straightRadius);
        var count = (int)Math.Ceiling(curvedSpan / Step) + 1;
        var samples = new TunnelBoreFrame[Math.Max(count, 1)];

        var position = new Vector3D(0.0, 0.0, -straightRadius);
        var forward = new Vector3D(0.0, 0.0, -1.0);
        var right = new Vector3D(1.0, 0.0, 0.0);
        var up = new Vector3D(0.0, 1.0, 0.0);
        samples[0] = new TunnelBoreFrame(position, forward, right, up);

        // The two sinusoids share the cap budget: |(yawRate, pitchRate)| <= cap.
        var amplitude = curvatureCapRadPerUnit / Math.Sqrt(2.0);

        for (var i = 1; i < samples.Length; i++)
        {
            var depth = straightRadius + ((i - 1) * Step);
            var t = Math.Clamp((depth - straightRadius) / TunnelBoreContract.RampLength, 0.0, 1.0);
            var ramp = t * t * (3.0 - (2.0 * t)); // smoothstep => C1 at the boundary

            var yawRate = amplitude * ramp
                * Math.Sin(2.0 * Math.PI * ((yawFreq * depth) + yawPhase));
            var pitchRate = amplitude * ramp
                * Math.Sin(2.0 * Math.PI * ((pitchFreq * depth) + pitchPhase));

            // Parallel transport: rotate the WHOLE frame by the incremental turn. Rotating
            // right/up with the same rotations that turn forward injects zero roll.
            var yaw = Quaternion.FromAxisAngle(up, yawRate * Step);
            var pitch = Quaternion.FromAxisAngle(right, pitchRate * Step);

            forward = pitch.Rotate(yaw.Rotate(forward)).Normalize();
            right = pitch.Rotate(yaw.Rotate(right)).Normalize();
            up = pitch.Rotate(yaw.Rotate(up)).Normalize();

            position = new Vector3D(
                position.X + (forward.X * Step),
                position.Y + (forward.Y * Step),
                position.Z + (forward.Z * Step));

            samples[i] = new TunnelBoreFrame(position, forward, right, up);
        }

        return new TunnelBoreSpline(straightRadius, maxDepth, samples);
    }

    internal TunnelBoreFrame Evaluate(double depth)
    {
        if (!double.IsFinite(depth))
            depth = 0.0;
        depth = Math.Clamp(depth, 0.0, _maxDepth);

        if (depth <= _straightRadius)
        {
            return new TunnelBoreFrame(
                new Vector3D(0.0, 0.0, -depth),
                new Vector3D(0.0, 0.0, -1.0),
                new Vector3D(1.0, 0.0, 0.0),
                new Vector3D(0.0, 1.0, 0.0));
        }

        var offset = (depth - _straightRadius) / Step;
        var index = (int)Math.Floor(offset);
        if (index >= _samples.Length - 1)
            return _samples[^1];

        var f = offset - index;
        var a = _samples[index];
        var b = _samples[index + 1];
        return new TunnelBoreFrame(
            Lerp(a.Position, b.Position, f),
            NLerp(a.Forward, b.Forward, f),
            NLerp(a.Right, b.Right, f),
            NLerp(a.Up, b.Up, f));
    }

    private static Vector3D Lerp(Vector3D a, Vector3D b, double t)
        => new(
            a.X + ((b.X - a.X) * t),
            a.Y + ((b.Y - a.Y) * t),
            a.Z + ((b.Z - a.Z) * t));

    private static Vector3D NLerp(Vector3D a, Vector3D b, double t)
        => Lerp(a, b, t).Normalize();
}
```

Note for the implementer: verify the exact UnifyMaths API surface before compiling
(`Quaternion.FromAxisAngle(Vector3D axis, double radians)`, `Quaternion.Rotate(Vector3D)`,
`Vector3D.Normalize()`). These are the documented UnifyMaths primitives (workspace rule: build on
UnifyMaths, do not hand-roll quaternion math). If `Rotate` composes differently, fix the call
sites — never re-implement the quaternion type. If orthonormality drifts above test tolerance
after NLerp interpolation of nearly-parallel neighbors (step is 0.05, so neighbors are ~0.006 rad
apart — it will not), re-orthogonalize `right = (up × forward).Normalize()` style inside
`Evaluate` rather than loosening the test.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test project/tests/App.Presentation.Tests/App.Presentation.Tests.csproj --nologo -v minimal --filter "FullyQualifiedName~TunnelBoreSplineTests"`
Expected: PASS (8 tests).

---

### Task 2: TunnelBoreSegments planner

**Files:**
- Create: `project/plugins/App.Presentation/Tunnel/TunnelBoreSegments.cs`
- Test: `project/tests/App.Presentation.Tests/TunnelBoreSegmentsTests.cs`

**Interfaces:**
- Consumes: `TunnelBoreSpline.Evaluate`, `TunnelBoreFrame`, `TunnelBoreContract.MaxSegmentLength`.
- Produces:
  - `internal readonly record struct TunnelBoreSegment(double MidDepth, double HalfLength, TunnelBoreFrame Frame);`
  - `internal static class TunnelBoreSegments` with
    `internal static IReadOnlyList<TunnelBoreSegment> Plan(TunnelBoreSpline spline, double nearDepth, double farDepth, double maxSegmentLength)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Collections.Generic;
using FantaSim.App.Presentation.Tunnel;
using Xunit;

namespace FantaSim.App.Presentation.Tests;

public sealed class TunnelBoreSegmentsTests
{
    private static TunnelBoreSpline Spline()
        => TunnelBoreSpline.Create(
            seed: 1234,
            TunnelBoreContract.StraightRadius,
            TunnelBoreContract.CurvatureCapRadPerUnit,
            maxDepth: 15.0);

    [Fact]
    public void Straight_band_is_a_single_segment()
    {
        var segments = TunnelBoreSegments.Plan(Spline(), 0.0, 7.5, TunnelBoreContract.MaxSegmentLength);

        var segment = Assert.Single(segments);
        Assert.Equal(3.75, segment.MidDepth, 12);
        Assert.Equal(3.75, segment.HalfLength, 12);
        Assert.Equal(-3.75, segment.Frame.Position.Z, 12);
    }

    [Fact]
    public void Curved_band_is_subdivided_to_the_maximum_segment_length()
    {
        var segments = TunnelBoreSegments.Plan(Spline(), 7.5, 11.25, TunnelBoreContract.MaxSegmentLength);

        Assert.Equal(3, segments.Count);
        foreach (var segment in segments)
            Assert.True(segment.HalfLength * 2.0 <= TunnelBoreContract.MaxSegmentLength + 1e-9);
    }

    [Fact]
    public void Segments_tile_the_band_without_gaps()
    {
        var segments = TunnelBoreSegments.Plan(Spline(), 7.5, 15.0, TunnelBoreContract.MaxSegmentLength);

        var covered = 0.0;
        var cursor = 7.5;
        foreach (var segment in segments)
        {
            Assert.Equal(cursor + segment.HalfLength, segment.MidDepth, 9);
            cursor += segment.HalfLength * 2.0;
            covered += segment.HalfLength * 2.0;
        }
        Assert.Equal(7.5, covered, 9);
    }

    [Fact]
    public void Band_spanning_the_boundary_splits_at_the_straight_radius()
    {
        // A caller may pass a band that crosses StraightRadius; the straight part stays one
        // segment and only the curved remainder subdivides.
        var segments = TunnelBoreSegments.Plan(Spline(), 3.75, 11.25, TunnelBoreContract.MaxSegmentLength);

        Assert.True(segments.Count >= 4);
        Assert.Equal(3.75 + ((7.5 - 3.75) / 2.0), segments[0].MidDepth, 9);
        Assert.Equal((7.5 - 3.75) / 2.0, segments[0].HalfLength, 9);
    }

    [Fact]
    public void Degenerate_or_non_finite_inputs_yield_no_segments()
    {
        Assert.Empty(TunnelBoreSegments.Plan(Spline(), 5.0, 5.0, TunnelBoreContract.MaxSegmentLength));
        Assert.Empty(TunnelBoreSegments.Plan(Spline(), 9.0, 7.0, TunnelBoreContract.MaxSegmentLength));
        Assert.Empty(TunnelBoreSegments.Plan(Spline(), double.NaN, 9.0, TunnelBoreContract.MaxSegmentLength));
        Assert.Empty(TunnelBoreSegments.Plan(Spline(), 7.0, 9.0, 0.0));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test project/tests/App.Presentation.Tests/App.Presentation.Tests.csproj --nologo -v minimal --filter "FullyQualifiedName~TunnelBoreSegmentsTests"`
Expected: FAIL — `TunnelBoreSegments` not defined.

- [ ] **Step 3: Implement**

```csharp
using System;
using System.Collections.Generic;

namespace FantaSim.App.Presentation.Tunnel;

internal readonly record struct TunnelBoreSegment(
    double MidDepth,
    double HalfLength,
    TunnelBoreFrame Frame);

/// <summary>
/// Chops a depth band into rigid chord segments placed on the bore spline. The straight
/// portion (depth <= the spline's straight radius) is emitted as one exact segment; the curved
/// remainder is subdivided so each chord is at most maxSegmentLength long, which keeps the
/// polyline approximation visually smooth at the capped curvature.
/// </summary>
internal static class TunnelBoreSegments
{
    internal static IReadOnlyList<TunnelBoreSegment> Plan(
        TunnelBoreSpline spline,
        double nearDepth,
        double farDepth,
        double maxSegmentLength)
    {
        ArgumentNullException.ThrowIfNull(spline);
        if (!double.IsFinite(nearDepth) || !double.IsFinite(farDepth)
            || !double.IsFinite(maxSegmentLength)
            || maxSegmentLength <= 0.0
            || farDepth <= nearDepth)
        {
            return Array.Empty<TunnelBoreSegment>();
        }

        var result = new List<TunnelBoreSegment>();
        var straight = TunnelBoreContract.StraightRadius;

        if (nearDepth < straight)
        {
            var straightFar = Math.Min(farDepth, straight);
            var mid = (nearDepth + straightFar) / 2.0;
            result.Add(new TunnelBoreSegment(mid, (straightFar - nearDepth) / 2.0, spline.Evaluate(mid)));
            nearDepth = straightFar;
        }

        if (farDepth <= nearDepth)
            return result;

        var span = farDepth - nearDepth;
        var count = (int)Math.Ceiling(span / maxSegmentLength);
        var length = span / count;
        for (var i = 0; i < count; i++)
        {
            var mid = nearDepth + (length * (i + 0.5));
            result.Add(new TunnelBoreSegment(mid, length / 2.0, spline.Evaluate(mid)));
        }

        return result;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test project/tests/App.Presentation.Tests/App.Presentation.Tests.csproj --nologo -v minimal --filter "FullyQualifiedName~TunnelBoreSegmentsTests"`
Expected: PASS (5 tests).

---

### Task 3: TunnelBoreSeedPolicy (branch axis → seed)

**Files:**
- Create: `project/plugins/App.Presentation/Tunnel/TunnelBoreSeedPolicy.cs`
- Test: `project/tests/App.Presentation.Tests/TunnelBoreSeedPolicyTests.cs`

**Interfaces:**
- Produces: `internal static class TunnelBoreSeedPolicy` with
  `internal static long SeedFor(string? branchId)` — FNV-1a 64-bit over the ordinal UTF-16 code
  units of the trimmed branch id; null/empty/whitespace falls back to `"main"`.

- [ ] **Step 1: Write the failing tests**

```csharp
using FantaSim.App.Presentation.Tunnel;
using Xunit;

namespace FantaSim.App.Presentation.Tests;

public sealed class TunnelBoreSeedPolicyTests
{
    [Fact]
    public void Same_branch_id_maps_to_the_same_seed()
        => Assert.Equal(TunnelBoreSeedPolicy.SeedFor("main"), TunnelBoreSeedPolicy.SeedFor("main"));

    [Fact]
    public void Distinct_branch_ids_map_to_distinct_seeds()
        => Assert.NotEqual(TunnelBoreSeedPolicy.SeedFor("main"), TunnelBoreSeedPolicy.SeedFor("import-b"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_branch_falls_back_to_main(string? branch)
        => Assert.Equal(TunnelBoreSeedPolicy.SeedFor("main"), TunnelBoreSeedPolicy.SeedFor(branch));

    [Fact]
    public void Seed_is_stable_across_runs()
        // Locks the encoding so a refactor cannot silently re-bend every tunnel.
        => Assert.Equal(unchecked((long)0xC29FDD00E9E48F0EUL), TunnelBoreSeedPolicy.SeedFor("main"));
}
```

Note: the golden value in `Seed_is_stable_across_runs` is the FNV-1a-64 hash of `"main"` under
the implementation below. On the first GREEN run, if the computed value differs, verify the
implementation matches the spec below EXACTLY (offset basis 14695981039346656037, prime
1099511628211, per-char: XOR low byte then multiply, XOR high byte then multiply); update the
golden constant ONLY if you had to correct the implementation to match this spec, and say so in
AGENT-SUMMARY.md.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test project/tests/App.Presentation.Tests/App.Presentation.Tests.csproj --nologo -v minimal --filter "FullyQualifiedName~TunnelBoreSeedPolicyTests"`
Expected: FAIL — `TunnelBoreSeedPolicy` not defined.

- [ ] **Step 3: Implement**

```csharp
using System;

namespace FantaSim.App.Presentation.Tunnel;

/// <summary>
/// Stable seed derivation from the stream-identity branch axis. FNV-1a 64-bit over UTF-16 code
/// units (low byte, then high byte, per char) — deterministic across processes and platforms,
/// unlike string.GetHashCode.
/// </summary>
internal static class TunnelBoreSeedPolicy
{
    private const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    internal static long SeedFor(string? branchId)
    {
        var branch = string.IsNullOrWhiteSpace(branchId) ? "main" : branchId.Trim();
        var hash = OffsetBasis;
        foreach (var ch in branch)
        {
            hash = (hash ^ (byte)(ch & 0xFF)) * Prime;
            hash = (hash ^ (byte)(ch >> 8)) * Prime;
        }

        return unchecked((long)hash);
    }
}
```

- [ ] **Step 4: Run tests, verify pass** (same filter). Expected: PASS (4 tests; see the golden-
value note in Step 1 if the constant needed correction).

---

### Task 4: Interactive-window guard (picking never sees the bend)

`TunnelRayHitMapper` is a pure parametric primitive — the wall-pick Z clip range comes from its
single production caller, `TunnelPresentationBinder.Input.cs:670`:
`TunnelRayHitMapper.TryIntersectCylinder(ray, CorridorSurfaceRadius, ThroatZ, MouthZ, out wallLocal)`.
The guard is therefore a caller-side clip, in the same assembly as `TunnelBoreContract` — do NOT
modify `TunnelRayHitMapper` itself.

**Files:**
- Modify: `project/plugins/App.Presentation/Tunnel/TunnelBoreSpline.cs` (add one method to
  `TunnelBoreContract`)
- Modify: `project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.Input.cs:670`
- Test: `project/tests/App.Presentation.Tests/TunnelBoreSplineTests.cs` (append)

**Interfaces:**
- Produces: `internal static float InteractiveThroatZ(float currentPlaneZ)` on
  `TunnelBoreContract` — the deepest Z wall picking may accept.

- [ ] **Step 1: Write the failing test** (append to `TunnelBoreSplineTests`):

```csharp
[Fact]
public void Interactive_window_is_inside_the_straight_window()
{
    // Wall picking must never see bent geometry: the pick clip plane sits exactly at the
    // straight radius, between the throat and the current plane.
    var clip = TunnelBoreContract.InteractiveThroatZ(currentPlaneZ: -5.0f);
    Assert.Equal(-12.5f, clip, 6);
    Assert.True(clip > -20.0f);  // shallower than ThroatZ
    Assert.True(clip < -5.0f);   // deeper than the current plane
}
```

- [ ] **Step 2: Run it, verify FAIL** (method not defined):
`dotnet test project/tests/App.Presentation.Tests/App.Presentation.Tests.csproj --nologo -v minimal --filter "FullyQualifiedName~Interactive_window_is_inside_the_straight_window"`

- [ ] **Step 3: Implement.** Add to `TunnelBoreContract`:

```csharp
internal static float InteractiveThroatZ(float currentPlaneZ)
    => currentPlaneZ - (float)StraightRadius;
```

Then change `TunnelPresentationBinder.Input.cs:670` from passing `ThroatZ` to:

```csharp
return TunnelRayHitMapper.TryIntersectCylinder(
    ray,
    CorridorSurfaceRadius,
    TunnelBoreContract.InteractiveThroatZ(TunnelCameraFraming.CurrentPlaneZ),
    MouthZ,
    out wallLocal);
```

(Confirm the local names `ThroatZ`/`MouthZ` at that site resolve to the
`TunnelCameraFraming` constants; keep `MouthZ` exactly as-is.)

- [ ] **Step 4: Run both suites' pick-related tests:**
`dotnet test project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj --nologo -v minimal --filter "FullyQualifiedName~TunnelRayHitMapper|FullyQualifiedName~TunnelGestureCoordinator"`
Expected: PASS — the mapper is untouched; if any gesture test asserted picks deeper than
Z = -12.5, STOP and record it in AGENT-SUMMARY.md instead of weakening the test (that would mean
the interactive window is genuinely deeper than the straight window and the lead must re-decide
`StraightRadius`).

---

### Task 5: Bend the corridor walls

**Files:**
- Modify: `project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.Corridors.cs`
  (the depth-band wall loop at ~lines 209-236, and add two private helpers)
- Modify: `project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.cs` (bore-spline
  field + ensure method)

**Interfaces:**
- Consumes: `TunnelBoreSpline`, `TunnelBoreSegments`, `TunnelBoreSeedPolicy`,
  `TunnelBoreContract` (Tasks 1-3).
- Produces: `_boreSpline` field + `EnsureBoreSpline(string? branchId)` used by Tasks 6-7.

- [ ] **Step 1: Add the spline field and helpers.** In `TunnelPresentationBinder.cs` add:

```csharp
private TunnelBoreSpline? _boreSpline;
private long _boreSeed;

private TunnelBoreSpline EnsureBoreSpline(string? branchId)
{
    var seed = TunnelBoreSeedPolicy.SeedFor(branchId);
    if (_boreSpline is null || _boreSeed != seed)
    {
        _boreSeed = seed;
        _boreSpline = TunnelBoreSpline.Create(
            seed,
            TunnelBoreContract.StraightRadius,
            TunnelBoreContract.CurvatureCapRadPerUnit,
            maxDepth: TunnelCameraFraming.TimelineDepth);
    }

    return _boreSpline;
}
```

In `TunnelPresentationBinder.Corridors.cs` add (file-local statics):

```csharp
private static float DepthOfZ(float z) => TunnelCameraFraming.CurrentPlaneZ - z;

private static Vector3 BoreWorldPosition(TunnelBoreFrame frame)
    => new(
        (float)frame.Position.X,
        (float)frame.Position.Y,
        TunnelCameraFraming.CurrentPlaneZ + (float)frame.Position.Z);

private static Basis BoreBasis(TunnelBoreFrame frame)
    => new(
        new Vector3((float)frame.Right.X, (float)frame.Right.Y, (float)frame.Right.Z),
        new Vector3((float)frame.Up.X, (float)frame.Up.Y, (float)frame.Up.Z),
        new Vector3((float)-frame.Forward.X, (float)-frame.Forward.Y, (float)-frame.Forward.Z));
```

(Godot's basis columns are X, Y, Z axes; the bore's Forward is -Z in local space, hence the
negation on the third column. Wait — verify against the straight case: for the identity frame
(Forward=(0,0,-1)) this yields the identity basis, which must leave today's rendering
byte-identical. That is the correctness gate for the mapping.)

- [ ] **Step 2: Replace the wall band loop.** The current loop builds one mesh per depth band
with world-Z extents baked into the mesh (`BuildCylinderSectorMesh(start, span, radius,
band.NearZ, band.FarZ)`) and leaves the node at the origin. Replace the body with
segment-planned placement — geometry LOCAL, transform on the node:

```csharp
var spline = EnsureBoreSpline(ResolveActiveBranchId());
for (var depthBand = 0; depthBand < depthBands.Count; depthBand++)
{
    var band = depthBands[depthBand];
    var segments = TunnelBoreSegments.Plan(
        spline,
        DepthOfZ(band.NearZ),
        DepthOfZ(band.FarZ),
        TunnelBoreContract.MaxSegmentLength);
    for (var s = 0; s < segments.Count; s++)
    {
        var segment = segments[s];
        var wallMesh = BuildCylinderSectorMesh(
            start,
            span,
            CorridorSurfaceRadius,
            (float)segment.HalfLength,
            (float)-segment.HalfLength);
        if (wallMesh is null)
            continue;

        var wall = new MeshInstance3D
        {
            Name = $"Corridor_{SafeNodeName(slot.Descriptor.SphereId)}_{SafeNodeName(slot.Descriptor.LayerId)}_Depth{depthBand}_Seg{s}",
            Mesh = wallMesh,
            MaterialOverride = BuildCorridorDepthMaterial(color, band.DepthFraction),
            Position = BoreWorldPosition(segment.Frame),
            Basis = BoreBasis(segment.Frame),
        };
        _corridorsRoot!.AddChild(wall);
        _corridorNodes.Add(new CorridorWallBinding(
            wall,
            slot.Descriptor,
            slot.IsFocused,
            band.DepthFraction));
    }
}
```

`ResolveActiveBranchId()`: add a small private helper that returns the branch axis of the first
corridor slot's `LayerTrackDescriptor.StreamId` (open
`project/contracts/App.World/Composition/LayerTrackDescriptor.cs` to confirm the exact property
name for the branch axis) with `null` fallback when no slots exist. All descriptors share one
branch today; do not add any cross-branch logic.

IMPORTANT mesh-local check: `BuildCylinderSectorMesh` today receives world-Z near/far. Passing
`(+HalfLength, -HalfLength)` makes the mesh symmetric about its node origin; combined with
`Position`/`Basis` above, a straight-window segment must land at EXACTLY the geometry the old
code produced (same vertices in world space). Read `BuildCylinderSectorMesh` to confirm it has
no other dependency on absolute Z (if it derives UVs or tone from Z, parameterize with the
band's `DepthFraction`, which you already have).

- [ ] **Step 3: Run the presentation suites** (no new tests in this task — the pure modules are
tested; the binder is exercised by the windowed gate):
`dotnet test project/tests/App.Presentation.Tests/App.Presentation.Tests.csproj --nologo -v minimal`
Expected: PASS, all existing tests unchanged.

---

### Task 6: Bend the filmstrip frames

**Files:**
- Modify: `project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.Corridors.cs`
  (`BuildFilmstripFrames`, ~lines 303-333)

**Interfaces:**
- Consumes: `EnsureBoreSpline`, `BoreWorldPosition`, `BoreBasis`, `DepthOfZ` (Task 5).

- [ ] **Step 1: Replace the frame-center computation.** Current code:

```csharp
var frameCenter = new Vector3(
    Mathf.Cos(rad) * (CorridorSurfaceRadius - 0.55f),
    Mathf.Sin(rad) * (CorridorSurfaceRadius - 0.55f),
    z);
var frameRoot = new Node3D { Name = ..., Position = frameCenter };
```

Replace with lateral offset applied IN the bore frame:

```csharp
var boreFrame = EnsureBoreSpline(ResolveActiveBranchId()).Evaluate(DepthOfZ(z));
var lateralX = Mathf.Cos(rad) * (CorridorSurfaceRadius - 0.55f);
var lateralY = Mathf.Sin(rad) * (CorridorSurfaceRadius - 0.55f);
var frameCenter = BoreWorldPosition(boreFrame)
    + (BoreBasis(boreFrame).X * lateralX)
    + (BoreBasis(boreFrame).Y * lateralY);
var frameRoot = new Node3D
{
    Name = $"Frame_{SafeNodeName(descriptor.SphereId)}_{SafeNodeName(descriptor.LayerId)}_{fs.Index}_{fs.Tick}",
    Position = frameCenter,
    Basis = BoreBasis(boreFrame),
};
```

For depths inside the straight window the result is bit-identical to today (identity basis,
same Z), so near-field filmstrips — including everything the fine-preview scheduler touches —
are untouched.

- [ ] **Step 2: Run both suites:**
`dotnet test project/tests/App.Presentation.Tests/App.Presentation.Tests.csproj --nologo -v minimal && dotnet test project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj --nologo -v minimal`
Expected: PASS.

---

### Task 7: Bend the dark shell

**Files:**
- Modify: `project/plugins/App.Presentation/Tunnel/TunnelPresentationBinder.cs`
  (`BuildDarkShell`, ~line 786)

**Interfaces:**
- Consumes: `EnsureBoreSpline`, `TunnelBoreSegments`, and the Task-5 helpers (move
  `DepthOfZ`/`BoreWorldPosition`/`BoreBasis` into the MAIN binder partial if Task 5 placed them
  in Corridors.cs, so both partials share one copy — they are one partial class, so file
  placement is a readability choice; keep exactly one definition).

- [ ] **Step 1: Apply the same segment treatment** to the shell bands from
`TunnelShellDepthPolicy.Plan(MouthZ, ThroatZ)`: bands entirely at depth ≤
`TunnelBoreContract.StraightRadius` (including everything behind the current plane toward the
mouth, whose depth is negative) keep today's exact single-mesh path — DO NOT change the
mouth-side shell at all. Bands beyond the straight radius are planned via
`TunnelBoreSegments.Plan` and placed with local-extent meshes + `Position`/`Basis`, mirroring
Task 5's loop verbatim (same naming suffix `_Seg{s}`).

Note: `TunnelBoreSegments.Plan` returns nothing for `farDepth <= nearDepth`, and negative-depth
(mouth-side) bands must simply bypass the planner — guard with
`if (DepthOfZ(band.FarZ) <= TunnelBoreContract.StraightRadius) { /* legacy single-mesh path */ }`.

- [ ] **Step 2: Run the full presentation suite.** Expected: PASS.

---

### Task 8: Full-suite gate + handoff summary

- [ ] **Step 1:**
`dotnet test project/tests/App.Presentation.Tests/App.Presentation.Tests.csproj --nologo -v minimal`
Expected: PASS — 232 pre-existing + 18 new (8 spline, 5 segments, 4 seed, 1 interactive-window)
= 250; report the exact count.
- [ ] **Step 2:**
`dotnet test project/tests/App.Timeline.Tests/App.Timeline.Tests.csproj --nologo -v minimal`
Expected: PASS — 339, unchanged (Task 4 touches no seam code).
- [ ] **Step 3:** Write `AGENT-SUMMARY.md` at the repo root: files changed, RED→GREEN evidence
(exact test summary lines), the golden-seed note if applicable, any place where the actual code
shape forced a deviation from this plan (name the deviation explicitly — do not silently adapt),
and confirmation that nothing was committed.

---

## Out of scope (do not touch)

Camera, instrument rings/readouts, input relays beyond the Task-4 guard, scrub/fine-preview
scheduling, planet zoom/occlusion, the 2D face, bundle manifests, any engine repo change, any
branch/fork/junction rendering (slice 3), flight mode.
