using System;
using System.Collections.Generic;

namespace FantaSim.App.World;

/// <summary>
/// App-facing, ALC-safe description of one world-generation graph. This is an
/// authoring document, not execution state: UI bundles and agents can inspect it
/// without crossing into fantasim-world, ECS, or Godot runtime types.
/// </summary>
public sealed record WorldGenerationGraphView(
    string GraphId,
    string Label,
    string Description,
    IReadOnlyList<WorldGenerationGraphNode> Nodes,
    IReadOnlyList<WorldGenerationGraphWire> Wires,
    IReadOnlyList<WorldGenerationGraphAnnotation>? Annotations = null,
    IReadOnlyList<string>? OutputNodeIds = null);

/// <summary>One typed node in a world-generation graph.</summary>
public sealed record WorldGenerationGraphNode(
    string NodeId,
    string TypeId,
    string Label,
    string Category,
    bool IsSideEffect,
    bool IsExpensive,
    IReadOnlyList<WorldGenerationGraphPort> Inputs,
    IReadOnlyList<WorldGenerationGraphPort> Outputs,
    IReadOnlyList<WorldGenerationGraphParameter>? Parameters = null);

/// <summary>One typed input or output port on a world-generation node.</summary>
public sealed record WorldGenerationGraphPort(
    string PortId,
    string Label,
    string KindHint,
    bool Required);

/// <summary>One wire between typed output and input ports.</summary>
public sealed record WorldGenerationGraphWire(
    string FromNodeId,
    string FromPortId,
    string ToNodeId,
    string ToPortId,
    string KindHint);

/// <summary>One editable parameter on a generation node. Values are string-encoded for DTO stability.</summary>
public sealed record WorldGenerationGraphParameter(
    string Key,
    string Label,
    string Value,
    string KindHint);

/// <summary>A node type available for authoring: the port template new nodes are created from.</summary>
public sealed record WorldGenerationNodeSchema(
    string TypeId,
    string Label,
    string Category,
    bool IsSideEffect,
    bool IsExpensive,
    IReadOnlyList<WorldGenerationGraphPort> Inputs,
    IReadOnlyList<WorldGenerationGraphPort> Outputs,
    string Summary,
    IReadOnlyList<WorldGenerationGraphParameter>? Parameters = null);

/// <summary>
/// Authoring-only annotation kinds. These are intentionally not executable node types.
/// </summary>
public static class WorldGenerationGraphAnnotationKinds
{
    public const string CommentBoundary = "comment-boundary";
    public const string GroupBoundary = "group-boundary";
}

/// <summary>Canvas-space bounds for a non-executable graph annotation.</summary>
public sealed record WorldGenerationGraphBounds(float X, float Y, float Width, float Height);

/// <summary>
/// A non-executable annotation such as a comment frame or grouping boundary.
/// Compilers must strip annotations before handing a graph to the generic executor.
/// </summary>
public sealed record WorldGenerationGraphAnnotation(
    string AnnotationId,
    string Kind,
    string Label,
    WorldGenerationGraphBounds Bounds,
    IReadOnlyList<string> NodeIds,
    string? Text = null,
    string? Color = null);

/// <summary>
/// A parent graph node that opens a named graph in the same family. The binding
/// makes subgraphs explicit without forcing the generic executor to understand them.
/// </summary>
public sealed record WorldGenerationSubgraphBinding(
    string ParentGraphId,
    string NodeId,
    string SubgraphId,
    IReadOnlyDictionary<string, string>? InputPortMap = null,
    IReadOnlyDictionary<string, string>? OutputPortMap = null);

/// <summary>
/// One authoring edit against a generation graph. Kind selects the operation;
/// unused fields stay null.
/// </summary>
public sealed record WorldGenerationGraphEdit(
    string Kind,
    string? NodeId = null,
    string? TypeId = null,
    string? FromNodeId = null,
    string? FromPortId = null,
    string? ToNodeId = null,
    string? ToPortId = null,
    string? ParamKey = null,
    string? ParamValue = null);

/// <summary>Per-node outcome of a graph run. Status values are "ok", "skipped", or "error".</summary>
public sealed record WorldGenerationNodeRunResult(
    string NodeId,
    string TypeId,
    string Status,
    string? Message);

