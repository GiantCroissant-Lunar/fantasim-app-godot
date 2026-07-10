using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FantaSim.App.World.Composition;

/// <summary>
/// Stream identity for one layer track, per the tunnel-design product-address convention
/// (Variation/Branch/L/Domain/Model). Slice 1 populates this with stable defaults; a future
/// slice addresses real stream variants through it. See
/// vault/specs/2026-07-10-layer-track-registry-design.md.
/// </summary>
/// <remarks>
/// <see cref="JsonPropertyNameAttribute"/> pins every wire key to camelCase explicitly (the
/// spec sketch's wire format) regardless of a caller's <c>JsonSerializerOptions</c> -- this is a
/// cross-process/cross-tool (eventually Unity) contract, not an in-process DTO.
/// </remarks>
public sealed record LayerTrackStreamId(
    [property: JsonPropertyName("variation")] string Variation,
    [property: JsonPropertyName("branch")] string Branch,
    [property: JsonPropertyName("l")] string L,
    [property: JsonPropertyName("domain")] string Domain,
    [property: JsonPropertyName("model")] string Model);

/// <summary>
/// Canonical-tick time bounds for a track. <see cref="EndTick"/> null means open-ended (the
/// track is still current). <see cref="Rung"/> is the odometer-ladder display rung (e.g. "ka"),
/// never Ma/Ga.
/// </summary>
public sealed record LayerTrackTimeDomain(
    [property: JsonPropertyName("startTick")] long StartTick,
    [property: JsonPropertyName("endTick")] long? EndTick,
    [property: JsonPropertyName("rung")] string Rung);

/// <summary>
/// Describes how a track's content renders. <see cref="Type"/> is an extensible string (seed
/// vocabulary: world-context, filmstrip, series, graph, observations, events) -- a view with no
/// presenter registered for a given type must fall back to a generic labeled strip, never throw
/// or go invisible (the Unity round-trip degradation guarantee).
/// </summary>
public sealed record LayerTrackContent(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("source")] string? Source = null,
    [property: JsonPropertyName("cadenceTicks")] long? CadenceTicks = null);

/// <summary>Well-known <see cref="LayerTrackContent.Type"/> values seeded in slice 1.</summary>
public static class LayerTrackContentTypes
{
    public const string WorldContext = "world-context";
    public const string Filmstrip = "filmstrip";
    public const string Series = "series";
    public const string Graph = "graph";
    public const string Observations = "observations";
    public const string Events = "events";

    /// <summary>
    /// A declared-always layer with no content yet (pre-onset, or a source not wired). No
    /// presenter is registered for this type on purpose: it must fall through to the generic
    /// dimmed strip, which IS the declared-always contract (never invisible, never a crash).
    /// </summary>
    public const string DeclaredEmpty = "declared-empty";
}

/// <summary>Well-known <see cref="LayerTrackDescriptor.State"/> values. A plain string (not an
/// enum) per the house schema-first rule: unknown values must round-trip, not throw.</summary>
public static class LayerTrackStates
{
    /// <summary>Always has a track, even before it produces content (family json or declared-layers).</summary>
    public const string Declared = "declared";

    /// <summary>Surfaced by truth-stream discovery on first content. Slice 2.</summary>
    public const string Discovered = "discovered";

    /// <summary>User-hidden; the underlying truth-stream data is never destroyed.</summary>
    public const string Archived = "archived";
}

/// <summary>
/// One row in the layer-track registry: everything a view needs to place, label, and render a
/// track without knowing the domain concept behind it. Canonical ticks everywhere; no Ma/Ga
/// vocabulary. See vault/specs/2026-07-10-layer-track-registry-design.md for the schema
/// rationale and vault/plans/2026-07-10-layer-track-registry-slice1-plan.md for scope.
/// </summary>
public sealed record LayerTrackDescriptor(
    [property: JsonPropertyName("sphereId")] string SphereId,
    [property: JsonPropertyName("layerId")] string LayerId,
    [property: JsonPropertyName("streamId")] LayerTrackStreamId StreamId,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("timeDomain")] LayerTrackTimeDomain TimeDomain,
    [property: JsonPropertyName("content")] LayerTrackContent Content,
    [property: JsonPropertyName("capabilities")] IReadOnlyList<string> Capabilities,
    [property: JsonPropertyName("sourceRef")] string SourceRef);
