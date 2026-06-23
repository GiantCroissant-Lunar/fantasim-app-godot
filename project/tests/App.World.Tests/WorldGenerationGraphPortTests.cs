using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FantaSim.App.NodeGraph;
using FantaSim.App.World.GenerationGraph;
using Xunit;

namespace FantaSim.App.World.Tests;

public sealed class WorldGenerationGraphPortTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void FamilyDocument_CarriesRegimeGraphs_Subgraphs_AndCommentBoundaries()
    {
        var baseGraph = MakeView("world.base", "Base");
        var formationGraph = MakeView("formation.planetesimal-swarm", "Planetesimal Swarm");
        var geosphereGraph = MakeView("geosphere.mobile-plate", "Mobile Plate");

        var family = new WorldGenerationGraphFamilyDocument(
            DocumentId: "world-generation.family",
            SchemaVersion: 1,
            Revision: 2,
            BaseGraph: baseGraph,
            Graphs: new[] { formationGraph, geosphereGraph },
            RegimeGraphBindings: new[]
            {
                new WorldRegimeGraphBinding(
                    WorldRegimeScheduleKinds.BodyFormation,
                    "planetesimal-swarm",
                    formationGraph.GraphId),
                new WorldRegimeGraphBinding(
                    WorldRegimeScheduleKinds.Sphere,
                    "mobile-plate",
                    geosphereGraph.GraphId,
                    SphereId: WorldGenerationGraphDefaults.GeosphereSphereId),
            },
            GraphOverrides: Array.Empty<WorldGenerationGraphScopedOverride>(),
            LegacyOverrides: Array.Empty<WorldGenerationGraphOverride>(),
            RunHistory: Array.Empty<WorldGenerationRunHistoryEntry>(),
            UpdatedUtc: new DateTimeOffset(2026, 6, 23, 1, 0, 0, TimeSpan.Zero),
            SubgraphBindings: new[]
            {
                new WorldGenerationSubgraphBinding(
                    ParentGraphId: geosphereGraph.GraphId,
                    NodeId: "geosphere_subgraph",
                    SubgraphId: "geosphere.mobile-plate.layers"),
            });

        Assert.Equal(2, family.Graphs.Count);
        Assert.Single(family.SubgraphBindings!);
        Assert.Equal("geosphere.mobile-plate.layers", family.SubgraphBindings![0].SubgraphId);
        Assert.Equal(WorldGenerationGraphAnnotationKinds.CommentBoundary, baseGraph.Annotations![0].Kind);
    }

    [Fact]
    public void FamilyDocument_RoundTrips_WithCamelCaseJson()
    {
        var family = new WorldGenerationGraphFamilyDocument(
            DocumentId: "world-generation.family",
            SchemaVersion: 1,
            Revision: 2,
            BaseGraph: MakeView("world.base", "Base"),
            Graphs: new[] { MakeView("formation.substrate", "Formation Substrate") },
            RegimeGraphBindings: new[]
            {
                new WorldRegimeGraphBinding(
                    WorldRegimeScheduleKinds.BodyFormation,
                    "dispersed-material",
                    "formation.substrate"),
            },
            GraphOverrides: new[]
            {
                new WorldGenerationGraphScopedOverride(
                    "override_1",
                    "formation.substrate",
                    "Early accretion rate",
                    new WorldGenerationTickRange(0, 100),
                    1,
                    new[] { new WorldGenerationGraphEdit("set-param", NodeId: "source", ParamKey: "seed", ParamValue: "7") }),
            },
            LegacyOverrides: Array.Empty<WorldGenerationGraphOverride>(),
            RunHistory: Array.Empty<WorldGenerationRunHistoryEntry>(),
            UpdatedUtc: new DateTimeOffset(2026, 6, 23, 1, 0, 0, TimeSpan.Zero),
            SubgraphBindings: new[]
            {
                new WorldGenerationSubgraphBinding("formation.substrate", "sub", "formation.material-cell-set"),
            });

        var json = JsonSerializer.Serialize(family, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<WorldGenerationGraphFamilyDocument>(json, JsonOptions);

        Assert.Contains("\"documentId\"", json);
        Assert.Contains("\"subgraphBindings\"", json);
        Assert.Contains("\"annotations\"", json);
        Assert.NotNull(deserialized);
        Assert.Equal(family.DocumentId, deserialized!.DocumentId);
        Assert.Single(deserialized.SubgraphBindings!);
        Assert.Equal("formation.material-cell-set", deserialized.SubgraphBindings![0].SubgraphId);
        Assert.Single(deserialized.BaseGraph.Annotations!);
    }

    [Fact]
    public void Compiler_LowersTypedWorldGraph_ToGenericExecutableGraph()
    {
        var graph = MakeView("world.base", "Base");

        var compiled = WorldGenerationGraphCompiler.Compile(graph);

        Assert.Equal("crust", compiled.Document.SinkNodeId);
        Assert.Equal(new[] { "crust" }, compiled.OutputNodeIds);
        Assert.Equal("world.options", compiled.Document.Nodes[0].FunctionId);
        Assert.Equal(42, compiled.Document.Nodes[0].Params["seed"]!.GetValue<int>());
        Assert.Equal("crust.generate", compiled.Document.Nodes[1].FunctionId);
        Assert.Single(compiled.Document.Wires);
        Assert.Equal("options", compiled.Document.Wires[0].FromPort);
    }

    [Fact]
    public void Compiler_RejectsMissingRequiredInput()
    {
        var graph = MakeView("world.base", "Base") with
        {
            Wires = Array.Empty<WorldGenerationGraphWire>(),
        };

        var ex = Assert.Throws<ArgumentException>(() => WorldGenerationGraphCompiler.Compile(graph));
        Assert.Contains("missing required input", ex.Message);
    }

    [Fact]
    public void Compiler_RejectsUnknownOutputNode()
    {
        var graph = MakeView("world.base", "Base") with
        {
            OutputNodeIds = new[] { "ghost" },
        };

        var ex = Assert.Throws<ArgumentException>(() => WorldGenerationGraphCompiler.Compile(graph));
        Assert.Contains("Output node 'ghost' does not exist", ex.Message);
    }

    [Fact]
    public void Composer_ResolvesRegimeGraph_AndAppliesTickScopedSetParamOverride()
    {
        var baseGraph = MakeView("world.base", "Base");
        var formationGraph = MakeView("formation.planetesimal-swarm", "Planetesimal Swarm");
        var family = new WorldGenerationGraphFamilyDocument(
            DocumentId: "world-generation.family",
            SchemaVersion: 1,
            Revision: 2,
            BaseGraph: baseGraph,
            Graphs: new[] { formationGraph },
            RegimeGraphBindings: new[]
            {
                new WorldRegimeGraphBinding(
                    WorldRegimeScheduleKinds.BodyFormation,
                    "planetesimal-swarm",
                    formationGraph.GraphId),
            },
            GraphOverrides: new[]
            {
                new WorldGenerationGraphScopedOverride(
                    "boost_seed",
                    formationGraph.GraphId,
                    "Boost seed",
                    new WorldGenerationTickRange(10, 20),
                    0,
                    new[] { new WorldGenerationGraphEdit("set-param", NodeId: "source", ParamKey: "seed", ParamValue: "99") }),
            },
            LegacyOverrides: Array.Empty<WorldGenerationGraphOverride>(),
            RunHistory: Array.Empty<WorldGenerationRunHistoryEntry>(),
            UpdatedUtc: DateTimeOffset.UtcNow,
            SubgraphBindings: new[]
            {
                new WorldGenerationSubgraphBinding(formationGraph.GraphId, "crust", "formation.detail"),
            });

        var resolved = WorldGenerationGraphFamilyComposer.ResolveRegimeGraph(
            family,
            WorldRegimeScheduleKinds.BodyFormation,
            "planetesimal-swarm");
        var active = WorldGenerationGraphFamilyComposer.ComposeEffectiveGraph(family, resolved.GraphId, tick: 12);
        var inactive = WorldGenerationGraphFamilyComposer.ComposeEffectiveGraph(family, resolved.GraphId, tick: 9);
        var subgraphs = WorldGenerationGraphFamilyComposer.ListSubgraphs(family, resolved.GraphId);

        Assert.Equal(formationGraph.GraphId, resolved.GraphId);
        Assert.Empty(active.Warnings);
        Assert.Equal("99", active.Graph.Nodes[0].Parameters![0].Value);
        Assert.Equal("42", inactive.Graph.Nodes[0].Parameters![0].Value);
        Assert.Single(subgraphs);
        Assert.Equal("formation.detail", subgraphs[0].SubgraphId);
    }

    [Fact]
    public void FamilySource_SelectsRegimeGraph_ComposesTickOverrides_AndListsSubgraphs()
    {
        var family = WorldGenerationGraphDefaults.BuildFamily() with
        {
            GraphOverrides = new[]
            {
                new WorldGenerationGraphScopedOverride(
                    "fast_geosphere",
                    WorldGenerationGraphDefaults.GeosphereGraphId,
                    "Fast geosphere",
                    new WorldGenerationTickRange(10, 20),
                    0,
                    new[]
                    {
                        new WorldGenerationGraphEdit(
                            "set-param",
                            NodeId: "options",
                            ParamKey: "frequency",
                            ParamValue: "5"),
                    }),
            },
        };

        var source = WorldGenerationGraphFamilySource.ForRegime(
            "world-generation",
            family,
            WorldRegimeScheduleKinds.Sphere,
            "mobile-plate",
            tick: 12,
            sphereId: WorldGenerationGraphDefaults.GeosphereSphereId);

        Assert.Equal(WorldGenerationGraphDefaults.GeosphereGraphId, source.ActiveGraphId);
        Assert.Equal(12, source.ActiveTick);
        Assert.Empty(source.CompositionWarnings);
        Assert.Single(source.ActiveSubgraphs);
        Assert.Equal("geosphere.mobile-plate.layers", source.ActiveSubgraphs[0].SubgraphId);
        Assert.Equal(5, source.Document.Nodes.Single(node => node.Id == "options").Params["frequency"]!.GetValue<int>());

        source.SetTick(9);

        Assert.Equal(3, source.Document.Nodes.Single(node => node.Id == "options").Params["frequency"]!.GetValue<int>());
    }

    [Fact]
    public async Task FamilySource_AppliesEditsToAuthoredGraph_ThenRecomposesEffectiveGraph()
    {
        var family = WorldGenerationGraphDefaults.BuildFamily() with
        {
            GraphOverrides = new[]
            {
                new WorldGenerationGraphScopedOverride(
                    "fast_geosphere",
                    WorldGenerationGraphDefaults.GeosphereGraphId,
                    "Fast geosphere",
                    new WorldGenerationTickRange(10, 20),
                    0,
                    new[]
                    {
                        new WorldGenerationGraphEdit(
                            "set-param",
                            NodeId: "options",
                            ParamKey: "frequency",
                            ParamValue: "5"),
                    }),
            },
        };
        var source = WorldGenerationGraphFamilySource.ForRegime(
            "world-generation",
            family,
            WorldRegimeScheduleKinds.Sphere,
            "mobile-plate",
            tick: 12,
            sphereId: WorldGenerationGraphDefaults.GeosphereSphereId);
        var changed = 0;
        source.Changed += () => changed++;

        await source.ApplyEditAsync(new GraphEdit.SetParam("options", "frequency", JsonValue.Create(4)));

        var authoredParam = source.Family.Graphs.Single(graph => graph.GraphId == WorldGenerationGraphDefaults.GeosphereGraphId)
            .Nodes.Single(node => node.NodeId == "options")
            .Parameters!.Single(parameter => parameter.Key == "frequency");

        Assert.Equal(1, changed);
        Assert.Equal(family.Revision + 1, source.Family.Revision);
        Assert.Equal("4", authoredParam.Value);
        Assert.Equal(5, source.Document.Nodes.Single(node => node.Id == "options").Params["frequency"]!.GetValue<int>());

        source.SetTick(9);

        Assert.Equal(4, source.Document.Nodes.Single(node => node.Id == "options").Params["frequency"]!.GetValue<int>());
    }

    [Fact]
    public async Task FamilySource_CompilesSelectedRegimeGraphThroughGenericExecutor()
    {
        var source = WorldGenerationGraphFamilySource.ForRegime(
            "world-generation",
            WorldGenerationGraphDefaults.BuildFamily(),
            WorldRegimeScheduleKinds.Sphere,
            "mobile-plate",
            tick: 0,
            sphereId: WorldGenerationGraphDefaults.GeosphereSphereId);

        var compiled = source.CompileForExecution();
        var result = await new GraphExecutor(new[] { new WorldFunctionProvider() }).ExecuteAsync(compiled.Document);

        Assert.Equal(WorldGenerationGraphDefaults.GeosphereGraphId, source.Graph.GraphId);
        Assert.Equal("Mobile Plate Geosphere", source.Graph.Label);
        Assert.Equal(WorldFunctionProvider.CrustGenerate, result["function"]?.GetValue<string>());
        Assert.Equal(3, result["frequency"]?.GetValue<int>());
    }

    [Fact]
    public void ScopeKeyAndProductAddress_KeepCacheIdentitySeparateFromProductPath()
    {
        var key = new WorldGenerationGraphExecutionScopeKey(
            LifecycleKind: WorldRegimeScheduleKinds.BodyFormation,
            RegimeId: "planetesimal-swarm",
            GraphId: "formation.planetesimal-swarm",
            GraphRevision: 7,
            ScheduleRevision: 3,
            Variant: "base",
            Branch: "main");

        var address = new WorldGenerationProductAddress(
            Variant: "base",
            Branch: "main",
            Domain: "formation",
            Product: "body-set",
            Tick: 500);

        Assert.Equal("body-formation:planetesimal-swarm:formation.planetesimal-swarm:G7:S3:base:main", key.ToCacheKey());
        Assert.Equal("/base/main/formation/body-set@500", address.ToPath());
        Assert.Throws<ArgumentException>(() => new WorldGenerationGraphExecutionScopeKey(
            "body:formation", "regime", "graph", 1, 1, "base", "main"));
        Assert.Throws<ArgumentException>(() => new WorldGenerationProductAddress(
            "base", "main/branch", "formation", "body-set", 500));
    }

    [Fact]
    public void NodeCatalog_ContainsCurrentExecutableWorldGraphNodes()
    {
        var options = WorldGenerationNodeCatalog.Find(WorldFunctionProvider.WorldOptions);
        var crust = WorldGenerationNodeCatalog.Find(WorldFunctionProvider.CrustGenerate);

        Assert.NotNull(options);
        Assert.NotNull(crust);
        Assert.Equal("options", options!.Outputs[0].PortId);
        Assert.Equal("options", crust!.Inputs[0].PortId);
        Assert.True(crust.IsExpensive);
    }

    [Fact]
    public async Task DefaultWorldGraphSource_CompilesAndRunsThroughGenericExecutor()
    {
        var source = new WorldGenerationGraphSource(
            "world-generation",
            WorldGenerationGraphDefaults.BuildCrustGraph());

        var compiled = source.CompileForExecution();
        var result = await new GraphExecutor(new[] { new WorldFunctionProvider() }).ExecuteAsync(compiled.Document);

        Assert.Equal(2, compiled.Document.Nodes.Count);
        Assert.Equal("crust", compiled.Document.SinkNodeId);
        Assert.Equal(WorldFunctionProvider.WorldOptions, compiled.Document.Nodes[0].FunctionId);
        Assert.Equal(WorldFunctionProvider.CrustGenerate, compiled.Document.Nodes[1].FunctionId);
        Assert.Equal(WorldFunctionProvider.CrustGenerate, result["function"]?.GetValue<string>());
        Assert.Equal(3, result["frequency"]?.GetValue<int>());
        Assert.True(result["activeBoundaries"]?.GetValue<bool>());
    }

    [Fact]
    public async Task WorldGraphSource_AppliesGenericParamEditsToTypedGraphAndProjection()
    {
        var source = new WorldGenerationGraphSource(
            "world-generation",
            WorldGenerationGraphDefaults.BuildCrustGraph());
        var changed = 0;
        source.Changed += () => changed++;

        await source.ApplyEditAsync(new GraphEdit.SetParam("options", "frequency", JsonValue.Create(4)));

        var typedParam = source.Graph.Nodes.Single(node => node.NodeId == "options")
            .Parameters!
            .Single(parameter => parameter.Key == "frequency");
        var projectedNode = source.Document.Nodes.Single(node => node.Id == "options");

        Assert.Equal(1, changed);
        Assert.Equal("4", typedParam.Value);
        Assert.Equal(4, projectedNode.Params["frequency"]!.GetValue<int>());
    }

    [Fact]
    public async Task WorldGraphSource_AllowsAuthoringIncompleteNodesButExecutionCompileRejectsThem()
    {
        var source = new WorldGenerationGraphSource(
            "world-generation",
            WorldGenerationGraphDefaults.BuildCrustGraph());

        await source.ApplyEditAsync(new GraphEdit.AddNode(new GraphNode(
            "extra_crust",
            WorldFunctionProvider.CrustGenerate,
            new JsonObject())));

        Assert.Contains(source.Graph.Nodes, node => node.NodeId == "extra_crust");
        Assert.Contains(source.Document.Nodes, node => node.Id == "extra_crust");
        Assert.Throws<ArgumentException>(() => source.CompileForExecution());
    }

    [Fact]
    public async Task WorldGraphSource_RemoveNodeUpdatesWiresAndAnnotations()
    {
        var source = new WorldGenerationGraphSource(
            "world-generation",
            WorldGenerationGraphDefaults.BuildCrustGraph());

        await source.ApplyEditAsync(new GraphEdit.RemoveNode("options"));

        Assert.DoesNotContain(source.Graph.Nodes, node => node.NodeId == "options");
        Assert.Empty(source.Graph.Wires);
        Assert.DoesNotContain(source.Graph.Annotations![0].NodeIds, id => id == "options");
    }

    [Fact]
    public async Task WorldGraphSource_RejectsRemovingOutputNode()
    {
        var source = new WorldGenerationGraphSource(
            "world-generation",
            WorldGenerationGraphDefaults.BuildCrustGraph());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            source.ApplyEditAsync(new GraphEdit.RemoveNode("crust")));
    }

    private static WorldGenerationGraphView MakeView(string graphId, string label)
    {
        var source = new WorldGenerationGraphNode(
            NodeId: "source",
            TypeId: "world.options",
            Label: "World Options",
            Category: "source",
            IsSideEffect: false,
            IsExpensive: false,
            Inputs: Array.Empty<WorldGenerationGraphPort>(),
            Outputs: new[] { new WorldGenerationGraphPort("options", "Options", "world/options", Required: false) },
            Parameters: new[] { new WorldGenerationGraphParameter("seed", "Seed", "42", "int") });

        var crust = new WorldGenerationGraphNode(
            NodeId: "crust",
            TypeId: "crust.generate",
            Label: "Crust Generation",
            Category: "geosphere",
            IsSideEffect: false,
            IsExpensive: true,
            Inputs: new[] { new WorldGenerationGraphPort("options", "Options", "world/options", Required: true) },
            Outputs: new[] { new WorldGenerationGraphPort("result", "Result", "world/result", Required: false) });

        return new WorldGenerationGraphView(
            GraphId: graphId,
            Label: label,
            Description: $"Description of {label}",
            Nodes: new[] { source, crust },
            Wires: new[] { new WorldGenerationGraphWire("source", "options", "crust", "options", "world/options") },
            Annotations: new[]
            {
                new WorldGenerationGraphAnnotation(
                    AnnotationId: "comment_1",
                    Kind: WorldGenerationGraphAnnotationKinds.CommentBoundary,
                    Label: "Geosphere setup",
                    Bounds: new WorldGenerationGraphBounds(0, 0, 320, 180),
                    NodeIds: new[] { "source", "crust" },
                    Text: "Inputs that drive the geosphere recipe.",
                    Color: "#6ea8fe"),
            },
            OutputNodeIds: new[] { "crust" });
    }
}
