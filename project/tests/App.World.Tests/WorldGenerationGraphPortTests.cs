using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FantaSim.App.NodeGraph;
using FantaSim.App.World.Composition;
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
    public void Composer_RemoveNodeOverridePrunesAnnotationAttachments()
    {
        var baseGraph = MakeView("world.base", "Base");
        var family = new WorldGenerationGraphFamilyDocument(
            DocumentId: "world-generation.family",
            SchemaVersion: 1,
            Revision: 2,
            BaseGraph: baseGraph,
            Graphs: Array.Empty<WorldGenerationGraphView>(),
            RegimeGraphBindings: Array.Empty<WorldRegimeGraphBinding>(),
            GraphOverrides: new[]
            {
                new WorldGenerationGraphScopedOverride(
                    "remove_source",
                    baseGraph.GraphId,
                    "Remove source",
                    new WorldGenerationTickRange(10, 20),
                    0,
                    new[] { new WorldGenerationGraphEdit("remove-node", NodeId: "source") }),
            },
            LegacyOverrides: Array.Empty<WorldGenerationGraphOverride>(),
            RunHistory: Array.Empty<WorldGenerationRunHistoryEntry>(),
            UpdatedUtc: DateTimeOffset.UtcNow);

        var active = WorldGenerationGraphFamilyComposer.ComposeEffectiveGraph(family, baseGraph.GraphId, tick: 12);
        var inactive = WorldGenerationGraphFamilyComposer.ComposeEffectiveGraph(family, baseGraph.GraphId, tick: 9);

        Assert.DoesNotContain(active.Graph.Nodes, node => node.NodeId == "source");
        Assert.Empty(active.Graph.Wires);
        Assert.DoesNotContain(active.Graph.Annotations![0].NodeIds, nodeId => nodeId == "source");
        Assert.Equal(new[] { "crust" }, active.Graph.Annotations![0].NodeIds);
        Assert.Equal(new[] { "source", "crust" }, inactive.Graph.Annotations![0].NodeIds);
    }

    [Fact]
    public void Composer_AddNodeOverrideCreatesCatalogBackedNode()
    {
        var baseGraph = MakeView("world.base", "Base");
        var family = new WorldGenerationGraphFamilyDocument(
            DocumentId: "world-generation.family",
            SchemaVersion: 1,
            Revision: 2,
            BaseGraph: baseGraph,
            Graphs: Array.Empty<WorldGenerationGraphView>(),
            RegimeGraphBindings: Array.Empty<WorldRegimeGraphBinding>(),
            GraphOverrides: new[]
            {
                new WorldGenerationGraphScopedOverride(
                    "add_layer_probe",
                    baseGraph.GraphId,
                    "Add layer probe",
                    new WorldGenerationTickRange(10, 20),
                    0,
                    new[]
                    {
                        new WorldGenerationGraphEdit(
                            "add-node",
                            NodeId: "layer_probe",
                            TypeId: WorldFunctionProvider.LayerScope),
                    }),
            },
            LegacyOverrides: Array.Empty<WorldGenerationGraphOverride>(),
            RunHistory: Array.Empty<WorldGenerationRunHistoryEntry>(),
            UpdatedUtc: DateTimeOffset.UtcNow);

        var active = WorldGenerationGraphFamilyComposer.ComposeEffectiveGraph(family, baseGraph.GraphId, tick: 12);
        var inactive = WorldGenerationGraphFamilyComposer.ComposeEffectiveGraph(family, baseGraph.GraphId, tick: 9);

        Assert.Empty(active.Warnings);
        var node = Assert.Single(active.Graph.Nodes, candidate => candidate.NodeId == "layer_probe");
        Assert.Equal(WorldFunctionProvider.LayerScope, node.TypeId);
        Assert.Equal("Layer Scope", node.Label);
        Assert.DoesNotContain(inactive.Graph.Nodes, candidate => candidate.NodeId == "layer_probe");
    }

    [Fact]
    public void Composer_AddNodeOverrideWarnsForInvalidCatalogEdits()
    {
        var baseGraph = MakeView("world.base", "Base");
        var family = new WorldGenerationGraphFamilyDocument(
            DocumentId: "world-generation.family",
            SchemaVersion: 1,
            Revision: 2,
            BaseGraph: baseGraph,
            Graphs: Array.Empty<WorldGenerationGraphView>(),
            RegimeGraphBindings: Array.Empty<WorldRegimeGraphBinding>(),
            GraphOverrides: new[]
            {
                new WorldGenerationGraphScopedOverride(
                    "bad_add_nodes",
                    baseGraph.GraphId,
                    "Bad add nodes",
                    new WorldGenerationTickRange(10, 20),
                    0,
                    new[]
                    {
                        new WorldGenerationGraphEdit("add-node", TypeId: WorldFunctionProvider.LayerScope),
                        new WorldGenerationGraphEdit("add-node", NodeId: "source", TypeId: WorldFunctionProvider.LayerScope),
                        new WorldGenerationGraphEdit("add-node", NodeId: "unknown", TypeId: "unknown.node"),
                    }),
            },
            LegacyOverrides: Array.Empty<WorldGenerationGraphOverride>(),
            RunHistory: Array.Empty<WorldGenerationRunHistoryEntry>(),
            UpdatedUtc: DateTimeOffset.UtcNow);

        var active = WorldGenerationGraphFamilyComposer.ComposeEffectiveGraph(family, baseGraph.GraphId, tick: 12);

        Assert.Equal(3, active.Warnings.Count);
        Assert.Contains(active.Warnings, warning => warning.Contains("incomplete fields", StringComparison.Ordinal));
        Assert.Contains(active.Warnings, warning => warning.Contains("already exists", StringComparison.Ordinal));
        Assert.Contains(active.Warnings, warning => warning.Contains("unknown node schema 'unknown.node'", StringComparison.Ordinal));
        Assert.DoesNotContain(active.Graph.Nodes, node => node.NodeId == "unknown");
        Assert.Equal(2, active.Graph.Nodes.Count);
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
        var navigator = Assert.IsAssignableFrom<IGraphSubgraphSource>(source);
        Assert.Equal(WorldGenerationGraphDefaults.GeosphereGraphId, navigator.ActiveGraphId);
        var activeSubgraph = Assert.Single(navigator.Subgraphs);
        Assert.Equal("crust", activeSubgraph.ParentNodeId);
        Assert.Equal(WorldGenerationGraphDefaults.MobilePlateLayerGraphId, activeSubgraph.SubgraphId);
        Assert.Equal("Mobile Plate Layers", activeSubgraph.Label);
        Assert.Equal(5, source.Document.Nodes.Single(node => node.Id == "options").Params["frequency"]!.GetValue<int>());
        var scopeKey = source.TryBuildExecutionScopeKey();
        Assert.NotNull(scopeKey);
        Assert.Equal(WorldRegimeScheduleKinds.Sphere, scopeKey!.LifecycleKind);
        Assert.Equal("mobile-plate", scopeKey.RegimeId);
        Assert.Equal(WorldGenerationGraphDefaults.GeosphereGraphId, scopeKey.GraphId);
        Assert.Equal(family.Revision, scopeKey.GraphRevision);

        navigator.SelectGraph(WorldGenerationGraphDefaults.MobilePlateLayerGraphId);

        Assert.Equal(WorldGenerationGraphDefaults.MobilePlateLayerGraphId, source.ActiveGraphId);
        Assert.Equal("Mobile Plate Layers", navigator.ActiveGraphLabel);
        Assert.Equal(2, navigator.Subgraphs.Count);
        var layerScopeKey = source.TryBuildExecutionScopeKey();
        Assert.NotNull(layerScopeKey);
        Assert.Equal(WorldRegimeScheduleKinds.Sphere, layerScopeKey!.LifecycleKind);
        Assert.Equal("mobile-plate", layerScopeKey.RegimeId);
        Assert.Equal(WorldGenerationGraphDefaults.MobilePlateLayerGraphId, layerScopeKey.GraphId);

        navigator.SelectGraph(WorldGenerationGraphDefaults.GeospherePlateLayerGraphId);

        var leafLayerScopeKey = source.TryBuildExecutionScopeKey();
        Assert.NotNull(leafLayerScopeKey);
        Assert.Equal("mobile-plate", leafLayerScopeKey!.RegimeId);
        Assert.Equal(WorldGenerationGraphDefaults.GeospherePlateLayerGraphId, leafLayerScopeKey.GraphId);

        source.SetTick(9);

        Assert.Equal("geosphere.plate", source.Document.Nodes.Single(node => node.Id == "layer_scope").Params["layerId"]!.GetValue<string>());
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
    public async Task FamilySource_AppliesAnnotationEditsToActiveAuthoredGraph()
    {
        var originalRevision = WorldGenerationGraphDefaults.BuildFamily().Revision;
        var source = WorldGenerationGraphFamilySource.ForRegime(
            "world-generation",
            WorldGenerationGraphDefaults.BuildFamily(),
            WorldRegimeScheduleKinds.Sphere,
            "mobile-plate",
            tick: 0,
            sphereId: WorldGenerationGraphDefaults.GeosphereSphereId);
        var annotation = new GraphAnnotation(
            "comment_extra",
            WorldGenerationGraphAnnotationKinds.CommentBoundary,
            "Extra comment",
            new GraphAnnotationBounds(8, 12, 300, 160),
            new[] { "options", "crust" },
            "Review this world recipe.",
            "#89c2ff");

        await source.ApplyEditAsync(new GraphEdit.AddAnnotation(annotation));

        var authoredGraph = source.Family.Graphs.Single(graph =>
            graph.GraphId == WorldGenerationGraphDefaults.GeosphereGraphId);
        var authoredAnnotation = authoredGraph.Annotations!.Single(candidate =>
            candidate.AnnotationId == "comment_extra");

        Assert.Equal(originalRevision + 1, source.Family.Revision);
        Assert.Equal("Extra comment", authoredAnnotation.Label);
        Assert.Contains(source.Annotations, candidate => candidate.AnnotationId == "comment_extra");

        await source.ApplyEditAsync(new GraphEdit.UpdateAnnotation(annotation with
        {
            Label = "Updated comment",
            Bounds = new GraphAnnotationBounds(20, 30, 340, 180),
        }));

        authoredGraph = source.Family.Graphs.Single(graph =>
            graph.GraphId == WorldGenerationGraphDefaults.GeosphereGraphId);
        authoredAnnotation = authoredGraph.Annotations!.Single(candidate =>
            candidate.AnnotationId == "comment_extra");

        Assert.Equal("Updated comment", authoredAnnotation.Label);
        Assert.Equal(340, authoredAnnotation.Bounds.Width);

        await source.ApplyEditAsync(new GraphEdit.RemoveAnnotation("comment_extra"));

        authoredGraph = source.Family.Graphs.Single(graph =>
            graph.GraphId == WorldGenerationGraphDefaults.GeosphereGraphId);

        Assert.DoesNotContain(authoredGraph.Annotations!, candidate => candidate.AnnotationId == "comment_extra");
        Assert.DoesNotContain(source.Annotations, candidate => candidate.AnnotationId == "comment_extra");
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
    public void DefaultFamily_DefinesResolvableLayerSubgraphs()
    {
        var family = WorldGenerationGraphDefaults.BuildFamily();
        var graphIds = family.Graphs
            .Select(graph => graph.GraphId)
            .Append(family.BaseGraph.GraphId)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(WorldGenerationGraphDefaults.MobilePlateLayerGraphId, graphIds);
        Assert.Contains(WorldGenerationGraphDefaults.GeospherePlateLayerGraphId, graphIds);
        Assert.Contains(WorldGenerationGraphDefaults.GeosphereCrustLayerGraphId, graphIds);
        Assert.All(family.SubgraphBindings!, binding => Assert.Contains(binding.SubgraphId, graphIds));

        var layerIndex = family.Graphs.Single(graph => graph.GraphId == WorldGenerationGraphDefaults.MobilePlateLayerGraphId);
        Assert.Equal(new[] { "plate_layer", "crust_layer" }, layerIndex.OutputNodeIds);
        Assert.Equal(WorldGenerationGraphAnnotationKinds.GroupBoundary, layerIndex.Annotations![0].Kind);

        var subgraphs = WorldGenerationGraphFamilyComposer.ListSubgraphs(
            family,
            WorldGenerationGraphDefaults.MobilePlateLayerGraphId);

        Assert.Equal(2, subgraphs.Count);
        Assert.Contains(subgraphs, binding => binding.SubgraphId == WorldGenerationGraphDefaults.GeospherePlateLayerGraphId);
        Assert.Contains(subgraphs, binding => binding.SubgraphId == WorldGenerationGraphDefaults.GeosphereCrustLayerGraphId);
    }

    [Theory]
    [InlineData("magma-ocean", WorldGenerationGraphDefaults.GeosphereMagmaOceanGraphId)]
    [InlineData("stagnant-lid", WorldGenerationGraphDefaults.GeosphereStagnantLidGraphId)]
    [InlineData("mobile-plate", WorldGenerationGraphDefaults.GeosphereGraphId)]
    public void DefaultFamily_ResolvesLiveGeosphereRegimeGraphs(string regimeId, string expectedGraphId)
    {
        var graph = WorldGenerationGraphFamilyComposer.ResolveRegimeGraph(
            WorldGenerationGraphDefaults.BuildFamily(),
            WorldRegimeScheduleKinds.Sphere,
            regimeId,
            WorldGenerationGraphDefaults.GeosphereSphereId);

        Assert.Equal(expectedGraphId, graph.GraphId);
    }

    [Fact]
    public async Task LayerScopeRegimeGraph_RunsThroughGenericExecutor()
    {
        var source = WorldGenerationGraphFamilySource.ForRegime(
            "world-generation",
            WorldGenerationGraphDefaults.BuildFamily(),
            WorldRegimeScheduleKinds.Sphere,
            "magma-ocean",
            tick: 1_234,
            sphereId: WorldGenerationGraphDefaults.GeosphereSphereId);

        var compiled = source.CompileForExecution();
        var run = await new WorldGenerationGraphRunner(new[] { new WorldFunctionProvider() })
            .RunAsync(compiled.Document, new JsonObject { ["canonicalTick"] = source.ActiveTick });
        var result = run.Sink;
        var layer = Assert.IsType<JsonObject>(result["layer"]);

        Assert.Equal(WorldFunctionProvider.LayerScope, result["function"]?.GetValue<string>());
        Assert.Equal(WorldRegimeScheduleKinds.Sphere, result["scheduleKind"]?.GetValue<string>());
        Assert.Equal(WorldGenerationGraphDefaults.GeosphereSphereId, result["sphereId"]?.GetValue<string>());
        Assert.Equal("geosphere.magma-ocean", layer["layerId"]?.GetValue<string>());
        Assert.Equal("magma-ocean", layer["regimeId"]?.GetValue<string>());
        Assert.Equal(1_234, result["canonicalTick"]?.GetValue<long>());
        Assert.Single(run.Products);
        Assert.Equal("layer_scope", run.Products[0].NodeId);
        Assert.Equal(WorldFunctionProvider.LayerScope, run.Products[0].FunctionId);
        Assert.Equal("/base/main/geosphere/magma-ocean.geosphere.magma-ocean@1234", run.Products[0].ProductAddress);
    }

    [Fact]
    public async Task BodyFormationRegimeGraph_RunsThroughGenericExecutor()
    {
        var source = WorldGenerationGraphFamilySource.ForRegime(
            "world-generation",
            WorldGenerationGraphDefaults.BuildFamily(),
            WorldRegimeScheduleKinds.BodyFormation,
            "planetesimal-swarm",
            tick: 0);

        var compiled = source.CompileForExecution();
        var result = await new GraphExecutor(new[] { new WorldFunctionProvider() }).ExecuteAsync(compiled.Document);
        var bodySet = Assert.IsType<JsonObject>(result["bodySet"]);
        var handoff = Assert.IsType<JsonObject>(result["sphereHandoff"]);
        var handoffAlias = Assert.IsType<JsonObject>(result["handoff"]);
        var composition = Assert.IsType<JsonArray>(handoff["bulkCompositionFractions"]);

        Assert.Equal(WorldGenerationGraphDefaults.FormationGraphId, source.Graph.GraphId);
        Assert.DoesNotContain(source.Graph.Nodes, node => node.TypeId == WorldFunctionProvider.CrustGenerate);
        Assert.Equal(WorldFunctionProvider.BodyFormation, compiled.Document.Nodes.Single().FunctionId);
        Assert.DoesNotContain(compiled.Document.Nodes, node => node.FunctionId == WorldFunctionProvider.CrustGenerate);
        Assert.Equal(WorldFunctionProvider.BodyFormation, result["function"]?.GetValue<string>());
        Assert.Equal(WorldRegimeScheduleKinds.BodyFormation, result["scheduleKind"]?.GetValue<string>());
        Assert.Equal("planetesimal-swarm", result["regimeId"]?.GetValue<string>());
        Assert.Equal("planetesimal-swarm", bodySet["shapeState"]!.GetValue<string>());
        Assert.Equal(10, bodySet["bodyCount"]!.GetValue<int>());
        Assert.InRange(handoff["retainedHeatJ"]!.GetValue<double>(), 5.971e31, 5.973e31);
        Assert.Equal("geosphere/seed-7", handoff["latentSubstrateSeed"]!.GetValue<string>());
        Assert.Equal(handoff["retainedHeatJ"]!.GetValue<double>(), handoffAlias["retainedHeatJ"]!.GetValue<double>());
        Assert.Equal(3, composition.Count);
        Assert.Equal("/base/main/formation/body-set@0", result["productAddress"]!.GetValue<string>());

        var typedHandoff = SphereHandoff.FromJson(handoff);
        Assert.Equal(0, typedHandoff.Tick);
        Assert.Equal("protoplanet", typedHandoff.SourceBodyId);
        Assert.InRange(typedHandoff.RetainedHeatJ, 5.971e31, 5.973e31);
        Assert.Equal("geosphere/seed-7", typedHandoff.LatentSubstrateSeed);
        Assert.Equal(3, typedHandoff.BulkCompositionFractions.Count);
    }

    [Fact]
    public async Task WorldGenerationGraphRunner_CapturesFormationProductsAndHandoff()
    {
        var source = WorldGenerationGraphFamilySource.ForRegime(
            "world-generation",
            WorldGenerationGraphDefaults.BuildFamily(),
            WorldRegimeScheduleKinds.BodyFormation,
            "planetesimal-swarm",
            tick: 0);

        var compiled = source.CompileForExecution();
        var scopeKey = source.TryBuildExecutionScopeKey();
        var run = await new WorldGenerationGraphRunner(new[] { new WorldFunctionProvider() })
            .RunAsync(compiled.Document, executionScopeKey: scopeKey);
        var commandResult = WorldGenerationGraphRunner.ToCommandResult(run);
        var products = Assert.IsType<JsonArray>(commandResult["products"]);

        Assert.Equal(WorldFunctionProvider.BodyFormation, run.Sink["function"]?.GetValue<string>());
        Assert.NotNull(scopeKey);
        Assert.Equal(scopeKey!.ToCacheKey(), run.ExecutionScopeKey!.ToCacheKey());
        Assert.Single(run.Products);
        Assert.Equal("body_formation", run.Products[0].NodeId);
        Assert.Equal("/base/main/formation/body-set@0", run.Products[0].ProductAddress);
        Assert.Equal(scopeKey.ToCacheKey(), run.Products[0].ExecutionScopeKey!.ToCacheKey());
        Assert.NotNull(run.SphereHandoff);
        Assert.InRange(run.SphereHandoff!.RetainedHeatJ, 5.971e31, 5.973e31);
        Assert.Equal(scopeKey.ToCacheKey(), commandResult["executionScopeKey"]!.GetValue<string>());
        Assert.Single(products);
        Assert.Equal("/base/main/formation/body-set@0", products[0]!["productAddress"]!.GetValue<string>());
        Assert.Equal(scopeKey.ToCacheKey(), products[0]!["executionScopeKey"]!.GetValue<string>());

        var generationRequest = WorldGenerationGraphRunner.ToGenerationRequest(run, worldId: "graph-world");

        Assert.Equal("graph-world", generationRequest.WorldId);
        Assert.Equal(scopeKey.ToCacheKey(), generationRequest.GenerationSpec);
        Assert.Equal("world-generation.graph", generationRequest.Parameters["source"]);
        Assert.Equal(1, generationRequest.Parameters["productCount"]);
        Assert.Equal(
            new[] { "/base/main/formation/body-set@0" },
            Assert.IsType<string[]>(generationRequest.Parameters["productAddresses"]));
        Assert.Equal(scopeKey.ToCacheKey(), generationRequest.Parameters["executionScopeKey"]);
        Assert.Equal(WorldRegimeScheduleKinds.BodyFormation, generationRequest.Parameters["lifecycleKind"]);
        Assert.Equal(WorldGenerationGraphDefaults.FormationGraphId, generationRequest.Parameters["graphId"]);
        Assert.Equal(0L, generationRequest.Parameters["canonicalTick"]);
        Assert.Equal("geosphere/seed-7", generationRequest.Parameters["sphereHandoffLatentSubstrateSeed"]);
    }

    [Fact]
    public void ExecutionPayload_RoundTripsSharedParams_AndAcceptsLegacyGraphPayload()
    {
        var graph = WorldGenerationGraphFamilySource.ForRegime(
                "world-generation",
                WorldGenerationGraphDefaults.BuildFamily(),
                WorldRegimeScheduleKinds.Sphere,
                "magma-ocean",
                tick: 0,
                sphereId: WorldGenerationGraphDefaults.GeosphereSphereId)
            .CompileForExecution()
            .Document;

        var payloadJson = WorldGenerationGraphExecutionPayload.Serialize(
            graph,
            new JsonObject { ["canonicalTick"] = 1_234 },
            new WorldGenerationGraphExecutionScopeKey(
                WorldRegimeScheduleKinds.Sphere,
                "magma-ocean",
                WorldGenerationGraphDefaults.GeosphereMagmaOceanGraphId,
                7,
                1,
                "base",
                "main"));
        var payload = WorldGenerationGraphExecutionPayload.Deserialize(payloadJson);
        var legacy = WorldGenerationGraphExecutionPayload.Deserialize(JsonSerializer.Serialize(graph));

        Assert.Equal(graph.SinkNodeId, payload.Graph.SinkNodeId);
        Assert.Equal(1_234, payload.SharedParams!["canonicalTick"]!.GetValue<int>());
        Assert.Equal(
            "sphere:magma-ocean:geosphere.magma-ocean:G7:S1:base:main",
            payload.ExecutionScopeKey!.ToCacheKey());
        Assert.Equal(graph.SinkNodeId, legacy.Graph.SinkNodeId);
        Assert.Null(legacy.SharedParams);
        Assert.Null(legacy.ExecutionScopeKey);
    }

    [Fact]
    public async Task RegimeFieldSampler_UsesGraphHandoffForMagmaOceanTemperature()
    {
        var source = WorldGenerationGraphFamilySource.ForRegime(
            "world-generation",
            WorldGenerationGraphDefaults.BuildFamily(),
            WorldRegimeScheduleKinds.BodyFormation,
            "planetesimal-swarm",
            tick: 0);

        var run = await new WorldGenerationGraphRunner(new[] { new WorldFunctionProvider() })
            .RunAsync(source.CompileForExecution().Document);
        var handoff = Assert.IsType<SphereHandoff>(run.SphereHandoff);
        var sampler = new WorldGenerationRegimeFieldSampler();
        var graphTemperature = sampler.ResolveMagmaOceanSurfaceTemperatureK(tick: 0, handoff);
        var fallbackTemperature = sampler.ResolveMagmaOceanSurfaceTemperatureK(tick: 0, sphereHandoff: null);
        var lowerHeatTemperature = sampler.ResolveMagmaOceanSurfaceTemperatureK(
            tick: 0,
            handoff with { RetainedHeatJ = handoff.RetainedHeatJ * 0.1 });

        Assert.Equal(fallbackTemperature, graphTemperature);
        Assert.True(lowerHeatTemperature < graphTemperature);
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
        var bodyFormation = WorldGenerationNodeCatalog.Find(WorldFunctionProvider.BodyFormation);
        var layerScope = WorldGenerationNodeCatalog.Find(WorldFunctionProvider.LayerScope);
        var crust = WorldGenerationNodeCatalog.Find(WorldFunctionProvider.CrustGenerate);

        Assert.NotNull(options);
        Assert.NotNull(bodyFormation);
        Assert.NotNull(layerScope);
        Assert.NotNull(crust);
        Assert.Equal("options", options!.Outputs[0].PortId);
        Assert.Equal("bodySet", bodyFormation!.Outputs[0].PortId);
        Assert.Equal("layer", layerScope!.Outputs[0].PortId);
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
    public async Task WorldGraphSource_AppliesAnnotationEditsToTypedGraphAndProjection()
    {
        var source = new WorldGenerationGraphSource(
            "world-generation",
            WorldGenerationGraphDefaults.BuildCrustGraph());
        var annotation = new GraphAnnotation(
            "comment_extra",
            WorldGenerationGraphAnnotationKinds.GroupBoundary,
            "Extra group",
            new GraphAnnotationBounds(10, 20, 300, 140),
            new[] { "options", "crust", "options" },
            "Grouped inputs and output.",
            "#6ea8fe");

        await source.ApplyEditAsync(new GraphEdit.AddAnnotation(annotation));

        var typedAnnotation = source.Graph.Annotations!.Single(candidate =>
            candidate.AnnotationId == "comment_extra");
        var projectedAnnotation = source.Annotations.Single(candidate =>
            candidate.AnnotationId == "comment_extra");

        Assert.Equal(WorldGenerationGraphAnnotationKinds.GroupBoundary, typedAnnotation.Kind);
        Assert.Equal(new[] { "options", "crust" }, typedAnnotation.NodeIds);
        Assert.Equal(300, projectedAnnotation.Bounds.Width);

        await source.ApplyEditAsync(new GraphEdit.UpdateAnnotation(annotation with
        {
            Label = "Updated group",
            Bounds = new GraphAnnotationBounds(30, 40, 360, 180),
            NodeIds = new[] { "crust" },
        }));

        typedAnnotation = source.Graph.Annotations!.Single(candidate =>
            candidate.AnnotationId == "comment_extra");

        Assert.Equal("Updated group", typedAnnotation.Label);
        Assert.Equal(360, typedAnnotation.Bounds.Width);
        Assert.Equal(new[] { "crust" }, typedAnnotation.NodeIds);

        await source.ApplyEditAsync(new GraphEdit.RemoveAnnotation("comment_extra"));

        Assert.DoesNotContain(source.Graph.Annotations!, candidate => candidate.AnnotationId == "comment_extra");
        Assert.DoesNotContain(source.Annotations, candidate => candidate.AnnotationId == "comment_extra");
    }

    [Fact]
    public async Task WorldGraphSource_RejectsInvalidAnnotationEdits()
    {
        var source = new WorldGenerationGraphSource(
            "world-generation",
            WorldGenerationGraphDefaults.BuildCrustGraph());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            source.ApplyEditAsync(new GraphEdit.AddAnnotation(new GraphAnnotation(
                "bad_node",
                WorldGenerationGraphAnnotationKinds.CommentBoundary,
                "Bad node",
                new GraphAnnotationBounds(0, 0, 200, 120),
                new[] { "ghost" }))));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            source.ApplyEditAsync(new GraphEdit.AddAnnotation(new GraphAnnotation(
                "bad_bounds",
                WorldGenerationGraphAnnotationKinds.CommentBoundary,
                "Bad bounds",
                new GraphAnnotationBounds(0, 0, 0, 120),
                new[] { "options" }))));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            source.ApplyEditAsync(new GraphEdit.AddAnnotation(new GraphAnnotation(
                "bad_kind",
                "unknown-boundary",
                "Bad kind",
                new GraphAnnotationBounds(0, 0, 200, 120),
                new[] { "options" }))));
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

    [Fact]
    public void TimelineGraphBinding_FollowsGeosphereRegimeChanges()
    {
        var timeline = new FakeTimelineController(
            SphereRegimeScheduleDefaults.GeosphereDefault,
            SphereRegimeScheduleDefaults.AtmosphereFor(SphereRegimeScheduleDefaults.PlateOnsetTick));
        var source = WorldGenerationGraphFamilySource.ForRegime(
            "world-generation",
            WorldGenerationGraphDefaults.BuildFamily(),
            WorldRegimeScheduleKinds.Sphere,
            "mobile-plate",
            SphereRegimeScheduleDefaults.PlateOnsetTick,
            WorldGenerationGraphDefaults.GeosphereSphereId);

        using var binding = WorldGenerationTimelineGraphBinding.BindGeosphere(timeline, source);

        Assert.Equal(WorldGenerationGraphDefaults.GeosphereMagmaOceanGraphId, source.ActiveGraphId);
        Assert.Equal(0, source.ActiveTick);

        timeline.PushTick(SphereRegimeScheduleDefaults.MagmaOceanEndTick);

        Assert.Equal(WorldGenerationGraphDefaults.GeosphereStagnantLidGraphId, source.ActiveGraphId);
        Assert.Equal(SphereRegimeScheduleDefaults.MagmaOceanEndTick, source.ActiveTick);

        timeline.PushTick(SphereRegimeScheduleDefaults.PlateOnsetTick);

        Assert.Equal(WorldGenerationGraphDefaults.GeosphereGraphId, source.ActiveGraphId);
        Assert.Equal(SphereRegimeScheduleDefaults.PlateOnsetTick, source.ActiveTick);
    }

    [Fact]
    public void TimelineGraphBinding_PreservesOpenedSubgraphWithinSameRegime_AndDisposes()
    {
        var onset = SphereRegimeScheduleDefaults.PlateOnsetTick;
        var timeline = new FakeTimelineController(
            SphereRegimeScheduleDefaults.GeosphereDefault,
            SphereRegimeScheduleDefaults.AtmosphereFor(onset),
            onset);
        var source = WorldGenerationGraphFamilySource.ForRegime(
            "world-generation",
            WorldGenerationGraphDefaults.BuildFamily(),
            WorldRegimeScheduleKinds.Sphere,
            "mobile-plate",
            onset,
            WorldGenerationGraphDefaults.GeosphereSphereId);
        using var binding = WorldGenerationTimelineGraphBinding.BindGeosphere(timeline, source);
        var navigator = Assert.IsAssignableFrom<IGraphSubgraphSource>(source);
        navigator.SelectGraph(WorldGenerationGraphDefaults.MobilePlateLayerGraphId);

        timeline.PushTick(onset + 1);

        Assert.Equal(WorldGenerationGraphDefaults.MobilePlateLayerGraphId, source.ActiveGraphId);
        Assert.Equal(onset + 1, source.ActiveTick);

        binding.Dispose();
        timeline.PushTick(0);

        Assert.Equal(WorldGenerationGraphDefaults.MobilePlateLayerGraphId, source.ActiveGraphId);
        Assert.Equal(onset + 1, source.ActiveTick);
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

    [Fact]
    public async Task WorldGenerationGraphRunner_WhenProductAddressIsNotString_SkipsProduct()
    {
        var node = new GraphNode("node1", "test.product", new JsonObject());
        var graph = new GraphDocument(new[] { node }, Array.Empty<GraphWire>(), "node1");

        var provider = new FakeProductProvider(new JsonObject { ["foo"] = "bar" }); // an object instead of string
        var runner = new WorldGenerationGraphRunner(new[] { provider });
        var run = await runner.RunAsync(graph);

        Assert.Empty(run.Products);
    }

    private sealed class FakeProductProvider : INodeFunctionProvider
    {
        private readonly JsonNode? _productAddressValue;

        public FakeProductProvider(JsonNode? productAddressValue)
        {
            _productAddressValue = productAddressValue;
        }

        public bool Supports(string functionId) => functionId == "test.product";

        public Task<JsonObject> InvokeAsync(string functionId, JsonObject payload, CancellationToken cancellationToken = default)
        {
            var res = new JsonObject
            {
                ["function"] = "test.product"
            };
            if (_productAddressValue is not null)
            {
                res["productAddress"] = _productAddressValue.DeepClone();
            }
            return Task.FromResult(res);
        }
    }

    private sealed class FakeTimelineController : ITimelineController
    {
        public FakeTimelineController(
            SphereRegimeSchedule geosphereSchedule,
            SphereRegimeSchedule atmosphereSchedule,
            long tick = 0,
            long maxTick = 120_000_000)
        {
            GeosphereSchedule = geosphereSchedule;
            AtmosphereSchedule = atmosphereSchedule;
            Tick = tick;
            MaxTick = maxTick;
        }

        public long Tick { get; private set; }
        public long MaxTick { get; }
        public bool IsPlaying => false;
        public SphereRegimeSchedule GeosphereSchedule { get; }
        public SphereRegimeSchedule AtmosphereSchedule { get; }
        public event Action<long>? TickChanged;

        public void Play() { }
        public void Pause() { }
        public void SeekTo(long tick) => PushTick(tick);

        public void PushTick(long tick)
        {
            Tick = tick;
            TickChanged?.Invoke(tick);
        }

        public void RegisterPlayback(Action onPlay, Action onPause, Action<long> onSeek, Func<bool> checkPlaying) { }
        public void UnregisterPlayback() { }
    }
}
