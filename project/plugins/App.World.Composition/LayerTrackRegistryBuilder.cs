using System;
using System.Collections.Generic;
using FantaSim.App.World;

namespace FantaSim.App.World.Composition;

/// <summary>
/// Pure interpreter for a track-pipeline json: walks the document's nodes in declared order,
/// resolving each through <see cref="TrackPipelineNodeCatalog"/>, and returns the track-set
/// node's output as a <see cref="LayerTrackRegistrySnapshot"/>. No file I/O, no Godot, no
/// mutable service state -- <see cref="LayerTrackRegistryService"/> (Task 2) owns all of that
/// and calls through this pure function on every (re)build.
/// </summary>
public static class LayerTrackRegistryBuilder
{
    public static LayerTrackRegistrySnapshot Build(
        TrackPipelineDocument document,
        WorldGenerationGraphFamilyDocument? familyDocument,
        DeclaredLayersDocument? declaredLayers,
        IReadOnlySet<string> archivedKeys,
        int revision,
        IReadOnlyList<DiscoveredTrackRecord>? discoveredTracks = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(archivedKeys);

        var context = new TrackPipelineBuildContext
        {
            Document = document,
            FamilyDocument = familyDocument,
            DeclaredLayers = declaredLayers,
            ArchivedKeys = archivedKeys,
            DiscoveredTracks = discoveredTracks ?? Array.Empty<DiscoveredTrackRecord>(),
        };

        IReadOnlyList<LayerTrackDescriptor> trackSetResult = Array.Empty<LayerTrackDescriptor>();
        foreach (var node in document.Nodes)
        {
            // Find throws (naming the kind) for an unregistered kind -- no silent skip. Every
            // node in the document is resolved, not just the ones a wire happens to reach, so an
            // authoring mistake surfaces immediately rather than at first use.
            var handler = TrackPipelineNodeCatalog.Find(node.Kind);
            var result = handler(node, context);
            context.ResolvedByNodeId[node.NodeId] = result;

            if (string.Equals(node.Kind, TrackPipelineNodeKinds.TrackSet, StringComparison.Ordinal))
                trackSetResult = result;
        }

        return new LayerTrackRegistrySnapshot(revision, trackSetResult);
    }

    /// <summary>Stable key for the archive overlay set: "{sphereId}:{layerId}".</summary>
    public static string ArchiveKey(string sphereId, string layerId) => $"{sphereId}:{layerId}";
}
