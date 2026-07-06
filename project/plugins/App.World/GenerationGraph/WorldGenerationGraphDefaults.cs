using System;
using System.Collections.Generic;
using System.Linq;

namespace FantaSim.App.World.GenerationGraph;

/// <summary>Default current-app world-generation graph documents.</summary>
public static class WorldGenerationGraphDefaults
{
    public const string DocumentId = "world-generation.family";
    public const string BaseGraphId = "world.base";
    public const string FormationGraphId = "formation.planetesimal-swarm";
    public const string GeosphereGraphId = "geosphere.mobile-plate";
    public const string GeosphereMagmaOceanGraphId = "geosphere.magma-ocean";
    public const string GeosphereStagnantLidGraphId = "geosphere.stagnant-lid";
    public const string MobilePlateLayerGraphId = "geosphere.mobile-plate.layers";
    public const string GeospherePlateLayerGraphId = "geosphere.plate.layer";
    public const string GeosphereCrustLayerGraphId = "geosphere.crust.layer";
    public const string VplanetEarthGraphId = "external.vplanet.earth";
    public const string GeosphereSphereId = "geosphere";
    public const string AtmosphereSphereId = "atmosphere";
    public const string DefaultLayerRendererContract = "world.globe.layer.v1";
    public const string PlateLayerRendererContract = "world.globe.plate-layer.v1";
    public const string CrustLayerRendererContract = "world.globe.crust-layer.v1";
    public const string GenericLayerProductKind = "world/layer";
    public const string PlateLayerProductKind = "world/plate-layer";
    public const string CrustLayerProductKind = "world/crust-layer";

    public static WorldGenerationGraphFamilyDocument BuildFamily(DateTimeOffset? updatedUtc = null)
    {
        var baseGraph = BuildCrustGraph(BaseGraphId, "World Creation");
        var formationGraph = BuildBodyFormationGraph();
        var geosphereGraph = BuildCrustGraph(GeosphereGraphId, "Mobile Plate Geosphere");
        var magmaOceanGraph = BuildLayerScopeGraph(
            GeosphereMagmaOceanGraphId,
            "Magma Ocean Layer",
            "magma-ocean",
            "geosphere.magma-ocean",
            "regime-layer");
        var stagnantLidGraph = BuildLayerScopeGraph(
            GeosphereStagnantLidGraphId,
            "Stagnant Lid Layer",
            "stagnant-lid",
            "geosphere.stagnant-lid",
            "regime-layer");
        var mobilePlateLayersGraph = BuildMobilePlateLayersGraph();
        var plateLayerGraph = BuildLayerScopeGraph(
            GeospherePlateLayerGraphId,
            "Plate Layer",
            "mobile-plate",
            "geosphere.plate",
            "field-layer");
        var crustLayerGraph = BuildLayerScopeGraph(
            GeosphereCrustLayerGraphId,
            "Crust Layer",
            "mobile-plate",
            "geosphere.crust",
            "field-layer");
        var vplanetEarthGraph = BuildVplanetEarthGraph();

        return new WorldGenerationGraphFamilyDocument(
            DocumentId: DocumentId,
            SchemaVersion: 1,
            Revision: 1,
            BaseGraph: baseGraph,
            Graphs: new[]
            {
                formationGraph,
                geosphereGraph,
                magmaOceanGraph,
                stagnantLidGraph,
                mobilePlateLayersGraph,
                plateLayerGraph,
                crustLayerGraph,
                vplanetEarthGraph,
            },
            RegimeGraphBindings: new[]
            {
                new WorldRegimeGraphBinding(
                    WorldRegimeScheduleKinds.BodyFormation,
                    "planetesimal-swarm",
                    FormationGraphId),
                new WorldRegimeGraphBinding(
                    WorldRegimeScheduleKinds.Sphere,
                    "magma-ocean",
                    GeosphereMagmaOceanGraphId,
                    SphereId: GeosphereSphereId),
                new WorldRegimeGraphBinding(
                    WorldRegimeScheduleKinds.Sphere,
                    "stagnant-lid",
                    GeosphereStagnantLidGraphId,
                    SphereId: GeosphereSphereId),
                new WorldRegimeGraphBinding(
                    WorldRegimeScheduleKinds.Sphere,
                    "mobile-plate",
                    GeosphereGraphId,
                    SphereId: GeosphereSphereId),
            },
            GraphOverrides: Array.Empty<WorldGenerationGraphScopedOverride>(),
            LegacyOverrides: Array.Empty<WorldGenerationGraphOverride>(),
            RunHistory: Array.Empty<WorldGenerationRunHistoryEntry>(),
            UpdatedUtc: updatedUtc ?? DateTimeOffset.UnixEpoch,
            SubgraphBindings: new[]
            {
                new WorldGenerationSubgraphBinding(BaseGraphId, "crust", GeosphereGraphId),
                new WorldGenerationSubgraphBinding(GeosphereGraphId, "crust", MobilePlateLayerGraphId),
                new WorldGenerationSubgraphBinding(MobilePlateLayerGraphId, "plate_layer", GeospherePlateLayerGraphId),
                new WorldGenerationSubgraphBinding(MobilePlateLayerGraphId, "crust_layer", GeosphereCrustLayerGraphId),
            },
            LayerGraphBindings: new[]
            {
                new WorldLayerGraphBinding(GeosphereSphereId, "geosphere.magma-ocean", GeosphereMagmaOceanGraphId, RegimeId: "magma-ocean"),
                new WorldLayerGraphBinding(GeosphereSphereId, "geosphere.stagnant-lid", GeosphereStagnantLidGraphId, RegimeId: "stagnant-lid"),
                new WorldLayerGraphBinding(GeosphereSphereId, "geosphere.plate", GeospherePlateLayerGraphId, RegimeId: "mobile-plate"),
                new WorldLayerGraphBinding(GeosphereSphereId, "geosphere.crust", GeosphereCrustLayerGraphId, RegimeId: "mobile-plate"),
            },
            LayerSourceBindings: BuildLayerSourceBindings());
    }

