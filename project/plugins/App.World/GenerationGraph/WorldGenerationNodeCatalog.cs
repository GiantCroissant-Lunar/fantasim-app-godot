using System;
using System.Collections.Generic;
using System.Linq;
using FantaSim.App.NodeGraph;

namespace FantaSim.App.World.GenerationGraph;

/// <summary>
/// World-generation node schemas backed by local handlers or projected external-tool manifests.
/// This is intentionally smaller than the reference catalog until each node has a current-app handler
/// or an explicit external-tool bridge.
/// </summary>
public static class WorldGenerationNodeCatalog
{
    private static readonly FunctionProviderMetadata CSharpMetadata = new(FunctionProviderKinds.CSharp);

    private static readonly FunctionExecutionTraits NativeExecutionTraits = new(
        RequiresExternalProcess: false,
        RequiresNetwork: false,
        IsDeterministic: true);

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
            },
            ProviderMetadata: CSharpMetadata,
            ExecutionTraits: NativeExecutionTraits),

        new WorldGenerationNodeSchema(
            TypeId: WorldFunctionProvider.BodyFormation,
            Label: "Body Formation",
            Category: "formation",
            IsSideEffect: false,
            IsExpensive: false,
            Inputs: Array.Empty<WorldGenerationGraphPort>(),
            Outputs: new[]
            {
                new WorldGenerationGraphPort("bodySet", "Body Set", "world/body_set", Required: false),
                new WorldGenerationGraphPort("sphereHandoff", "Sphere Handoff", "world/sphere_handoff", Required: false),
            },
            Summary: "Packages pre-sphere body-formation products and the handoff inputs consumed by later sphere regimes.",
            Parameters: new[]
            {
                new WorldGenerationGraphParameter("seed", "Seed", "7", "int"),
                new WorldGenerationGraphParameter("canonicalTick", "Canonical Tick", "0", "long"),
                new WorldGenerationGraphParameter("regimeId", "Regime", "planetesimal-swarm", "string"),
                new WorldGenerationGraphParameter("totalMassKg", "Total Mass Kg", "5.972e24", "double"),
                new WorldGenerationGraphParameter("specificAccretionHeatJPerKg", "Accretion Heat J/Kg", "1.0e7", "double"),
                new WorldGenerationGraphParameter("volatileMassFraction", "Volatile Mass Fraction", "0.02", "double"),
            },
            ProviderMetadata: CSharpMetadata,
            ExecutionTraits: NativeExecutionTraits),

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
            },
            ProviderMetadata: CSharpMetadata,
            ExecutionTraits: NativeExecutionTraits),

        new WorldGenerationNodeSchema(
            TypeId: WorldFunctionProvider.LayerSource,
            Label: "Layer Source",
            Category: "source",
            IsSideEffect: false,
            IsExpensive: false,
            Inputs: new[] { new WorldGenerationGraphPort("layer", "Layer", "world/layer_scope", Required: false) },
            Outputs: new[] { new WorldGenerationGraphPort("source", "Source", "world/layer_source", Required: false) },
            Summary: "Declares one concrete source candidate for a world layer: PCG, world-native import, iii-normalized data, or observed dataset.",
            Parameters: new[]
            {
                new WorldGenerationGraphParameter("sphereId", "Sphere", WorldGenerationGraphDefaults.GeosphereSphereId, "string"),
                new WorldGenerationGraphParameter("regimeId", "Regime", "mobile-plate", "string"),
                new WorldGenerationGraphParameter("layerId", "Layer", "geosphere.crust", "string"),
                new WorldGenerationGraphParameter("sourceId", "Source", "geosphere.crust.pcg", "string"),
                new WorldGenerationGraphParameter("sourceKind", "Source Kind", WorldLayerSourceKinds.Procedural, "string"),
                new WorldGenerationGraphParameter("availability", "Availability", WorldLayerSourceAvailability.Available, "string"),
                new WorldGenerationGraphParameter("bodyId", "Body", "fantasim", "string"),
                new WorldGenerationGraphParameter("datasetId", "Dataset", "", "string"),
                new WorldGenerationGraphParameter("providerId", "Provider", "App.World", "string"),
                new WorldGenerationGraphParameter("importFormat", "Import Format", "", "string"),
                new WorldGenerationGraphParameter("normalizedProductKind", "Product Kind", "world/layer", "string"),
                new WorldGenerationGraphParameter("rendererContract", "Renderer", "world.globe.layer.v1", "string"),
            },
            ProviderMetadata: CSharpMetadata,
            ExecutionTraits: NativeExecutionTraits),

        new WorldGenerationNodeSchema(
            TypeId: WorldFunctionProvider.LayerNormalize,
            Label: "Normalize Layer",
            Category: "geosphere",
            IsSideEffect: false,
            IsExpensive: false,
            Inputs: new[]
            {
                new WorldGenerationGraphPort("layer", "Layer", "world/layer_scope", Required: true),
                new WorldGenerationGraphPort("primarySource", "Primary Source", "world/layer_source", Required: true),
                new WorldGenerationGraphPort("secondarySource", "Secondary Source", "world/layer_source", Required: false),
            },
            Outputs: new[] { new WorldGenerationGraphPort("normalizedLayer", "Normalized Layer", "world/normalized_layer", Required: false) },
            Summary: "Normalizes the selected source candidate into the product contract consumed by the shared planet layer renderer.",
            Parameters: new[]
            {
                new WorldGenerationGraphParameter("sphereId", "Sphere", WorldGenerationGraphDefaults.GeosphereSphereId, "string"),
                new WorldGenerationGraphParameter("regimeId", "Regime", "mobile-plate", "string"),
                new WorldGenerationGraphParameter("layerId", "Layer", "geosphere.crust", "string"),
                new WorldGenerationGraphParameter("selectedSourceId", "Selected Source", "geosphere.crust.pcg", "string"),
                new WorldGenerationGraphParameter("selectedSourceKind", "Selected Kind", WorldLayerSourceKinds.Procedural, "string"),
                new WorldGenerationGraphParameter("normalizedProductKind", "Product Kind", "world/layer", "string"),
                new WorldGenerationGraphParameter("rendererContract", "Renderer", "world.globe.layer.v1", "string"),
            },
            ProviderMetadata: CSharpMetadata,
            ExecutionTraits: NativeExecutionTraits),

        new WorldGenerationNodeSchema(
            TypeId: WorldFunctionProvider.CrustGenerate,
            Label: "Crust Generation",
            Category: "geosphere",
            IsSideEffect: false,
            IsExpensive: true,
            Inputs: new[] { new WorldGenerationGraphPort("options", "Options", "world/options", Required: true) },
            Outputs: new[] { new WorldGenerationGraphPort("result", "Result", "world/crust_summary", Required: false) },
            Summary: "Runs the current crust-generation pipeline and returns a JSON world summary.",
            ProviderMetadata: CSharpMetadata,
            ExecutionTraits: NativeExecutionTraits),
    }
    .Concat(ExternalToolNodeSchemaProjector.Project(VplanetExternalToolManifest.Build()))
    .Concat(ExternalToolNodeSchemaProjector.Project(ComfyExternalToolManifest.Build()))
    .Concat(ExternalToolNodeSchemaProjector.Project(BlenderExternalToolManifest.Build()))
    .ToList();

    public static IReadOnlyList<WorldGenerationNodeSchema> All => Schemas;

    public static WorldGenerationNodeSchema? Find(string typeId)
        => Schemas.FirstOrDefault(schema => string.Equals(schema.TypeId, typeId, StringComparison.Ordinal));
}
