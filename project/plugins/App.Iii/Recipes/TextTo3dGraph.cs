using System.Text.Json.Nodes;
using FantaSim.App.NodeGraph;

namespace FantaSim.App.Iii.Recipes;

/// <summary>
/// The text->3D pipeline as a data-driven graph: comfy.generate -> blender.refine -> asset.to_gltf.
/// A recipe is a named <see cref="GraphDocument"/> builder; this one ships with App.Iii. Bundles
/// author their own recipes against FantaSim.App.NodeGraph directly.
/// </summary>
public static class TextTo3dGraph
{
    public static GraphDocument Build(string prompt) => new(
        Nodes: new[]
        {
            new GraphNode("comfy", "comfy.generate", new JsonObject { ["prompt"] = prompt }),
            new GraphNode("refine", "blender.refine", new JsonObject { ["prompt"] = prompt }),
            new GraphNode("gltf", "asset.to_gltf", new JsonObject()),
        },
        Wires: new[]
        {
            new GraphWire("comfy", "path", "refine", "source"),
            new GraphWire("refine", "usd_path", "gltf", "source"),
        },
        SinkNodeId: "gltf");
}
