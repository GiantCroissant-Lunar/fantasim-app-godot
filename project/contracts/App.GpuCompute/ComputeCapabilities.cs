namespace FantaSim.App.GpuCompute;

/// <summary>Backend availability for the current runtime.</summary>
public sealed record ComputeCapabilities(
    bool IsAvailable,
    string BackendName,
    string? UnavailableReason = null);
