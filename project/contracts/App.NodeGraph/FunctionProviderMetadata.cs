namespace FantaSim.App.NodeGraph;

/// <summary>Provider metadata describing how a function or tool is implemented.
/// Kept as an optional, app-safe record so manifests can carry execution origin
/// without the graph surface branching on internal vs external identity.</summary>
public sealed record FunctionProviderMetadata(
    string ProviderKind,
    string? ProviderId = null,
    string? RuntimeRequirement = null,
    string? Determinism = null,
    string? TrustLevel = null);
