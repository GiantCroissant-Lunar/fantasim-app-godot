using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace FantaSim.App.World.Composition;

/// <summary>
/// One node in a track-pipeline json: a "kind" string (see <see cref="TrackPipelineNodeKinds"/>)
/// plus JSON-schema'd params. Same structural family as the world-generation graphs (kind + params
/// + wires + SchemaVersion + Revision) per
/// vault/specs/2026-07-10-layer-track-registry-design.md -- pipelines and policies are data, code
/// is only a catalog of small registered handlers (<see cref="TrackPipelineNodeCatalog"/>).
/// </summary>
public sealed record TrackPipelineNode(string NodeId, string Kind, JsonObject? Params = null);

/// <summary>One edge feeding one node's output into another node's input.</summary>
public sealed record TrackPipelineWire(string FromNodeId, string ToNodeId);

/// <summary>The pipeline json itself: how tracks materialize (sources -&gt; merge -&gt; track-set).</summary>
public sealed record TrackPipelineDocument(
    string DocumentId,
    int SchemaVersion,
    int Revision,
    IReadOnlyList<TrackPipelineNode> Nodes,
    IReadOnlyList<TrackPipelineWire> Wires);

/// <summary>Slice-1 track-pipeline node kinds. An unregistered kind is a hard error, never a
/// silent skip -- see <see cref="TrackPipelineNodeCatalog.Find"/>.</summary>
public static class TrackPipelineNodeKinds
{
    /// <summary>Source: one descriptor per <c>WorldLayerGraphBinding</c> in the generation family json.</summary>
    public const string FamilyLayers = "family-layers";

    /// <summary>Source: one descriptor per entry in the declared-layers json asset (the
    /// declared-always contract for layers with no generation graph yet).</summary>
    public const string DeclaredLayers = "declared-layers";

    /// <summary>Sink: merges every wired-in source, applies the archive overlay, and stable-sorts
    /// by SphereId then LayerId.</summary>
    public const string TrackSet = "track-set";
}

/// <summary>One entry in the declared-layers json asset.</summary>
public sealed record DeclaredLayerEntry(
    string SphereId,
    string LayerId,
    string DisplayName,
    string? ContentType = null,
    string? ContentSource = null,
    long? CadenceTicks = null,
    IReadOnlyList<string>? Capabilities = null,
    string? SourceRef = null);

/// <summary>
/// The declared-layers json asset: additional declared-but-not-yet-generating layers (e.g. a
/// sphere with no generation graphs, such as today's atmosphere). Lives beside the pipeline json,
/// not inside the family json -- composition is an R/M presentation product, not causal truth.
/// </summary>
public sealed record DeclaredLayersDocument(
    int SchemaVersion,
    IReadOnlyList<DeclaredLayerEntry> Layers);
