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
    public const string GeosphereSphereId = "geosphere";

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
            });
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
        var node = LayerScopeNode("layer_scope", label, regimeId, layerId, role);

        return new WorldGenerationGraphView(
            GraphId: graphId,
            Label: label,
            Description: $"Layer graph for {layerId} in the {regimeId} regime.",
            Nodes: new[] { node },
            Wires: Array.Empty<WorldGenerationGraphWire>(),
            Annotations: new[]
            {
                new WorldGenerationGraphAnnotation(
                    AnnotationId: $"comment_{SanitizeId(graphId)}",
                    Kind: WorldGenerationGraphAnnotationKinds.CommentBoundary,
                    Label: label,
                    Bounds: new WorldGenerationGraphBounds(-80, -80, 420, 220),
                    NodeIds: new[] { "layer_scope" },
                    Text: "Executable metadata placeholder for a layer-owned generation subgraph.",
                    Color: "#f6c85f"),
            },
            OutputNodeIds: new[] { "layer_scope" });
    }

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
            Parameters: parameters);
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
