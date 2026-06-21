using System.Collections.Generic;

namespace FantaSim.App.GpuCompute;

/// <summary>A single compute shader dispatch request.</summary>
public sealed record ComputeDispatchRequest(
    ComputeShaderReference Shader,
    ComputeDispatchSize Groups,
    IReadOnlyList<ComputeBufferBinding> Buffers);
