using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using FantaSim.App.World.GenerationGraph;
using Xunit;

namespace FantaSim.App.World.Tests;

public sealed class WorldGenerationGraphCompilerTests
{
    [Fact]
    public void Compile_parses_object_kind_hint_parameter_into_json_object()
    {
        var node = new WorldGenerationGraphNode(
            NodeId: "options",
            TypeId: WorldFunctionProvider.WorldOptions,
            Label: "World Options",
            Category: "options",
            IsSideEffect: false,
            IsExpensive: false,
            Inputs: Array.Empty<WorldGenerationGraphPort>(),
            Outputs: new[] { new WorldGenerationGraphPort("options", "Options", "world/options", Required: false) },
            Parameters: new[]
            {
                new WorldGenerationGraphParameter(
                    "continentalPatches",
                    "Continental Patches",
                    "{ \"seed\": 3, \"count\": 2 }",
                    "object"),
            });
        var graph = new WorldGenerationGraphView(
            "compiler-object-param",
            "Compiler Object Param",
            "",
            new[] { node },
            Array.Empty<WorldGenerationGraphWire>());

        var compiled = WorldGenerationGraphCompiler.Compile(graph);

        var payload = Assert.Single(compiled.Document.Nodes).Params;
        var patches = Assert.IsType<JsonObject>(payload["continentalPatches"]);
        Assert.Equal(3, patches["seed"]!.GetValue<int>());
        Assert.Equal(2, patches["count"]!.GetValue<int>());
    }

    [Fact]
    public void DefaultFamily_world_options_continental_patches_compiles_to_json_object()
    {
        var family = WorldGenerationGraphDefaults.BuildFamily();
        var graphsWithOptions = family.Graphs
            .Where(graph => graph.Nodes.Any(node => node.TypeId == WorldFunctionProvider.WorldOptions))
            .ToList();
        Assert.NotEmpty(graphsWithOptions);

        foreach (var graph in graphsWithOptions)
        {
            var compiled = WorldGenerationGraphCompiler.Compile(graph);
            var optionsNodes = compiled.Document.Nodes
                .Where(node => node.FunctionId == WorldFunctionProvider.WorldOptions)
                .ToList();
            Assert.NotEmpty(optionsNodes);

            foreach (var optionsNode in optionsNodes)
                Assert.IsType<JsonObject>(optionsNode.Params["continentalPatches"]);
        }
    }
}
