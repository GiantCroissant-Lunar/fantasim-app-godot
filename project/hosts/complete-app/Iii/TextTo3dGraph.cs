using System.Text.Json.Nodes;

namespace FantaSim.App.Iii;

/// <summary>
/// The text→3D pipeline expressed as a data-driven graph — the replacement for the Python
/// pipeline-worker's hard-coded DAG. Same chain: comfy.generate → blender.refine → asset.to_gltf,
/// with job_id supplied as a shared param by the executor.
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
            new GraphWire("comfy", "path", "refine", "source"),       // mesh .obj -> refine source
            new GraphWire("refine", "usd_path", "gltf", "source"),    // refined USD -> gltf source
        },
        SinkNodeId: "gltf");
}
