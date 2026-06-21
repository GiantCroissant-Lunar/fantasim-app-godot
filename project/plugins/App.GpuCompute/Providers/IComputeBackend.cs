using System;
using System.Threading;
using System.Threading.Tasks;

namespace FantaSim.App.GpuCompute.Providers;

/// <summary>
/// Lean engine seam owned by the T3 compute service and implemented by the resident T4 backend.
/// </summary>
public interface IComputeBackend : IDisposable
{
    ComputeCapabilities Capabilities { get; }

    Task<ComputeDispatchResult> DispatchAsync(
        ComputeDispatchRequest request,
        CancellationToken cancellationToken = default);
}
