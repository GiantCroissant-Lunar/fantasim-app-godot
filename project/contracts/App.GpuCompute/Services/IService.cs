using System;
using System.Threading;
using System.Threading.Tasks;
using ServiceArchi.Contracts;
using ServiceArchi.Contracts.Attributes;

namespace FantaSim.App.GpuCompute;

/// <summary>
/// T1 compute service contract. Describes compute workloads using pure DTOs while the resident
/// T4 seam owns the Godot RenderingDevice, shader resources, buffers, and RID cleanup.
/// </summary>
[ServiceContract]
[SelectionStrategy(SelectionMode.HighestPriority)]
public interface IService
{
    /// <summary>Current backend capability snapshot.</summary>
    ComputeCapabilities Capabilities { get; }

    /// <summary>
    /// Dispatch one imported RDShaderFile compute shader with storage-buffer bindings.
    /// Implementations may return a failed result for unavailable devices or compile/runtime errors.
    /// </summary>
    Task<ComputeDispatchResult> DispatchAsync(
        ComputeDispatchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Raised after a dispatch completes or fails.</summary>
    event Action<ComputeDispatchResult>? DispatchCompleted;
}
