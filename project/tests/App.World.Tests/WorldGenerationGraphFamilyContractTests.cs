using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.NodeGraph;
using FantaSim.App.World.GenerationGraph;
using Xunit;

namespace FantaSim.App.World.Tests;

public sealed class WorldGenerationGraphFamilyContractTests
{
    private sealed class VplanetContractProvider : INodeFunctionProvider
    {
        public List<(string FunctionId, JsonObject Payload)> Invocations { get; } = new();

        public bool Supports(string functionId)
            => functionId.StartsWith("vplanet.", System.StringComparison.Ordinal);

        public Task<JsonObject> InvokeAsync(string functionId, JsonObject payload, CancellationToken cancellationToken = default)
        {
            Invocations.Add((functionId, payload.DeepClone().AsObject()));

            var result = functionId switch
            {
                "vplanet.status" => new JsonObject
                {
                    ["status"] = new JsonObject { ["available"] = false },
                    ["ok"] = false,
                },
                "vplanet.input.build" => new JsonObject
                {
                    ["inputBundle"] = new JsonObject
                    {
                        ["job_id"] = payload["job_id"]?.DeepClone(),
                        ["rootPath"] = "/tmp/fantasim-vplanet",
                        ["primaryPath"] = "/tmp/fantasim-vplanet/vpl.in",
                        ["starBodyName"] = payload["starBodyName"]?.DeepClone(),
                        ["planetBodyName"] = payload["planetBodyName"]?.DeepClone(),
                    },
                    ["job_id"] = payload["job_id"]?.DeepClone(),
                },
                "vplanet.run" => new JsonObject
                {
                    ["runResult"] = new JsonObject
                    {
                        ["job_id"] = payload["job_id"]?.DeepClone(),
                        ["rootPath"] = "/tmp/fantasim-vplanet",
                        ["outputPath"] = "/tmp/fantasim-vplanet",
                        ["fallback"] = true,
                        ["available"] = false,
                    },
                    ["job_id"] = payload["job_id"]?.DeepClone(),
                },
                "vplanet.output.parse" => new JsonObject
                {
                    ["outputTable"] = new JsonObject
                    {
                        ["bodyName"] = payload["bodyName"]?.DeepClone(),
                        ["columns"] = new JsonArray("Time", "SemiMajorAxis"),
                        ["rows"] = new JsonArray(new JsonArray(0.0, 1.0)),
                        ["fallback"] = true,
                    },
                    ["job_id"] = payload["job_id"]?.DeepClone(),
                },
                _ => throw new System.InvalidOperationException(functionId),
            };

            return Task.FromResult(result);
        }
    }

    [Fact]
    public void DefaultFamily_CarriesCurrentRegimeAndLayerGraphTopology()
    {
        var family = WorldGenerationGraphDefaults.BuildFamily();
        var graphIds = family.Graphs
            .Select(graph => graph.GraphId)
            .Append(family.BaseGraph.GraphId)
            .ToHashSet(System.StringComparer.Ordinal);

        Assert.Equal(WorldGenerationGraphDefaults.DocumentId, family.DocumentId);
        Assert.Equal(WorldGenerationGraphDefaults.BaseGraphId, family.BaseGraph.GraphId);
        Assert.Contains(WorldGenerationGraphDefaults.FormationGraphId, graphIds);
        Assert.Contains(WorldGenerationGraphDefaults.GeosphereGraphId, graphIds);
        Assert.Contains(WorldGenerationGraphDefaults.GeosphereMagmaOceanGraphId, graphIds);
        Assert.Contains(WorldGenerationGraphDefaults.GeosphereStagnantLidGraphId, graphIds);
        Assert.Contains(WorldGenerationGraphDefaults.MobilePlateLayerGraphId, graphIds);
        Assert.Contains(WorldGenerationGraphDefaults.GeospherePlateLayerGraphId, graphIds);
        Assert.Contains(WorldGenerationGraphDefaults.GeosphereCrustLayerGraphId, graphIds);
        Assert.All(family.SubgraphBindings!, binding => Assert.Contains(binding.SubgraphId, graphIds));
        Assert.Contains(family.RegimeGraphBindings, binding =>
            binding.ScheduleKind == WorldRegimeScheduleKinds.BodyFormation
            && binding.RegimeId == "planetesimal-swarm"
            && binding.GraphId == WorldGenerationGraphDefaults.FormationGraphId);
        Assert.Contains(family.RegimeGraphBindings, binding =>
            binding.ScheduleKind == WorldRegimeScheduleKinds.Sphere
            && binding.RegimeId == "mobile-plate"
            && binding.GraphId == WorldGenerationGraphDefaults.GeosphereGraphId
            && binding.SphereId == WorldGenerationGraphDefaults.GeosphereSphereId);
    }

