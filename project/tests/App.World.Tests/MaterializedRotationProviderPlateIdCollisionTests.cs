using FantaSim.App.World.Crust;
using FantaSim.Geosphere.Plate.Reconstruction;
using FantaSim.Geosphere.Plate.Rotation;
using FantaSim.World.TruthStream;
using FantaSim.World.TruthStream.Core;
using Xunit;

namespace FantaSim.App.World.Tests;

public sealed class MaterializedRotationProviderPlateIdCollisionTests
{
    [Fact]
    public async Task Distinct_authored_ids_normalizing_to_same_integer_throw_at_construction()
    {
        // Defect 2: "1" and "001" are distinct authored ids that both normalize to integer plate
        // id 1. Last-wins silently dropped one plate's motion; construction must fail closed.
        const string rotText = """
            1 0 90 0 0 000
            1 10 90 0 20 000
            001 0 90 0 0 000
            001 10 90 0 30 000
            """;

        var parsed = new RotParser().Parse("collision.rot", new StringReader(rotText));
        Assert.Empty(parsed.Issues);

        var stream = new TruthStreamIdentity("collision", "main", 0, "geosphere", "plates");
        var store = new InMemoryTruthEventStore();
        await store.AppendIfHeadAsync(
            stream,
            RotationStreamImporter.ToDrafts(parsed, stream),
            expectedHead: null);
        var model = await RotationModelMaterializer.MaterializeAsync(store, stream);

        var ex = Assert.Throws<InvalidOperationException>(
            () => new MaterializedRotationProvider(model, onsetTick: 42_000_000L));

        Assert.Contains("'1'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("'001'", ex.Message, StringComparison.Ordinal);
    }
}