    public static WorldGenerationGraphView BuildBodyFormationGraph()
    {
        var bodyFormation = NodeFromSchema("body_formation", WorldFunctionProvider.BodyFormation);

        return new WorldGenerationGraphView(
            GraphId: FormationGraphId,
            Label: "Planetesimal Swarm",
            Description: "Pre-sphere body-formation graph: packages body-set and sphere-handoff products before geosphere regimes exist.",
            Nodes: new[] { bodyFormation },
            Wires: Array.Empty<WorldGenerationGraphWire>(),
            Annotations: new[]
            {
                new WorldGenerationGraphAnnotation(
                    AnnotationId: "comment_body_formation",
                    Kind: WorldGenerationGraphAnnotationKinds.CommentBoundary,
                    Label: "Body formation",
                    Bounds: new WorldGenerationGraphBounds(-80, -80, 560, 240),
                    NodeIds: new[] { "body_formation" },
                    Text: "This graph represents the parent formation lifecycle before a sphere/geosphere exists. It emits a body set and sphere handoff instead of running crust generation.",
                    Color: "#d99a4e"),
            },
            OutputNodeIds: new[] { "body_formation" });
    }

    public static WorldGenerationGraphView BuildCrustGraph(
        string graphId = BaseGraphId,
        string label = "World Creation")
    {
        var options = NodeFromSchema("options", WorldFunctionProvider.WorldOptions);
        var crust = NodeFromSchema("crust", WorldFunctionProvider.CrustGenerate);

        return new WorldGenerationGraphView(
            GraphId: graphId,
            Label: label,
            Description: "Current executable world-generation graph: authored options drive the crust pipeline.",
            Nodes: new[] { options, crust },
            Wires: new[] { new WorldGenerationGraphWire("options", "options", "crust", "options", "world/options") },
            Annotations: new[]
            {
                new WorldGenerationGraphAnnotation(
                    AnnotationId: "comment_world_creation",
                    Kind: WorldGenerationGraphAnnotationKinds.CommentBoundary,
                    Label: "World creation",
                    Bounds: new WorldGenerationGraphBounds(-80, -80, 560, 260),
                    NodeIds: new[] { "options", "crust" },
                    Text: "Executable first slice: options feed crust generation; later slices expand this into formation, geosphere, layer, and timeline subgraphs.",
                    Color: "#6ea8fe"),
            },
            OutputNodeIds: new[] { "crust" });
    }

