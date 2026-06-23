using System.Linq;
using FantaSim.App.NodeGraph;
using FantaSim.App.World.GenerationGraph;
using Xunit;

namespace FantaSim.App.World.Tests;

public sealed class WorldGenerationGraphFamilyContractTests
{
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
