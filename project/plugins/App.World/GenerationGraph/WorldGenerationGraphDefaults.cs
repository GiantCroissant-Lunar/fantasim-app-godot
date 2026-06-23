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
    public const string GeosphereSphereId = "geosphere";

    public static WorldGenerationGraphFamilyDocument BuildFamily()
    {
        var baseGraph = BuildCrustGraph(BaseGraphId, "World Creation");
        var formationGraph = BuildCrustGraph(FormationGraphId, "Planetesimal Swarm");
        var geosphereGraph = BuildCrustGraph(GeosphereGraphId, "Mobile Plate Geosphere");

        return new WorldGenerationGraphFamilyDocument(
            DocumentId: DocumentId,
            SchemaVersion: 1,
            Revision: 1,
            BaseGraph: baseGraph,
            Graphs: new[] { formationGraph, geosphereGraph },
            RegimeGraphBindings: new[]
            {
                new WorldRegimeGraphBinding(
                    WorldRegimeScheduleKinds.BodyFormation,
                    "planetesimal-swarm",
                    FormationGraphId),
                new WorldRegimeGraphBinding(
                    WorldRegimeScheduleKinds.Sphere,
                    "mobile-plate",
                    GeosphereGraphId,
                    SphereId: GeosphereSphereId),
            },
            GraphOverrides: Array.Empty<WorldGenerationGraphScopedOverride>(),
            LegacyOverrides: Array.Empty<WorldGenerationGraphOverride>(),
            RunHistory: Array.Empty<WorldGenerationRunHistoryEntry>(),
            UpdatedUtc: DateTimeOffset.UtcNow,
            SubgraphBindings: new[]
            {
                new WorldGenerationSubgraphBinding(BaseGraphId, "crust", GeosphereGraphId),
                new WorldGenerationSubgraphBinding(GeosphereGraphId, "crust", "geosphere.mobile-plate.layers"),
            });
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
}
