using System.Collections.Generic;

namespace FantaSim.App.World.Composition;

/// <summary>
/// One truth-stream-discovered track, as surfaced by an injected discovery provider (mirror of
/// the family-document provider seam in <see cref="LayerTrackRegistryService"/>). App-side only
/// -- not a contracts type -- because the engine's <c>ITruthEventStore</c> has no stream-
/// enumeration API today; discovery therefore always arrives through app code that already knows
/// which streams exist (see vault/plans/2026-07-10-layer-track-registry-slice2-plan.md Task 2).
/// </summary>
public sealed record DiscoveredTrackRecord(
    string SphereId,
    string LayerId,
    LayerTrackStreamId StreamId,
    string DisplayName,
    string ContentType,
    string? ContentSource = null,
    long? CadenceTicks = null,
    IReadOnlyList<string>? Capabilities = null,
    string? SourceRef = null);
