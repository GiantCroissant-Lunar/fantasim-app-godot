using System;
using System.Collections.Generic;
using System.Linq;
using FantaSim.App.World;

namespace FantaSim.App.World.Composition;

/// <summary>Everything a track-pipeline node handler needs to produce its own descriptor list.</summary>
public sealed class TrackPipelineBuildContext
{
    public required TrackPipelineDocument Document { get; init; }

    public WorldGenerationGraphFamilyDocument? FamilyDocument { get; init; }

    public DeclaredLayersDocument? DeclaredLayers { get; init; }

    public IReadOnlySet<string> ArchivedKeys { get; init; } = EmptySet;

    /// <summary>Descriptor lists already produced by earlier nodes, keyed by NodeId. Populated
    /// progressively by <see cref="LayerTrackRegistryBuilder"/> as it walks the document in
    /// declared order; a sink node (e.g. track-set) reads its upstream wires' entries here.</summary>
    public Dictionary<string, IReadOnlyList<LayerTrackDescriptor>> ResolvedByNodeId { get; } = new(StringComparer.Ordinal);

    private static readonly HashSet<string> EmptySet = new();
}

/// <summary>One track-pipeline node kind's executor: reads whatever it needs from
/// <see cref="TrackPipelineBuildContext"/> (and, for sink nodes, its own upstream wires) and
/// returns the descriptor list it contributes.</summary>
public delegate IReadOnlyList<LayerTrackDescriptor> TrackPipelineNodeHandler(
    TrackPipelineNode node,
    TrackPipelineBuildContext context);

/// <summary>
/// Registered catalog of track-pipeline node handlers, mirroring
/// <c>WorldGenerationNodeCatalog</c>'s shape (a lookup by kind/type-id string). No hard-coded
/// pipeline shape: <see cref="LayerTrackRegistryBuilder"/> only ever calls <see cref="Find"/> and
/// invokes whatever it returns.
/// </summary>
public static class TrackPipelineNodeCatalog
{
    private static readonly IReadOnlyDictionary<string, TrackPipelineNodeHandler> Handlers =
        new Dictionary<string, TrackPipelineNodeHandler>(StringComparer.Ordinal)
        {
            [TrackPipelineNodeKinds.FamilyLayers] = FamilyLayersNodeHandler.Execute,
            [TrackPipelineNodeKinds.DeclaredLayers] = DeclaredLayersNodeHandler.Execute,
            [TrackPipelineNodeKinds.TrackSet] = TrackSetNodeHandler.Execute,
        };

    /// <summary>Resolves the handler for <paramref name="kind"/>. Throws -- never silently skips
    /// -- when the kind has no registered handler, naming the kind in the message.</summary>
    public static TrackPipelineNodeHandler Find(string kind)
        => Handlers.TryGetValue(kind, out var handler)
            ? handler
            : throw new InvalidOperationException($"Unknown track-pipeline node kind '{kind}'.");
}

/// <summary>Source handler: one declared descriptor per <c>WorldLayerGraphBinding</c> in the
/// generation family json (the "layer-scope graphs" the family document already names).</summary>
internal static class FamilyLayersNodeHandler
{
    private static readonly LayerTrackStreamId DefaultStreamId = new("main", "default", "L0", "world", "default");
    private static readonly IReadOnlyList<string> Capabilities = new[] { "scrub", "toggle", "expand-graph" };

    public static IReadOnlyList<LayerTrackDescriptor> Execute(TrackPipelineNode node, TrackPipelineBuildContext context)
    {
        var bindings = context.FamilyDocument?.LayerGraphBindings;
        if (bindings is null || bindings.Count == 0)
            return Array.Empty<LayerTrackDescriptor>();

        return bindings
            .Select(binding => new LayerTrackDescriptor(
                SphereId: DeriveSphereId(binding.LayerId),
                LayerId: binding.LayerId,
                StreamId: DefaultStreamId,
                DisplayName: FriendlyLabel(binding.LayerId),
                State: LayerTrackStates.Declared,
                // Slice 1 leaves precise time-domain derivation to the deferred compose-json arc
                // (vault/specs/2026-07-10-layer-track-registry-design.md); a track exists for the
                // whole declared lifetime until then.
                TimeDomain: new LayerTrackTimeDomain(StartTick: 0L, EndTick: null, Rung: "ka"),
                Content: new LayerTrackContent(
                    LayerTrackContentTypes.Filmstrip,
                    Source: binding.GraphId,
                    CadenceTicks: CrustSnapshotTickSeries.DefaultSpacingTicks),
                Capabilities: Capabilities,
                SourceRef: binding.GraphId))
            .ToList();
    }