    [Fact]
    public void DefaultFamily_IncludesVplanetEarthTemplateGraph()
    {
        var family = WorldGenerationGraphDefaults.BuildFamily();
        var graph = Assert.Single(family.Graphs, graph => graph.GraphId == WorldGenerationGraphDefaults.VplanetEarthGraphId);

        Assert.Equal("VPLanet Earth Template", graph.Label);
        Assert.Equal(
            new[] { "vplanet_status", "vplanet_input", "vplanet_run", "vplanet_parse" },
            graph.Nodes.Select(node => node.NodeId));
        Assert.Equal(new[] { "vplanet_parse" }, graph.OutputNodeIds);
    }

    [Fact]
    public void VplanetEarthTemplate_CompilesConnectedExternalToolChain()
    {
        var family = WorldGenerationGraphDefaults.BuildFamily();
        var graph = Assert.Single(family.Graphs, graph => graph.GraphId == WorldGenerationGraphDefaults.VplanetEarthGraphId);

        var compiled = WorldGenerationGraphCompiler.Compile(graph);

        Assert.Equal("vplanet_parse", compiled.Document.SinkNodeId);
        Assert.Equal(new[] { "vplanet.input.build", "vplanet.run", "vplanet.output.parse" },
            compiled.Document.Nodes
                .Where(node => node.Id != "vplanet_status")
                .Select(node => node.FunctionId));
        Assert.Equal(2, compiled.Document.Wires.Count);
        Assert.Contains(compiled.Document.Wires, wire =>
            wire.FromNode == "vplanet_input"
            && wire.FromPort == "inputBundle"
            && wire.ToNode == "vplanet_run"
            && wire.ToPort == "inputBundle");
        Assert.Contains(compiled.Document.Wires, wire =>
            wire.FromNode == "vplanet_run"
            && wire.FromPort == "runResult"
            && wire.ToNode == "vplanet_parse"
            && wire.ToPort == "runResult");
    }

    [Fact]
    public async Task VplanetEarthTemplate_ExecutesThroughPortAlignedProviderContract()
    {
        var family = WorldGenerationGraphDefaults.BuildFamily();
        var graph = Assert.Single(family.Graphs, graph => graph.GraphId == WorldGenerationGraphDefaults.VplanetEarthGraphId);
        var compiled = WorldGenerationGraphCompiler.Compile(graph);
        var provider = new VplanetContractProvider();

        var result = await new GraphExecutor(new[] { provider }).ExecuteAsync(
            compiled.Document,
            sharedParams: new JsonObject { ["job_id"] = "vplanet-smoke" });

        Assert.Equal(
            new[] { "vplanet.status", "vplanet.input.build", "vplanet.run", "vplanet.output.parse" },
            provider.Invocations.Select(invocation => invocation.FunctionId));
        Assert.NotNull(provider.Invocations.Single(invocation => invocation.FunctionId == "vplanet.run").Payload["inputBundle"]);
        Assert.NotNull(provider.Invocations.Single(invocation => invocation.FunctionId == "vplanet.output.parse").Payload["runResult"]);

        var outputTable = Assert.IsType<JsonObject>(result["outputTable"]);
        Assert.Equal("sun", outputTable["bodyName"]?.GetValue<string>());
        Assert.True(outputTable["fallback"]?.GetValue<bool>());
    }

    [Fact]
    public void SubgraphBindings_AreNavigationMetadata_UntilNestedExecutionIsImplemented()
    {
        var source = WorldGenerationGraphFamilySource.ForRegime(
            "world-generation",
            WorldGenerationGraphDefaults.BuildFamily(),
            WorldRegimeScheduleKinds.Sphere,
            "mobile-plate",
            tick: 0,
            sphereId: WorldGenerationGraphDefaults.GeosphereSphereId);
        var navigator = Assert.IsAssignableFrom<IGraphSubgraphSource>(source);

        var parentGraph = source.CompileForExecution().Document;
        var parentSubgraph = Assert.Single(navigator.Subgraphs);

        Assert.Equal(WorldGenerationGraphDefaults.MobilePlateLayerGraphId, parentSubgraph.SubgraphId);
        Assert.Equal(new[] { "options", "crust" }, parentGraph.Nodes.Select(node => node.Id));
        Assert.DoesNotContain(parentGraph.Nodes, node => node.Id == "plate_layer");
        Assert.DoesNotContain(parentGraph.Nodes, node => node.Id == "crust_layer");

        navigator.SelectGraph(WorldGenerationGraphDefaults.MobilePlateLayerGraphId);
        var layerIndexGraph = source.CompileForExecution().Document;

        Assert.Equal(WorldGenerationGraphDefaults.MobilePlateLayerGraphId, navigator.ActiveGraphId);
        Assert.Equal(new[] { "plate_layer", "crust_layer" }, layerIndexGraph.Nodes.Select(node => node.Id));
        Assert.DoesNotContain(layerIndexGraph.Nodes, node => node.Id == "options");
        Assert.Equal(2, navigator.Subgraphs.Count);
    }
}