/// <summary>Outcome of executing an authored generation graph.</summary>
public sealed record WorldGenerationRunResult(
    bool Succeeded,
    int GraphRevision,
    IReadOnlyList<WorldGenerationNodeRunResult> Nodes,
    IReadOnlyList<string> Products);

/// <summary>
/// Snapshot of current world products for timeline/UI surfaces. Querying this does
/// not trigger generation.
/// </summary>
public sealed record WorldGenerationProductsView(
    int GraphRevision,
    IReadOnlyList<string> Products,
    long ReferenceTick,
    IReadOnlyList<long> CachedTicks);

/// <summary>
/// Optional per-node preview payload: raw RGBA8 image bytes, row-major, no padding.
/// The resident seam turns the bytes into a texture.
/// </summary>
public sealed record WorldGenerationNodePreview(
    string NodeId,
    int Width,
    int Height,
    byte[] Rgba);

/// <summary>Inclusive canonical-tick range an override layer applies to.</summary>
public sealed record WorldGenerationTickRange(long StartTick, long EndTick)
{
    public bool Contains(long tick) => tick >= StartTick && tick <= EndTick;
}

/// <summary>
/// A sparse override layer over the base generation graph. Higher StrengthOrder
/// composes later, so later layers are stronger.
/// </summary>
public sealed record WorldGenerationGraphOverride(
    string OverrideId,
    string Label,
    WorldGenerationTickRange Range,
    int StrengthOrder,
    IReadOnlyList<WorldGenerationGraphEdit> Edits);

/// <summary>
/// Legacy persisted document for a single authored world-generation graph. New
/// code should prefer <see cref="WorldGenerationGraphFamilyDocument"/>.
/// </summary>
public sealed record WorldGenerationGraphDocument(
    string DocumentId,
    int SchemaVersion,
    int Revision,
    WorldGenerationGraphView BaseGraph,
    IReadOnlyList<WorldGenerationGraphOverride> Overrides,
    IReadOnlyList<WorldGenerationRunHistoryEntry> RunHistory,
    DateTimeOffset UpdatedUtc);

/// <summary>Flattened persistence subsystem status for the generation graph document.</summary>
public sealed record WorldGenerationGraphPersistenceStatus(
    bool Enabled,
    string StoreKind,
    string StorePath,
    string DocumentId,
    bool IsLoaded,
    bool IsSaved,
    DateTimeOffset? LoadedAtUtc,
    DateTimeOffset? SavedAtUtc,
    int Revision,
    string? ContentHash,
    string? Warning);

/// <summary>One entry in generation graph run history.</summary>
public sealed record WorldGenerationRunHistoryEntry(
    string RunId,
    long Tick,
    int GraphRevision,
    string DocumentId,
    string EffectiveGraphId,
    string ContentHash,
    bool Succeeded,
    IReadOnlyList<string> Products,
    DateTimeOffset CreatedUtc);

/// <summary>Well-known regime schedule kind constants.</summary>
public static class WorldRegimeScheduleKinds
{
    /// <summary>Body formation regime schedule, before a hydrostatic sphere exists.</summary>
    public const string BodyFormation = "body-formation";

    /// <summary>Sphere regime schedule, after the body is a sphere-like geosphere.</summary>
    public const string Sphere = "sphere";
}

/// <summary>
/// Binds a regime within a schedule to a named graph in the family.
/// </summary>
public sealed record WorldRegimeGraphBinding(
    string ScheduleKind,
    string RegimeId,
    string GraphId,
    string? SphereId = null);

/// <summary>
/// A graph-scoped override layer. Edits apply only to one named graph when the
/// playhead tick falls inside Range.
/// </summary>
public sealed record WorldGenerationGraphScopedOverride(
    string OverrideId,
    string GraphId,
    string Label,
    WorldGenerationTickRange Range,
    int StrengthOrder,
    IReadOnlyList<WorldGenerationGraphEdit> Edits);

