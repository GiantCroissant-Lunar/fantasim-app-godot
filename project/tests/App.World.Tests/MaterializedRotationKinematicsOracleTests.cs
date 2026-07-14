using FantaSim.App.World.Crust;
using FantaSim.Geosphere.Plate.Reconstruction;
using FantaSim.Geosphere.Plate.Rotation;
using FantaSim.World.TruthStream;
using FantaSim.World.TruthStream.Core;
using UnifyMaths;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Lead-owned kinematics oracle with a drifting rotation axis. The fixed-axis constant-rate
/// fixture in <see cref="MaterializedRotationProviderParityTests"/> cannot falsify the
/// world-frame quaternion order (co-axial deltas commute) nor central-vs-one-sided differencing
/// (constant rate makes all secants equal). Here the authored pole swings from +X to +Z, so the
/// body-frame (swapped) delta order misplaces the pole axis by ~2.9e-1 while the assertions
/// tolerate 1e-9. The below-range case pins the stationary clamp on the side the original
/// fixture never asserted.
/// </summary>
/// <remarks>
/// Expected values are lead-derived with an independent pure-Python quaternion implementation
/// (Hamilton product; shortest-path SLERP of the absolute samples; world-frame delta
/// q(after) * inverse(q(before)) over the contract's 1000-tick half-window at 100000 ticks/Ma;
/// angle = 2*atan2(|v|, clamp(w,0,1)); rate = angle/(afterTick-beforeTick)), self-checked
/// against the accepted co-axial oracle values before use. They must not be regenerated from
/// production code. The 6.25 Ma playback lock sits at a quarter point of the drifting segment,
/// where normalized-lerp differs from SLERP by ~8.0e-4.
/// </remarks>
public sealed class MaterializedRotationKinematicsOracleTests
{
    private const long OnsetTick = 42_000_000L;
    private const long TicksPerMegaAnnum = 100_000L;

    // 0 Ma identity, 5 Ma +X 20 deg, 10 Ma +Z 30 deg: the axis genuinely drifts, and the
    // onset-relative playback equals the absolute rotation because R_abs(0 Ma) is identity.
    private const string RotText = """
        001 0 90 0 0 000
        001 5 0 0 20 000
        001 10 90 0 30 000
        """;

    [Fact]
    public async Task Drifting_axis_pole_uses_world_frame_delta_order()
    {
        var provider = await BuildProviderAsync();

        var pole = provider.InstantaneousPoleAt(1, OnsetTick + (7_500L * TicksPerMegaAnnum / 1_000L));

        AssertClose(1.0, pole.Axis.Length(), 1e-12);
        AssertClose(-0.543845621787933, pole.Axis.X, 1e-9);
        AssertClose(-0.145722995165276, pole.Axis.Y, 1e-9);
        AssertClose(0.826436173181061, pole.Axis.Z, 1e-9);
        AssertClose(1.254114022533164e-06, pole.AngularRate, 1e-12);
    }

    [Fact]
    public async Task Below_authored_range_is_stationary()
    {
        var provider = await BuildProviderAsync();

        var pole = provider.InstantaneousPoleAt(1, OnsetTick - 1L);

        Assert.Equal(0.0, pole.AngularRate);
    }

    [Fact]
    public async Task Drifting_segment_playback_slerps_absolute_samples()
    {
        var provider = await BuildProviderAsync();

        // Quarter point of the 5..10 Ma segment (6.25 Ma), probe (0,1,0).
        var rotated = provider
            .RotationFromOnsetTo(plateId: 1, OnsetTick + (6_250L * TicksPerMegaAnnum / 1_000L))
            .Rotate(new Vector3D(0.0, 1.0, 0.0));

        AssertClose(-0.129997464785220, rotated.X, 1e-9);
        AssertClose(0.956949198188559, rotated.Y, 1e-9);
        AssertClose(0.259516649245652, rotated.Z, 1e-9);
    }

    private static async Task<MaterializedRotationProvider> BuildProviderAsync()
    {
        var parsed = new RotParser().Parse("drifting-kinematics.rot", new StringReader(RotText));
        Assert.Empty(parsed.Issues);

        var stream = new TruthStreamIdentity("drifting-kinematics", "main", 2, "geosphere", "plates");
        var store = new InMemoryTruthEventStore();
        await store.AppendIfHeadAsync(stream, RotationStreamImporter.ToDrafts(parsed, stream), null);
        var model = await RotationModelMaterializer.MaterializeAsync(store, stream);
        return new MaterializedRotationProvider(model, OnsetTick);
    }

    private static void AssertClose(double expected, double actual, double tolerance)
        => Assert.InRange(actual, expected - tolerance, expected + tolerance);
}
