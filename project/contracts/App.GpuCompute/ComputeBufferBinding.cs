namespace FantaSim.App.GpuCompute;

/// <summary>Storage-buffer binding for a compute dispatch.</summary>
public sealed record ComputeBufferBinding(
    uint Set,
    uint Binding,
    byte[] Data,
    uint? ReadbackBytes = null,
    string? ReadbackLabel = null);