    // House convention: a layerId's dot-prefix segment IS its sphereId (e.g. "geosphere.crust" ->
    // "geosphere"). Derived explicitly here (rather than trusting WorldLayerGraphBinding.SphereId)
    // so the pipeline stays correct even against a future binding source that only names the layer.
    private static string DeriveSphereId(string layerId)
    {
        var dot = layerId.IndexOf('.');
        return dot > 0 ? layerId[..dot] : layerId;
    }

    private static string FriendlyLabel(string layerId)
    {
        var name = layerId.Split('.', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? layerId;
        return string.Join(' ', name.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Length == 0 ? part : char.ToUpperInvariant(part[0]) + part[1..]));
    }
}

/// <summary>Source handler: one declared descriptor per entry in the declared-layers json asset.</summary>
internal static class DeclaredLayersNodeHandler
{
    private static readonly LayerTrackStreamId DefaultStreamId = new("main", "default", "L0", "world", "default");
    internal const string DefaultSourceRef = "declared-layers";

    public static IReadOnlyList<LayerTrackDescriptor> Execute(TrackPipelineNode node, TrackPipelineBuildContext context)
    {
        var entries = context.DeclaredLayers?.Layers;
        if (entries is null || entries.Count == 0)
            return Array.Empty<LayerTrackDescriptor>();

        return entries
            .Select(entry => new LayerTrackDescriptor(
                SphereId: entry.SphereId,
                LayerId: entry.LayerId,
                StreamId: DefaultStreamId,
                DisplayName: entry.DisplayName,
                State: LayerTrackStates.Declared,
                TimeDomain: new LayerTrackTimeDomain(StartTick: 0L, EndTick: null, Rung: "ka"),
                Content: new LayerTrackContent(
                    entry.ContentType ?? LayerTrackContentTypes.DeclaredEmpty,
                    Source: entry.ContentSource,
                    CadenceTicks: entry.CadenceTicks),
                Capabilities: entry.Capabilities ?? Array.Empty<string>(),
                SourceRef: entry.SourceRef ?? DefaultSourceRef))
            .ToList();
    }
}

/// <summary>Sink handler: merges every source wired into this node, applies the archive overlay,
/// and stable-sorts by SphereId then LayerId (the deterministic order every view renders lanes in).</summary>
internal static class TrackSetNodeHandler
{
    public static IReadOnlyList<LayerTrackDescriptor> Execute(TrackPipelineNode node, TrackPipelineBuildContext context)
    {
        var merged = new List<LayerTrackDescriptor>();
        foreach (var wire in context.Document.Wires)
        {
            if (!string.Equals(wire.ToNodeId, node.NodeId, StringComparison.Ordinal))
                continue;
            if (context.ResolvedByNodeId.TryGetValue(wire.FromNodeId, out var upstream))
                merged.AddRange(upstream);
        }

        var withOverlay = merged.Select(track => ApplyArchiveOverlay(track, context.ArchivedKeys)).ToList();
        withOverlay.Sort((left, right) =>
        {
            var sphereCompare = string.CompareOrdinal(left.SphereId, right.SphereId);
            return sphereCompare != 0 ? sphereCompare : string.CompareOrdinal(left.LayerId, right.LayerId);
        });
        return withOverlay;
    }

    private static LayerTrackDescriptor ApplyArchiveOverlay(LayerTrackDescriptor track, IReadOnlySet<string> archivedKeys)
    {
        var isArchived = archivedKeys.Contains(LayerTrackRegistryBuilder.ArchiveKey(track.SphereId, track.LayerId));
        var isCurrentlyArchived = string.Equals(track.State, LayerTrackStates.Archived, StringComparison.Ordinal);
        if (isArchived == isCurrentlyArchived)
            return track;

        // Slice 1 only ever produces "declared" tracks from either source, so restoring from
        // archive always lands back on "declared" -- a "discovered" restore is slice-2 scope
        // (stream-discovery source), tracked in the design doc's open questions.
        return track with { State = isArchived ? LayerTrackStates.Archived : LayerTrackStates.Declared };
    }
}