    public static WorldGenerationGraphView BuildVplanetEarthGraph()
    {
        var status = NodeFromSchema("vplanet_status", "vplanet.status");
        var input = NodeFromSchema("vplanet_input", "vplanet.input.build");
        var run = NodeFromSchema("vplanet_run", "vplanet.run");
        var parse = NodeFromSchema("vplanet_parse", "vplanet.output.parse");

        return new WorldGenerationGraphView(
            GraphId: VplanetEarthGraphId,
            Label: "VPLanet Earth Template",
            Description: "External science template: builds an Earth/Sun VPLanet input bundle, runs VPLanet through iii, and parses outputs for later world-side conversion.",
            Nodes: new[] { status, input, run, parse },
            Wires: new[]
            {
                new WorldGenerationGraphWire("vplanet_input", "inputBundle", "vplanet_run", "inputBundle", "vplanet/input-bundle"),
                new WorldGenerationGraphWire("vplanet_run", "runResult", "vplanet_parse", "runResult", "vplanet/run-result"),
            },
            Annotations: new[]
            {
                new WorldGenerationGraphAnnotation(
                    AnnotationId: "comment_vplanet_earth",
                    Kind: WorldGenerationGraphAnnotationKinds.CommentBoundary,
                    Label: "VPLanet Earth template",
                    Bounds: new WorldGenerationGraphBounds(-80, -80, 780, 300),
                    NodeIds: new[] { "vplanet_status", "vplanet_input", "vplanet_run", "vplanet_parse" },
                    Text: "Availability check plus Earth/Sun VPLanet workflow. The final parsed table remains external data until a world-side converter maps it into topology, fields, or truth.",
                    Color: "#b689d6"),
            },
            OutputNodeIds: new[] { "vplanet_parse" });
    }

    public static WorldGenerationGraphView BuildMobilePlateLayersGraph()
    {
        var plate = LayerScopeNode("plate_layer", "Plate Layer", "mobile-plate", "geosphere.plate", "field-layer");
        var crust = LayerScopeNode("crust_layer", "Crust Layer", "mobile-plate", "geosphere.crust", "field-layer");

        return new WorldGenerationGraphView(
            GraphId: MobilePlateLayerGraphId,
            Label: "Mobile Plate Layers",
            Description: "Layer index for the mobile-plate regime. Each layer can open its own subgraph.",
            Nodes: new[] { plate, crust },
            Wires: Array.Empty<WorldGenerationGraphWire>(),
            Annotations: new[]
            {
                new WorldGenerationGraphAnnotation(
                    AnnotationId: "group_mobile_plate_layers",
                    Kind: WorldGenerationGraphAnnotationKinds.GroupBoundary,
                    Label: "Mobile plate layers",
                    Bounds: new WorldGenerationGraphBounds(-80, -80, 620, 260),
                    NodeIds: new[] { "plate_layer", "crust_layer" },
                    Text: "The mobile-plate regime activates plate topology and crust evolution layers.",
                    Color: "#8bd17c"),
            },
            OutputNodeIds: new[] { "plate_layer", "crust_layer" });
    }

    public static WorldGenerationGraphView BuildLayerScopeGraph(
        string graphId,
        string label,
        string regimeId,
        string layerId,
        string role)
    {
        if (string.Equals(layerId, "geosphere.crust", StringComparison.Ordinal))
            return BuildCrustLayerGraph(graphId, label, regimeId, layerId, role);
        if (string.Equals(layerId, "geosphere.plate", StringComparison.Ordinal))
            return BuildPlateLayerGraph(graphId, label, regimeId, layerId, role);
        if (string.Equals(layerId, "geosphere.magma-ocean", StringComparison.Ordinal))
            return BuildMagmaOceanLayerGraph(graphId, label, regimeId, layerId, role);
        if (string.Equals(layerId, "geosphere.stagnant-lid", StringComparison.Ordinal))
            return BuildStagnantLidLayerGraph(graphId, label, regimeId, layerId, role);

        return BuildExecutableLayerScopeGraph(graphId, label, regimeId, layerId, role);
    }

