using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using FantaSim.App.NodeGraph;
using FantaSim.App.World.Composition;
using FantaSim.App.World.GenerationGraph;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// P4b parity contract: the graph-driven regime layer-generation nodes (magma-ocean, stagnant-lid)
/// must delegate to the SAME composition path (<see cref="FieldValueResolver.Resolve"/>) the
/// composition runtime uses today, never a divergent second implementation. These tests prove that
/// by running both paths with identical geometry and comparing per-cell field values.
/// </summary>
public sealed class RegimeLayerGenerationParityTests
{
    private const int FixedSeed = 7;
    private const int FixedFrequency = 4;
    private const long MagmaOceanTick = 500_000;
    private const long StagnantLidTick = 1_500_000;

    // ---------------------------------------------------------------------
    // Behavior 1: the default magma-ocean and stagnant-lid graphs compile
    // and execute headlessly through the GraphExecutor with the
    // WorldFunctionProvider, emitting a regime-layer product from the
    // generate node.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task DefaultMagmaOceanGraph_RunsThroughExecutor_AndEmitsRegimeLayerProduct()
    {
        var run = await RunRegimeGraphAsync("magma-ocean", MagmaOceanTick);

        Assert.Equal(WorldGenerationGraphDefaults.GeosphereMagmaOceanGraphId, run.SourceGraphId);
        var generateProduct = Assert.Single(run.Products, p => p.NodeId == "generate");
        Assert.Equal(WorldFunctionProvider.MagmaOceanGenerate, generateProduct.FunctionId);
        Assert.Equal("geosphere.magma-ocean.generate", generateProduct.FunctionId);
        Assert.Contains("magma-ocean.geosphere.magma-ocean", generateProduct.ProductAddress);
        Assert.Equal(MagmaOceanTick, generateProduct.Payload["canonicalTick"]?.GetValue<long>());
        Assert.True(generateProduct.Payload["cellCount"]?.GetValue<int>() > 0);
    }

    [Fact]
    public async Task DefaultStagnantLidGraph_RunsThroughExecutor_AndEmitsRegimeLayerProduct()
    {
        var run = await RunRegimeGraphAsync("stagnant-lid", StagnantLidTick);

        Assert.Equal(WorldGenerationGraphDefaults.GeosphereStagnantLidGraphId, run.SourceGraphId);
        var generateProduct = Assert.Single(run.Products, p => p.NodeId == "generate");
        Assert.Equal(WorldFunctionProvider.StagnantLidGenerate, generateProduct.FunctionId);
        Assert.Contains("stagnant-lid.geosphere.stagnant-lid", generateProduct.ProductAddress);
        Assert.Equal(StagnantLidTick, generateProduct.Payload["canonicalTick"]?.GetValue<long>());
        Assert.True(generateProduct.Payload["cellCount"]?.GetValue<int>() > 0);
    }

    // ---------------------------------------------------------------------
    // Behavior 2: parity. The field values the generate node emits MUST
    // equal the values from the composition's canonical Resolve path with
    // the same geometry. This is the structural invariant: the graph node
    // calls ResolveRegimeLayerFields, so the values are identical by
    // construction; this test proves it by running both paths.
    // ---------------------------------------------------------------------
    [Theory]
    [InlineData("magma-ocean", MagmaOceanTick)]
    [InlineData("stagnant-lid", StagnantLidTick)]
    public async Task GenerateNodeFields_EqualDirectCompositionResolve_ForSameGeometry(string regimeId, long tick)
    {
        var run = await RunRegimeGraphAsync(regimeId, tick);
        var generateProduct = Assert.Single(run.Products, p => p.NodeId == "generate");
        var generateFields = ExtractFieldArrays(generateProduct.Payload);

        var (layer, geometry, handoff) = BuildCompositionPath(regimeId, tick);
        var directValues = WorldFunctionProvider.ResolveRegimeLayerFields(layer, geometry, tick, handoff);
        var directFields = ExtractFieldArrays(directValues);

        Assert.Equal(directFields.Keys.OrderBy(k => k), generateFields.Keys.OrderBy(k => k));
        foreach (var fieldId in directFields.Keys)
        {
            Assert.True(generateFields.TryGetValue(fieldId, out var generateValues),
                $"generate node missing field {fieldId}");
            Assert.Equal(directFields[fieldId], generateValues);
        }
    }

