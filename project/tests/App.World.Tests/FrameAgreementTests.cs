using System;
using System.Collections.Generic;
using System.Linq;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Globe;
using FantaSim.Cartography.Globe;
using FantaSim.Cartography.Shared;
using FantaSim.World.Contracts.Units;
using UnifyCell;
using UnifyGeometry.Spherical;
using UnifyMaths;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Frame-agreement regression: the typed boundary arcs and the plate caps must be derived at the
/// SAME tick. The failure mode this pins: arcs built at the playhead tick while caps sit at onset
/// (or are faked with a clamped cosmetic rotation) — a convergent ribbon then crosses a plate's
/// interior by up to several degrees.
///
/// <para>
/// The assertion is geometric and robust against the structural offset between arc sample points
/// (midpoints of shared cell edges) and cap vertices (shared cell corners): for each arc point at
/// tick T, the angular distance to the nearest cap vertex of one of its two plates, when the caps
/// are built at the SAME tick T, must be NO LARGER than the distance when the caps are built at
/// onset. When the frames disagree (BuildGlobeAt ignores the tick and returns onset geometry), the
/// two distances are identical and the delta is zero — the test fails. When the frames agree, the
/// caps move with the arcs and the same-tick distance is strictly smaller.
/// </para>
/// </summary>
public sealed class FrameAgreementTests
{
    private const int AppSeed = 7;
    private const int AppFrequency = 3;

    private static GlobeReconstructor BuildAppReconstructor(out long onsetTick)
    {
        onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        var roster = OnsetRoster.Build(AppSeed, onsetTick, AppFrequency);
        var schedule = SphereRegimeScheduleDefaults.GeosphereFor(onsetTick);
        return GlobeReconstructor.FromOnsetRoster(roster, onsetTick, schedule, AppFrequency);
    }

    private static Dictionary<int, PlateCap> BuildCapsAtTick(GlobeReconstructor model, long tick)
    {
        var snapshot = model.BuildGlobeAt(tick);
        var surfaces = new GlobePlateSurfaces(snapshot, noise: new NoiseParams(Amplitude: 0.0));
        var elevations = new double[snapshot.CellCount];
        return surfaces.BuildSurfaces(elevations, exaggeration: 0.0)
            .ToDictionary(c => c.PlateId);
    }

    private static double NearestVertexAngularDistance(Vector3D epUnit, PlateCap cap)
    {
        double best = double.PositiveInfinity;
        for (int v = 0; v < cap.Surface.VertexCount; v++)
        {
            var p = cap.Surface.Positions[v];
            double len = Math.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z);
            if (len < 1e-9) continue;
            double dot = Math.Clamp(Vector3D.Dot(epUnit, new Vector3D(p.X / len, p.Y / len, p.Z / len)), -1.0, 1.0);
            double ang = Math.Acos(dot);
            if (ang < best) best = ang;
        }
        return best;
    }

    [Fact]
    public void Caps_built_at_the_arc_tick_are_closer_to_the_arcs_than_caps_built_at_onset()
    {
        var model = BuildAppReconstructor(out long onsetTick);
        // 8 Ma past onset — well beyond the retired 0.08 rad preview cap, so the old frame
        // disagreement (caps at onset, arcs at tick) produces a clear multi-degree gap.
        long tick = onsetTick + 8 * UnitConverter.TicksPerMegaAnnum;

        var arcs = model.BuildBoundaryArcsAt(tick);
        Assert.NotEmpty(arcs);

        var capsAtTick = BuildCapsAtTick(model, tick);
        var capsAtOnset = BuildCapsAtTick(model, onsetTick);

        // When the frames agree, every arc point is closer to the same-tick caps than to the onset
        // caps by a margin that reflects the plate drift over 8 Ma (~0.16 rad for the fastest
        // plate). When the frames disagree, BuildGlobeAt(tick) returns onset geometry, so the two
        // cap sets are identical and the margin is zero. Require a positive mean margin to fail
        // the no-rotation (disagreement) case cleanly while absorbing per-point structural noise.
        double marginSum = 0.0;
        int marginCount = 0;
        foreach (var arc in arcs)
        {
            if (!capsAtTick.ContainsKey(arc.PlateA) || !capsAtTick.ContainsKey(arc.PlateB)) continue;
            var tickA = capsAtTick[arc.PlateA];
            var tickB = capsAtTick[arc.PlateB];
            var onsetA = capsAtOnset[arc.PlateA];
            var onsetB = capsAtOnset[arc.PlateB];

            foreach (var endpoint in arc.Points)
            {
                double elen = Math.Sqrt(endpoint.X * endpoint.X + endpoint.Y * endpoint.Y + endpoint.Z * endpoint.Z);
                var epUnit = new Vector3D(endpoint.X / elen, endpoint.Y / elen, endpoint.Z / elen);

                double distTick = Math.Min(NearestVertexAngularDistance(epUnit, tickA), NearestVertexAngularDistance(epUnit, tickB));
                double distOnset = Math.Min(NearestVertexAngularDistance(epUnit, onsetA), NearestVertexAngularDistance(epUnit, onsetB));

                marginSum += distOnset - distTick;
                marginCount++;
            }
        }

        Assert.True(marginCount > 0, "no arc points were compared");
        double meanMargin = marginSum / marginCount;
        // The fastest plate drifts ~0.16 rad over 8 Ma; the mean margin across all arcs/points is
        // smaller (many arcs are between slow plates), but strictly positive when frames agree.
        // The no-rotation disagreement gives exactly 0. A 0.01 rad floor cleanly separates them.
        Assert.True(meanMargin > 0.01,
            $"mean (onset-distance - tick-distance) = {meanMargin:F4} rad — caps are not closer to the " +
            $"arcs at the arc tick than at onset, so the frames disagree (BuildGlobeAt did not rotate " +
            $"cap geometry to the tick).");
    }
}