    private static WorldGenerationGraphView BuildExecutableLayerScopeGraph(
        string graphId,
        string label,
        string regimeId,
        string layerId,
        string role)
    {
        var scope = LayerScopeNode("layer_scope", label, regimeId, layerId, role);
        var source = LayerSourceNode(
            "source_pcg",
            "PCG Layer Source",
            regimeId,
            layerId,
            $"{layerId}.pcg",
            WorldLayerSourceKinds.Procedural,
            WorldLayerSourceAvailability.Available,
            GenericLayerProductKind,
            DefaultLayerRendererContract);
        var normalize = NormalizeLayerNode(
            "normalize_layer",
            "Normalize Layer",
            regimeId,
            layerId,
            $"{layerId}.pcg",
            WorldLayerSourceKinds.Procedural,
            GenericLayerProductKind,
            DefaultLayerRendererContract);

        return new WorldGenerationGraphView(
            GraphId: graphId,
            Label: label,
            Description: $"Executable layer graph for {layerId} in the {regimeId} regime: selected source normalizes into the shared renderer contract.",
            Nodes: new[] { scope, source, normalize },
            Wires: new[]
            {
                new WorldGenerationGraphWire("layer_scope", "layer", "source_pcg", "layer", "world/layer_scope"),
                new WorldGenerationGraphWire("layer_scope", "layer", "normalize_layer", "layer", "world/layer_scope"),
                new WorldGenerationGraphWire("source_pcg", "source", "normalize_layer", "primarySource", "world/layer_source"),
            },
            Annotations: new[]
            {
                new WorldGenerationGraphAnnotation(
                    AnnotationId: $"comment_{SanitizeId(graphId)}",
                    Kind: WorldGenerationGraphAnnotationKinds.CommentBoundary,
                    Label: label,
                    Bounds: new WorldGenerationGraphBounds(-80, -80, 760, 260),
                    NodeIds: new[] { "layer_scope", "source_pcg", "normalize_layer" },
                    Text: "Every layer graph declares its scope, selects a source candidate, then normalizes into the renderer-facing layer contract.",
                    Color: "#f6c85f"),
            },
            OutputNodeIds: new[] { "normalize_layer" });
    }

    private static WorldGenerationGraphView BuildPlateLayerGraph(
        string graphId,
        string label,
        string regimeId,
        string layerId,
        string role)
    {
        var scope = LayerScopeNode("layer_scope", label, regimeId, layerId, role);
        var pcgSource = LayerSourceNode(
            "source_pcg",
            "PCG Plate Source",
            regimeId,
            layerId,
            "geosphere.plate.pcg",
            WorldLayerSourceKinds.Procedural,
            WorldLayerSourceAvailability.Available,
            PlateLayerProductKind,
            PlateLayerRendererContract,
            providerId: "App.World");
        var rotSource = LayerSourceNode(
            "source_gplates_rot",
            "GPlates ROT Source",
            regimeId,
            layerId,
            "geosphere.plate.gplates-rot",
            WorldLayerSourceKinds.WorldNativeImport,
            WorldLayerSourceAvailability.RequiresUserContent,
            PlateLayerProductKind,
            PlateLayerRendererContract,
            bodyId: "user-selected",
            datasetId: "user-selected",
            providerId: "Geosphere.Plate.Rotation.Stream",
            importFormat: "gplates.rot");
        var normalize = NormalizeLayerNode(
            "normalize_plate",
            "Normalize Plate Layer",
            regimeId,
            layerId,
            "geosphere.plate.pcg",
            WorldLayerSourceKinds.Procedural,
            PlateLayerProductKind,
            PlateLayerRendererContract);

        return new WorldGenerationGraphView(
            GraphId: graphId,
            Label: label,
            Description: "Plate-layer graph: PCG and world-native import sources normalize into one shared plate renderer contract.",
            Nodes: new[] { scope, pcgSource, rotSource, normalize },
            Wires: new[]
            {
                new WorldGenerationGraphWire("layer_scope", "layer", "source_pcg", "layer", "world/layer_scope"),
                new WorldGenerationGraphWire("layer_scope", "layer", "source_gplates_rot", "layer", "world/layer_scope"),
                new WorldGenerationGraphWire("layer_scope", "layer", "normalize_plate", "layer", "world/layer_scope"),
                new WorldGenerationGraphWire("source_pcg", "source", "normalize_plate", "primarySource", "world/layer_source"),
                new WorldGenerationGraphWire("source_gplates_rot", "source", "normalize_plate", "secondarySource", "world/layer_source"),
            },
            Annotations: new[]
            {
                new WorldGenerationGraphAnnotation(
                    AnnotationId: "group_plate_layer_scope",
                    Kind: WorldGenerationGraphAnnotationKinds.CommentBoundary,
                    Label: "Plate layer scope",
                    Bounds: new WorldGenerationGraphBounds(-80, -80, 360, 220),
                    NodeIds: new[] { "layer_scope" },
                    Text: "This node declares the active mobile-plate layer selected from the timeline track.",
                    Color: "#f6c85f"),
                new WorldGenerationGraphAnnotation(
                    AnnotationId: "group_plate_sources",
                    Kind: WorldGenerationGraphAnnotationKinds.GroupBoundary,
                    Label: "Plate source candidates",
                    Bounds: new WorldGenerationGraphBounds(320, -80, 900, 300),
                    NodeIds: new[] { "source_pcg", "source_gplates_rot", "normalize_plate" },
                    Text: "PCG is the selected runtime source today. GPlates .rot is a real world-native import capability that requires user-provided rotation content; both converge on the same plate renderer contract.",
                    Color: "#8bd17c"),
            },
            OutputNodeIds: new[] { "normalize_plate" });
    }