    // ---------------------------------------------------------------------
    // Behavior 3: determinism. Same inputs -> identical products, stable
    // graph revision. The graph family is rebuilt twice and the generate
    // product's fields must match bit-for-bit.
    // ---------------------------------------------------------------------
    [Theory]
    [InlineData("magma-ocean", MagmaOceanTick)]
    [InlineData("stagnant-lid", StagnantLidTick)]
    public async Task GenerateNodeFields_AreDeterministic_AcrossRepeatedRuns(string regimeId, long tick)
    {
        var firstRun = await RunRegimeGraphAsync(regimeId, tick);
        var firstProduct = Assert.Single(firstRun.Products, p => p.NodeId == "generate");
        var firstFields = ExtractFieldArrays(firstProduct.Payload);

        var secondRun = await RunRegimeGraphAsync(regimeId, tick);
        var secondProduct = Assert.Single(secondRun.Products, p => p.NodeId == "generate");
        var secondFields = ExtractFieldArrays(secondProduct.Payload);

        Assert.Equal(firstRun.Family.Revision, secondRun.Family.Revision);
        Assert.Equal(firstFields.Count, secondFields.Count);
        foreach (var fieldId in firstFields.Keys)
        {
            Assert.True(secondFields.TryGetValue(fieldId, out var secondValues),
                $"second run missing field {fieldId}");
            Assert.Equal(firstFields[fieldId], secondValues);
        }
    }

    private static async Task<RegimeGraphRun> RunRegimeGraphAsync(string regimeId, long tick)
    {
        var family = WorldGenerationGraphDefaults.BuildFamily();
        var source = WorldGenerationGraphFamilySource.ForRegime(
            "world-generation",
            family,
            WorldRegimeScheduleKinds.Sphere,
            regimeId,
            tick: tick,
            sphereId: WorldGenerationGraphDefaults.GeosphereSphereId);

        var compiled = source.CompileForExecution();
        var run = await new WorldGenerationGraphRunner(new[] { new WorldFunctionProvider() })
            .RunAsync(compiled.Document, new JsonObject { ["canonicalTick"] = tick });

        return new RegimeGraphRun(source.Graph.GraphId, family, run);
    }

    private static (IFieldProducer layer, WorldGlobeGeometry geometry, SphereHandoff handoff) BuildCompositionPath(
        string regimeId, long tick)
    {
        var onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        var geometry = WorldFunctionProvider.BuildRegimeLayerGeometry(FixedSeed, FixedFrequency, tick, onsetTick);

        IFieldProducer layer = regimeId == "magma-ocean"
            ? new GeosphereMagmaOceanLayer()
            : new GeosphereStagnantLidLayer(plateOnsetTick: onsetTick);

        var handoff = new SphereHandoff(
            Tick: tick,
            SourceBodyId: "protoplanet",
            TotalMassKg: 5.972e24,
            BulkCompositionFractions: new[]
            {
                new MaterialCompositionFraction("silicate", 0.68),
                new MaterialCompositionFraction("iron", 0.30),
                new MaterialCompositionFraction("volatile", 0.02),
            },
            RetainedHeatJ: 5.972e31,
            RetainedVolatileMassKg: 5.972e24 * 0.02,
            AngularMomentum: new UnifyMaths.Vector3D(0, 0, 0),
            LatentSubstrateSeed: $"geosphere/seed-{FixedSeed}");

        return (layer, geometry, handoff);
    }

    private static Dictionary<string, double[]> ExtractFieldArrays(JsonObject payload)
    {
        var fields = Assert.IsType<JsonObject>(payload["fields"]);
        var result = new Dictionary<string, double[]>();
        foreach (var property in fields)
        {
            var arr = Assert.IsType<JsonArray>(property.Value);
            var values = new double[arr.Count];
            for (var i = 0; i < arr.Count; i++)
                values[i] = arr[i]!.GetValue<double>();
            result[property.Key] = values;
        }
        return result;
    }

    private static Dictionary<string, double[]> ExtractFieldArrays(WorldFieldValues values)
    {
        var result = new Dictionary<string, double[]>(values.Scalars.Count);
        foreach (var scalar in values.Scalars)
            result[scalar.Field.Value] = scalar.Values.ToArray();
        return result;
    }

    private sealed record RegimeGraphRun(
        string SourceGraphId,
        WorldGenerationGraphFamilyDocument Family,
        WorldGenerationGraphRunOutput Run)
    {
        public IReadOnlyList<WorldGenerationGraphProduct> Products => Run.Products;
    }
}