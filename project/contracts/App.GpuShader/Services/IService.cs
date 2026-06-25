using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ServiceArchi.Contracts;
using ServiceArchi.Contracts.Attributes;

namespace FantaSim.App.GpuShader;

/// <summary>
/// T1 shader graph authoring contract. Describes GPU shader graphs using pure DTOs while
/// resident T4 seams own Godot Shader, ShaderMaterial, resource, and RID lifetimes.
/// </summary>
[ServiceContract]
public interface IService
{
    /// <summary>
    /// Return the current GPU shader graph document. This is an authoring surface; calling it
    /// must not compile shader source, allocate Godot resources, or dispatch GPU work.
    /// </summary>
    Task<GpuShaderGraphView> GetShaderGraphAsync(CancellationToken cancellationToken = default);

    /// <summary>Node types available for GPU shader graph authoring.</summary>
    Task<IReadOnlyList<GpuShaderNodeSchema>> GetShaderNodeCatalogAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Apply one authoring edit to the current shader graph and return the updated document.
    /// Invalid edits throw ArgumentException with a reason; the graph is left unchanged.
    /// </summary>
    Task<GpuShaderGraphView> ApplyShaderGraphEditAsync(
        GpuShaderGraphEdit edit,
        CancellationToken cancellationToken = default);

    /// <summary>Raised after the authored shader graph changes and consumers should re-query it.</summary>
    event Action? ShaderGraphChanged;

    /// <summary>
    /// Validate an authored shader graph against the GPU shader node catalog. This does not
    /// compile shader source, allocate Godot resources, or dispatch GPU work.
    /// </summary>
    Task<GpuShaderGraphValidationResult> ValidateShaderGraphAsync(
        GpuShaderGraphView graph,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Load a (non-compute) shader resource through the resident seam and report what it is.
    /// The path may point into a collectible bundle's PCK (e.g. "res://bundles/globe-shader/...").
    /// A missing or broken resource yields Found=false / Error, never an exception.
    /// </summary>
    Task<GpuShaderInspection> InspectShaderAsync(
        string resourcePath,
        CancellationToken cancellationToken = default);
}