    private static WorldGenerationGraphView BuildCrustLayerGraph(
        string graphId,
        string label,
        string regimeId,
        string layerId,
        string role)
    {
        var scope = LayerScopeNode("layer_scope", label, regimeId, layerId, role);
        var source = LayerSourceNode(
            "source_pcg",
            "PCG Crust Source",
            regimeId,
            layerId,
            "geosphere.crust.pcg",
            WorldLayerSourceKinds.Procedural,
            WorldLayerSourceAvailability.Available,
            CrustLayerProductKind,
            CrustLayerRendererContract,
            providerId: "App.World.CrustPipeline");
        var normalize = NormalizeLayerNode(
            "normalize_crust",
            "Normalize Crust Layer",
            regimeId,
            layerId,
            "geosphere.crust.pcg",
            WorldLayerSourceKinds.Procedural,
            CrustLayerProductKind,
            CrustLayerRendererContract);
        var options = NodeFromSchema("options", WorldFunctionProvider.WorldOptions);
        var crust = NodeFromSchema("crust", WorldFunctionProvider.CrustGenerate);
        var comfy = NodeFromSchema("comfy_generate", "comfy.generate", new Dictionary<string, string>
        {
            ["prompt"] = "geosphere crust layer surface asset",
            ["jobId"] = "geosphere-crust-layer",
        });
        var blender = NodeFromSchema("blender_refine", "blender.refine", new Dictionary<string, string>
        {
            ["jobId"] = "geosphere-crust-layer",
        });
        var gltf = NodeFromSchema("asset_gltf", "asset.to_gltf", new Dictionary<string, string>
        {
            ["jobId"] = "geosphere-crust-layer",
        });

        return new WorldGenerationGraphView(
            GraphId: graphId,
            Label: label,
            Description: "Crust-layer graph: native PCG source normalizes into the shared renderer contract, with iii asset tools kept as authoring-side visual preparation.",
            Nodes: new[] { scope, source, normalize, options, crust, comfy, blender, gltf },
            Wires: new[]
            {
                new WorldGenerationGraphWire("layer_scope", "layer", "source_pcg", "layer", "world/layer_scope"),
                new WorldGenerationGraphWire("layer_scope", "layer", "normalize_crust", "layer", "world/layer_scope"),
                new WorldGenerationGraphWire("source_pcg", "source", "normalize_crust", "primarySource", "world/layer_source"),
                new WorldGenerationGraphWire("options", "options", "crust", "options", "world/options"),
                new WorldGenerationGraphWire("comfy_generate", "mesh", "blender_refine", "source", "comfy/mesh"),
                new WorldGenerationGraphWire("blender_refine", "usdPath", "asset_gltf", "source", "blender/usd"),
            },
            Annotations: new[]
            {
                new WorldGenerationGraphAnnotation(
                    AnnotationId: "group_crust_generation",
                    Kind: WorldGenerationGraphAnnotationKinds.GroupBoundary,
                    Label: "fantasim-world crust generation",
                    Bounds: new WorldGenerationGraphBounds(-80, -80, 780, 340),
                    NodeIds: new[] { "layer_scope", "source_pcg", "normalize_crust", "options", "crust" },
                    Text: "Native C# nodes bind the selected mobile-plate crust layer to a PCG source, run the current crust summary pipeline, and expose the normalized renderer contract.",
                    Color: "#6ea8fe"),
                new WorldGenerationGraphAnnotation(
                    AnnotationId: "group_crust_asset_tools",
                    Kind: WorldGenerationGraphAnnotationKinds.GroupBoundary,
                    Label: "iii visual asset chain",
                    Bounds: new WorldGenerationGraphBounds(760, -80, 860, 300),
                    NodeIds: new[] { "comfy_generate", "blender_refine", "asset_gltf" },
                    Text: "ComfyUI and Blender nodes are real iii external-tool nodes surfaced in the layer graph for authoring and inspection.",
                    Color: "#8bd17c"),
            },
            OutputNodeIds: new[] { "normalize_crust" });
    }