/// <summary>
/// Target authoring model for world creation: base graph for compatibility,
/// named regime/layer graphs, graph-scoped overrides, explicit subgraphs, and
/// run history.
/// </summary>
public sealed record WorldGenerationGraphFamilyDocument(
    string DocumentId,
    int SchemaVersion,
    int Revision,
    WorldGenerationGraphView BaseGraph,
    IReadOnlyList<WorldGenerationGraphView> Graphs,
    IReadOnlyList<WorldRegimeGraphBinding> RegimeGraphBindings,
    IReadOnlyList<WorldGenerationGraphScopedOverride> GraphOverrides,
    IReadOnlyList<WorldGenerationGraphOverride> LegacyOverrides,
    IReadOnlyList<WorldGenerationRunHistoryEntry> RunHistory,
    DateTimeOffset UpdatedUtc,
    IReadOnlyList<WorldGenerationSubgraphBinding>? SubgraphBindings = null);

/// <summary>
/// Cache key for a generation-graph execution scope. This identifies which
/// graph/regime/revision/branch produced a cached value.
/// </summary>
public sealed record WorldGenerationGraphExecutionScopeKey
{
    public WorldGenerationGraphExecutionScopeKey(
        string LifecycleKind,
        string RegimeId,
        string GraphId,
        int GraphRevision,
        int ScheduleRevision,
        string Variant,
        string Branch)
    {
        SegmentGuards.RequireCacheSegment(LifecycleKind, nameof(LifecycleKind));
        SegmentGuards.RequireCacheSegment(RegimeId, nameof(RegimeId));
        SegmentGuards.RequireCacheSegment(GraphId, nameof(GraphId));
        SegmentGuards.RequireCacheSegment(Variant, nameof(Variant));
        SegmentGuards.RequireCacheSegment(Branch, nameof(Branch));

        this.LifecycleKind = LifecycleKind;
        this.RegimeId = RegimeId;
        this.GraphId = GraphId;
        this.GraphRevision = GraphRevision;
        this.ScheduleRevision = ScheduleRevision;
        this.Variant = Variant;
        this.Branch = Branch;
    }

    public string LifecycleKind { get; init; }
    public string RegimeId { get; init; }
    public string GraphId { get; init; }
    public int GraphRevision { get; init; }
    public int ScheduleRevision { get; init; }
    public string Variant { get; init; }
    public string Branch { get; init; }

    /// <summary>
    /// Format: {LifecycleKind}:{RegimeId}:{GraphId}:G{GraphRevision}:S{ScheduleRevision}:{Variant}:{Branch}
    /// </summary>
    public string ToCacheKey()
        => $"{LifecycleKind}:{RegimeId}:{GraphId}:G{GraphRevision}:S{ScheduleRevision}:{Variant}:{Branch}";

    public override string ToString() => ToCacheKey();
}

/// <summary>
/// Stable product address in prim-path style for diagnostics, persistence, and export.
/// Format: /{Variant}/{Branch}/{Domain}/{Product}@{Tick}
/// </summary>
public sealed record WorldGenerationProductAddress
{
    public WorldGenerationProductAddress(
        string Variant,
        string Branch,
        string Domain,
        string Product,
        long Tick)
    {
        SegmentGuards.RequirePathSegment(Variant, nameof(Variant));
        SegmentGuards.RequirePathSegment(Branch, nameof(Branch));
        SegmentGuards.RequirePathSegment(Domain, nameof(Domain));
        SegmentGuards.RequirePathSegment(Product, nameof(Product));

        this.Variant = Variant;
        this.Branch = Branch;
        this.Domain = Domain;
        this.Product = Product;
        this.Tick = Tick;
    }

    public string Variant { get; init; }
    public string Branch { get; init; }
    public string Domain { get; init; }
    public string Product { get; init; }
    public long Tick { get; init; }

    public string ToPath() => $"/{Variant}/{Branch}/{Domain}/{Product}@{Tick}";

    public override string ToString() => ToPath();
}

internal static class SegmentGuards
{
    internal static void RequireCacheSegment(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Segment must be non-empty.", parameterName);
        if (value.Contains(':'))
            throw new ArgumentException("Cache-key segments must not contain ':'.", parameterName);
    }

    internal static void RequirePathSegment(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Segment must be non-empty.", parameterName);
        if (value.Contains('/') || value.Contains('@'))
            throw new ArgumentException("Product-address segments must not contain '/' or '@'.", parameterName);
    }
}
