using System.Collections.Generic;

namespace FantaSim.App.GpuCompute;

/// <summary>Result of a compute dispatch, including any requested buffer readbacks.</summary>
public sealed record ComputeDispatchResult(
    string ShaderId,
    bool Succeeded,
    IReadOnlyDictionary<string, byte[]> ReadbackBuffers,
    string? ErrorMessage = null)
{
    public static ComputeDispatchResult Failed(string shaderId, string errorMessage)
        => new(shaderId, false, new Dictionary<string, byte[]>(), errorMessage);
}