    private static WorldGenerationGraphView BuildMagmaOceanLayerGraph(
        string graphId,
        string label,
        string regimeId,
        string layerId,
        string role)
        => BuildRegimeLayerGraph(
            graphId,
            label,
            regimeId,
            layerId,
            role,
            "Magma Ocean PCG Source",
            "Normalize Magma Ocean Layer",
            "group_magma_ocean_generation",
            "Magma-ocean regime layer: the PCG source normalizes into the shared layer renderer contract and the generate node delegates to the composition GeosphereMagmaOceanLayer producer over a pre-plate lid globe.",
            WorldFunctionProvider.MagmaOceanGenerate,
            "magma_ocean",
            "group_magma_ocean_scope");

    private static WorldGenerationGraphView BuildStagnantLidLayerGraph(
        string graphId,
        string label,
        string regimeId,
        string layerId,
        string role)
        => BuildRegimeLayerGraph(
            graphId,
            label,
            regimeId,
            layerId,
            role,
            "Stagnant Lid PCG Source",
            "Normalize Stagnant Lid Layer",
            "group_stagnant_lid_generation",
            "Stagnant-lid regime layer: the PCG source normalizes into the shared layer renderer contract and the generate node delegates to the composition GeosphereStagnantLidLayer producer over a pre-plate lid globe.",
            WorldFunctionProvider.StagnantLidGenerate,
            "stagnant_lid",
            "group_stagnant_lid_scope");

