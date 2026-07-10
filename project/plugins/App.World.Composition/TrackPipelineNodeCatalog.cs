using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using FantaSim.App.World;

namespace FantaSim.App.World.Composition;

/// <summary>Everything a track-pipeline node handler needs to produce its own descriptor list.</summary>
public sealed class TrackPipelineBuildContext
{
    public required TrackPipelineDocument Document { get; init; }

    public WorldGenerationGraphFamilyDocument? FamilyDocument { get; init; }

    public DeclaredLayersDocument? DeclaredLayers { get; init; }

    /// <summary>Truth-stream-discovered records from the injected discovery provider seam
    /// (empty when no provider is wired or it currently has nothing to report). See
    /// <see cref="StreamDiscoveryNodeHandler"/> and
    /// vault/plans/2026-07-10-layer-track-registry-slice2-plan.md Task 2.</summary>
    public IReadOnlyList<DiscoveredTrackRecord> DiscoveredTracks { get; init; } = Array.Empty<DiscoveredTrackRecord>();

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
            [TrackPipelineNodeKinds.StreamDiscovery] = StreamDiscoveryNodeHandler.Execute,
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

/// <summary>Source handler: one discovered descriptor per <see cref="DiscoveredTrackRecord"/> the
/// injected discovery provider seam returns. The truth-stream-discovery half of the hybrid model
/// (vault/specs/2026-07-10-layer-track-registry-design.md decision 1): a record's own
/// <see cref="DiscoveredTrackRecord.SphereId"/>/<see cref="DiscoveredTrackRecord.LayerId"/> drive
/// placement (unlike <see cref="FamilyLayersNodeHandler"/>, nothing is derived from a naming
/// convention here -- the provider already knows both).</summary>
internal static class StreamDiscoveryNodeHandler
{
    private static readonly IReadOnlyList<string> DefaultCapabilities = new[] { "scrub", "toggle" };
    private const string DefaultSourceRef = "stream-discovery";

    public static IReadOnlyList<LayerTrackDescriptor> Execute(TrackPipelineNode node, TrackPipelineBuildContext context)
    {
        var records = context.DiscoveredTracks;
        if (records is null || records.Count == 0)
            return Array.Empty<LayerTrackDescriptor>();

        return records
            .Select(record => new LayerTrackDescriptor(
                SphereId: record.SphereId,
                LayerId: record.LayerId,
                StreamId: record.StreamId,
                DisplayName: record.DisplayName,
                State: LayerTrackStates.Discovered,
                // Slice 1's compose-json arc governs precise time-domain derivation (deferred);
                // a discovered track exists for its whole known lifetime until then, same as the
                // declared sources.
                TimeDomain: new LayerTrackTimeDomain(StartTick: 0L, EndTick: null, Rung: "ka"),
                Content: new LayerTrackContent(
                    record.ContentType,
                    Source: record.ContentSource,
                    CadenceTicks: record.CadenceTicks),
                Capabilities: record.Capabilities ?? DefaultCapabilities,
                SourceRef: record.SourceRef ?? record.ContentSource ?? DefaultSourceRef))
            .ToList();
    }
}

/// <summary>Sink handler: merges every source wired into this node, applies the archive overlay,
/// and stable-sorts by SphereId then LayerId (the deterministic order every view renders lanes in).
/// An optional <c>laneOrder</c> param (JSON array of sphereId strings) overrides sphere precedence
/// -- see <see cref="ParseLaneOrder"/> -- restoring a shipped-asset choice like geosphere-first
/// (vault/plans/2026-07-10-layer-track-registry-slice2-plan.md Task 3).</summary>
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

        var laneOrder = ParseLaneOrder(node.Params);
        var withOverlay = merged.Select(track => ApplyArchiveOverlay(track, context.ArchivedKeys)).ToList();
        withOverlay.Sort((left, right) =>
        {
            var laneCompare = LaneRank(left.SphereId, laneOrder).CompareTo(LaneRank(right.SphereId, laneOrder));
            if (laneCompare != 0)
                return laneCompare;

            var sphereCompare = string.CompareOrdinal(left.SphereId, right.SphereId);
            return sphereCompare != 0 ? sphereCompare : string.CompareOrdinal(left.LayerId, right.LayerId);
        });
        return withOverlay;
    }

    /// <summary>Reads the optional <c>laneOrder</c> param: a JSON array of sphereId strings naming
    /// sphere precedence. Read via JsonNode/JsonObject (never anonymous-type serialization -- the
    /// ALC-pin house rule) since <see cref="TrackPipelineNode.Params"/> is already a JsonObject.
    /// Missing param, missing key, non-array value, or an empty array all fall back to an empty
    /// list, which <see cref="LaneRank"/> treats as "no sphere is listed" -- exactly today's
    /// alphabetical-by-sphereId behavior. Non-string / null entries are skipped rather than
    /// throwing, so one malformed entry does not break the whole param.</summary>
    private static IReadOnlyList<string> ParseLaneOrder(JsonObject? nodeParams)
    {
        if (nodeParams is null)
            return Array.Empty<string>();
        if (!nodeParams.TryGetPropertyValue("laneOrder", out var laneOrderNode) || laneOrderNode is not JsonArray laneOrderArray)
            return Array.Empty<string>();

        var order = new List<string>();
        foreach (var entry in laneOrderArray)
        {
            if (entry is JsonValue value && value.TryGetValue<string>(out var sphereId) && !string.IsNullOrEmpty(sphereId))
                order.Add(sphereId);
        }
        return order;
    }

    /// <summary>A listed sphereId's rank is its index in <paramref name="laneOrder"/>; an unlisted
    /// sphereId ranks after every listed one (so it sorts last among ranks, then falls back to
    /// ordinal sphereId comparison against its unlisted peers).</summary>
    private static int LaneRank(string sphereId, IReadOnlyList<string> laneOrder)
    {
        for (var i = 0; i < laneOrder.Count; i++)
        {
            if (string.Equals(laneOrder[i], sphereId, StringComparison.Ordinal))
                return i;
        }
        return int.MaxValue;
    }

    private static LayerTrackDescriptor ApplyArchiveOverlay(LayerTrackDescriptor track, IReadOnlySet<string> archivedKeys)
    {
        var isArchived = archivedKeys.Contains(LayerTrackRegistryBuilder.ArchiveKey(track.SphereId, track.LayerId));
        if (!isArchived)
            // Not overlaid: leave the descriptor exactly as its source produced it -- "declared"
            // from family-layers/declared-layers, "discovered" from stream-discovery, whatever a
            // future source contributes. Restoring from archive never hardcodes a state (Task 1,
            // vault/plans/2026-07-10-layer-track-registry-slice2-plan.md): every rebuild re-runs
            // the source fresh (LayerTrackRegistryService.BuildSnapshotLocked), so this is exactly
            // what a track looks like post-restore.
            return track;

        return string.Equals(track.State, LayerTrackStates.Archived, StringComparison.Ordinal)
            ? track
            : track with { State = LayerTrackStates.Archived };
    }
}
