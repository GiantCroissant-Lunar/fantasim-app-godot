using FantaSim.App.World.Crust;
using FantaSim.Geosphere.Plate.Reconstruction;
using FantaSim.Geosphere.Plate.Rotation;
using FantaSim.World.TruthStream;
using FantaSim.World.TruthStream.Core;
using TimeDete.Time.Primitives;
using UnifyMaths;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Lead-owned parity oracle for the committed-truth rotation adapter. Expected orientations are
/// derived analytically from the authored angles; this fixture must not call the legacy direct
/// provider or reuse production interpolation/composition helpers to calculate its expectations.
/// </summary>
public sealed class MaterializedRotationProviderParityTests
{
    private const long OnsetTick = 42_000_000L;
    private const long TicksPerMegaAnnum = 100_000L;

    [Fact]
    public async Task Increasing_ticks_sample_increasing_Ma_with_onset_relative_sign_and_order()
    {
        // Authored absolute rotation is +30 degrees at 0 Ma and +50 degrees at 10 Ma about +Z.
        // Therefore onset-relative playback is identity at onset, +10 degrees at 5 Ma, and
        // +20 degrees at 10 Ma: R_abs(t) * inverse(R_abs(0)). The moving id deliberately has
        // leading zeroes while the app queries integer plate id 1.
        const string rotText = """
            001 0 90 0 30 000
            001 10 90 0 50 000
            """;

        var parsed = new RotParser().Parse("parity.rot", new StringReader(rotText));
        Assert.Empty(parsed.Issues);

        var stream = new TruthStreamIdentity("parity", "main", 0, "geosphere", "plates");
        var store = new InMemoryTruthEventStore();
        var drafts = RotationStreamImporter.ToDrafts(parsed, stream);
        await store.AppendIfHeadAsync(stream, drafts, expectedHead: null);
        var model = await RotationModelMaterializer.MaterializeAsync(store, stream);

        var provider = new MaterializedRotationProvider(model, OnsetTick);
        AssertRotationOfUnitX(provider, OnsetTick, expectedDegrees: 0.0);
        AssertRotationOfUnitX(provider, OnsetTick + (5L * TicksPerMegaAnnum), expectedDegrees: 10.0);
        AssertRotationOfUnitX(provider, OnsetTick + (10L * TicksPerMegaAnnum), expectedDegrees: 20.0);

        // Samples before the onset map below the authored 0 Ma range and preserve the established
        // clamp, so onset-relative output remains identity.
        AssertRotationOfUnitX(provider, OnsetTick - TicksPerMegaAnnum, expectedDegrees: 0.0);
    }

    [Fact]
    public async Task Unserved_integer_plate_is_identity()
    {
        const string rotText = "001 0 90 0 30 000\n001 10 90 0 50 000";
        var parsed = new RotParser().Parse("identity.rot", new StringReader(rotText));
        var stream = new TruthStreamIdentity("parity", "main", 0, "geosphere", "plates");
        var store = new InMemoryTruthEventStore();
        await store.AppendIfHeadAsync(stream, RotationStreamImporter.ToDrafts(parsed, stream), null);
        var model = await RotationModelMaterializer.MaterializeAsync(store, stream);

        var provider = new MaterializedRotationProvider(model, OnsetTick);
        var actual = provider.RotationFromOnsetTo(77, OnsetTick + TicksPerMegaAnnum)
            .Rotate(new Vector3D(1.0, 0.0, 0.0));

        AssertClose(1.0, actual.X);
        AssertClose(0.0, actual.Y);
        AssertClose(0.0, actual.Z);
    }

    private static void AssertRotationOfUnitX(
        IPlateRotationProvider provider,
        long tick,
        double expectedDegrees)
    {
        var actual = provider.RotationFromOnsetTo(plateId: 1, tick)
            .Rotate(new Vector3D(1.0, 0.0, 0.0));
        var radians = expectedDegrees * Math.PI / 180.0;

        AssertClose(Math.Cos(radians), actual.X);
        AssertClose(Math.Sin(radians), actual.Y);
        AssertClose(0.0, actual.Z);
    }

    private static void AssertClose(double expected, double actual)
        => Assert.InRange(actual, expected - 1e-9, expected + 1e-9);
}