    private static WorldGenerationGraphView BuildRegimeLayerGraph(
        string graphId,
        string label,
        string regimeId,
        string layerId,
        string role,
        string sourceLabel,
        string normalizeLabel,
        string generationGroupId,
        string generationText,
        string generateTypeId,
        string idSuffix,
        string scopeGroupId)
    {
        var scope = LayerScopeNode("layer_scope", label, regimeId, layerId, role);
        var source = LayerSourceNode(
            "source_pcg",
            sourceLabel,
            regimeId,
            layerId,
            $"{layerId}.pcg",
            WorldLayerSourceKinds.Procedural,
            WorldLayerSourceAvailability.Available,
            GenericLayerProductKind,
            DefaultLayerRendererContract);
        var normalize = NormalizeLayerNode(
            "normalize_layer",
            normalizeLabel,
            regimeId,
            layerId,
            $"{layerId}.pcg",
            WorldLayerSourceKinds.Procedural,
            GenericLayerProductKind,
            DefaultLayerRendererContract);
        var options = NodeFromSchema("options", WorldFunctionProvider.WorldOptions);
        var generate = NodeFromSchema("generate", generateTypeId);

        return new WorldGenerationGraphView(
            GraphId: graphId,
            Label: label,
            Description: generationText,
            Nodes: new[] { scope, source, normalize, options, generate },
            Wires: new[]
            {
                new WorldGenerationGraphWire("layer_scope", "layer", "source_pcg", "layer", "world/layer_scope"),
                new WorldGenerationGraphWire("layer_scope", "layer", "normalize_layer", "layer", "world/layer_scope"),
                new WorldGenerationGraphWire("source_pcg", "source", "normalize_layer", "primarySource", "world/layer_source"),
                new WorldGenerationGraphWire("options", "options", "generate", "options", "world/options"),
            },
            Annotations: new[]
            {
                new WorldGenerationGraphAnnotation(
                    AnnotationId: scopeGroupId,
                    Kind: WorldGenerationGraphAnnotationKinds.CommentBoundary,
                    Label: $"{label} scope",
                    Bounds: new WorldGenerationGraphBounds(-80, -80, 360, 220),
                    NodeIds: new[] { "layer_scope" },
                    Text: $"This node declares the active {regimeId} layer selected from the timeline track.",
                    Color: "#f6c85f"),
                new WorldGenerationGraphAnnotation(
                    AnnotationId: generationGroupId,
                    Kind: WorldGenerationGraphAnnotationKinds.GroupBoundary,
                    Label: $"{regimeId} layer generation",
                    Bounds: new WorldGenerationGraphBounds(320, -80, 780, 300),
                    NodeIds: new[] { "source_pcg", "normalize_layer", "options", "generate" },
                    Text: generationText,
                    Color: "#6ea8fe"),
            },
            OutputNodeIds: new[] { "normalize_layer", "generate" });
    }

    private static IReadOnlyList<WorldLayerSourceBinding> BuildLayerSourceBindings()
        => new[]
        {
            LayerSourceBinding(
                "geosphere.magma-ocean",
                "Magma Ocean PCG Source",
                "magma-ocean",
                GeosphereMagmaOceanGraphId,
                "geosphere.magma-ocean.pcg",
                WorldLayerSourceKinds.Procedural,
                "world/geosphere-magma-ocean-layer",
                DefaultLayerRendererContract),
            LayerSourceBinding(
                "geosphere.stagnant-lid",
                "Stagnant Lid PCG Source",
                "stagnant-lid",
                GeosphereStagnantLidGraphId,
                "geosphere.stagnant-lid.pcg",
                WorldLayerSourceKinds.Procedural,
                "world/geosphere-stagnant-lid-layer",
                DefaultLayerRendererContract),
            LayerSourceBinding(
                "geosphere.plate",
                "PCG Plate Source",
                "mobile-plate",
                GeospherePlateLayerGraphId,
                "geosphere.plate.pcg",
                WorldLayerSourceKinds.Procedural,
                PlateLayerProductKind,
                PlateLayerRendererContract,
                providerId: "App.World"),
            LayerSourceBinding(
                "geosphere.plate",
                "GPlates ROT Import",
                "mobile-plate",
                GeospherePlateLayerGraphId,
                "geosphere.plate.gplates-rot",
                WorldLayerSourceKinds.WorldNativeImport,
                PlateLayerProductKind,
                PlateLayerRendererContract,
                bodyId: "user-selected",
                datasetId: "user-selected",
                providerId: "Geosphere.Plate.Rotation.Stream",
                availability: WorldLayerSourceAvailability.RequiresUserContent,
                importFormat: "gplates.rot"),
            LayerSourceBinding(
                "geosphere.crust",
                "PCG Crust Source",
                "mobile-plate",
                GeosphereCrustLayerGraphId,
                "geosphere.crust.pcg",
                WorldLayerSourceKinds.Procedural,
                CrustLayerProductKind,
                CrustLayerRendererContract,
                providerId: "App.World.CrustPipeline"),
        };

