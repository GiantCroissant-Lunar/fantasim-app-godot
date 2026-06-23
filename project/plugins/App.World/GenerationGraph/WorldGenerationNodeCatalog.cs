using System;
using System.Collections.Generic;
using System.Linq;

namespace FantaSim.App.World.GenerationGraph;

/// <summary>
/// World-generation node schemas backed by handlers in <see cref="WorldFunctionProvider"/>.
/// This is intentionally smaller than the reference catalog until each node has a current-app handler.
/// </summary>
public static class WorldGenerationNodeCatalog
{
    private static readonly IReadOnlyList<WorldGenerationNodeSchema> Schemas = new[]
    {
        new WorldGenerationNodeSchema(
            TypeId: WorldFunctionProvider.WorldOptions,
            Label: "World Options",
            Category: "source",
            IsSideEffect: false,
            IsExpensive: false,
            Inputs: Array.Empty<WorldGenerationGraphPort>(),
            Outputs: new[] { new WorldGenerationGraphPort("options", "Options", "world/options", Required: false) },
            Summary: "Seed, frequency, canonical tick, and crust controls for a world-generation run.",
            Parameters: new[]
            {
                new WorldGenerationGraphParameter("seed", "Seed", "7", "int"),
                new WorldGenerationGraphParameter("frequency", "Frequency", "3", "int"),
                new WorldGenerationGraphParameter("canonicalTick", "Canonical Tick", "8000000", "long"),
                new WorldGenerationGraphParameter("spinRateRadiansPerMegaAnnum", "Spin Rate", "0.02", "float"),
            }),

        new WorldGenerationNodeSchema(
            TypeId: WorldFunctionProvider.LayerScope,
            Label: "Layer Scope",
            Category: "source",
            IsSideEffect: false,
            IsExpensive: false,
            Inputs: Array.Empty<WorldGenerationGraphPort>(),
            Outputs: new[] { new WorldGenerationGraphPort("layer", "Layer", "world/layer_scope", Required: false) },
            Summary: "Declares the sphere, regime, and layer represented by a regime or layer subgraph.",
            Parameters: new[]
            {
                new WorldGenerationGraphParameter("sphereId", "Sphere", WorldGenerationGraphDefaults.GeosphereSphereId, "string"),
                new WorldGenerationGraphParameter("regimeId", "Regime", "mobile-plate", "string"),
                new WorldGenerationGraphParameter("layerId", "Layer", "geosphere.crust", "string"),
                new WorldGenerationGraphParameter("role", "Role", "layer", "string"),
            }),

        new WorldGenerationNodeSchema(
            TypeId: WorldFunctionProvider.CrustGenerate,
            Label: "Crust Generation",
            Category: "geosphere",
            IsSideEffect: false,
            IsExpensive: true,
            Inputs: new[] { new WorldGenerationGraphPort("options", "Options", "world/options", Required: true) },
            Outputs: new[] { new WorldGenerationGraphPort("result", "Result", "world/crust_summary", Required: false) },
            Summary: "Runs the current crust-generation pipeline and returns a JSON world summary."),
    };

    public static IReadOnlyList<WorldGenerationNodeSchema> All => Schemas;

    public static WorldGenerationNodeSchema? Find(string typeId)
        => Schemas.FirstOrDefault(schema => string.Equals(schema.TypeId, typeId, StringComparison.Ordinal));
}
