namespace FantaSim.App.NodeGraph;

/// <summary>Execution traits that influence scheduling, trust, and runtime policy
/// for a provider-backed function. These are metadata, not separate node classes.</summary>
public sealed record FunctionExecutionTraits(
    bool? RequiresExternalProcess = null,
    bool? RequiresNetwork = null,
    bool? RequiresMainThread = null,
    bool? IsDeterministic = null,
    bool? SupportsCancellation = null,
    int? DefaultTimeoutSeconds = null,
    string? CacheKeyShape = null,
    string? ArtifactShape = null,
    string? CommitEligibility = null);