    private static WorldLayerSourceBinding LayerSourceBinding(
        string layerId,
        string label,
        string regimeId,
        string graphId,
        string sourceId,
        string sourceKind,
        string normalizedProductKind,
        string rendererContract,
        string bodyId = "fantasim",
        string? datasetId = null,
        string providerId = "App.World",
        string availability = WorldLayerSourceAvailability.Available,
        string? importFormat = null)
        => new(
            SphereId: GeosphereSphereId,
            LayerId: layerId,
            SourceId: sourceId,
            Label: label,
            SourceKind: sourceKind,
            GraphId: graphId,
            NormalizedProductKind: normalizedProductKind,
            RendererContract: rendererContract,
            RegimeId: regimeId,
            BodyId: bodyId,
            DatasetId: datasetId,
            ProviderId: providerId,
            Availability: availability,
            ImportFormat: importFormat);

    private static WorldGenerationGraphNode LayerSourceNode(
        string nodeId,
        string label,
        string regimeId,
        string layerId,
        string sourceId,
        string sourceKind,
        string availability,
        string normalizedProductKind,
        string rendererContract,
        string bodyId = "fantasim",
        string datasetId = "",
        string providerId = "App.World",
        string importFormat = "")
        => NodeFromSchema(
            nodeId,
            WorldFunctionProvider.LayerSource,
            new Dictionary<string, string>
            {
                ["sphereId"] = GeosphereSphereId,
                ["regimeId"] = regimeId,
                ["layerId"] = layerId,
                ["sourceId"] = sourceId,
                ["sourceKind"] = sourceKind,
                ["availability"] = availability,
                ["bodyId"] = bodyId,
                ["datasetId"] = datasetId,
                ["providerId"] = providerId,
                ["importFormat"] = importFormat,
                ["normalizedProductKind"] = normalizedProductKind,
                ["rendererContract"] = rendererContract,
            }) with
        {
            Label = label,
        };

    private static WorldGenerationGraphNode NormalizeLayerNode(
        string nodeId,
        string label,
        string regimeId,
        string layerId,
        string selectedSourceId,
        string selectedSourceKind,
        string normalizedProductKind,
        string rendererContract)
        => NodeFromSchema(
            nodeId,
            WorldFunctionProvider.LayerNormalize,
            new Dictionary<string, string>
            {
                ["sphereId"] = GeosphereSphereId,
                ["regimeId"] = regimeId,
                ["layerId"] = layerId,
                ["selectedSourceId"] = selectedSourceId,
                ["selectedSourceKind"] = selectedSourceKind,
                ["normalizedProductKind"] = normalizedProductKind,
                ["rendererContract"] = rendererContract,
            }) with
        {
            Label = label,
        };

    internal static WorldGenerationGraphNode NodeFromSchema(
        string nodeId,
        string typeId,
        IReadOnlyDictionary<string, string>? parameterOverrides = null)
    {
        var schema = WorldGenerationNodeCatalog.Find(typeId)
            ?? throw new ArgumentException($"World node schema '{typeId}' is not registered.", nameof(typeId));

        var parameters = schema.Parameters?
            .Select(parameter => parameterOverrides is not null
                                 && parameterOverrides.TryGetValue(parameter.Key, out var value)
                ? parameter with { Value = value }
                : parameter)
            .ToList();

        return new WorldGenerationGraphNode(
            NodeId: nodeId,
            TypeId: schema.TypeId,
            Label: schema.Label,
            Category: schema.Category,
            IsSideEffect: schema.IsSideEffect,
            IsExpensive: schema.IsExpensive,
            Inputs: schema.Inputs,
            Outputs: schema.Outputs,
            Parameters: parameters,
            Summary: schema.Summary,
            ProviderMetadata: schema.ProviderMetadata,
            ExecutionTraits: schema.ExecutionTraits);
    }

    private static WorldGenerationGraphNode LayerScopeNode(
        string nodeId,
        string label,
        string regimeId,
        string layerId,
        string role)
        => NodeFromSchema(
            nodeId,
            WorldFunctionProvider.LayerScope,
            new Dictionary<string, string>
            {
                ["sphereId"] = GeosphereSphereId,
                ["regimeId"] = regimeId,
                ["layerId"] = layerId,
                ["role"] = role,
            }) with
        {
            Label = label,
        };

    private static string SanitizeId(string id)
        => id.Replace('.', '_').Replace('-', '_');
}
