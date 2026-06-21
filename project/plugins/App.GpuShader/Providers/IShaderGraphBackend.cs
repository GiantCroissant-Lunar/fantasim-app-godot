using System;
using System.Threading;
using System.Threading.Tasks;

namespace FantaSim.App.GpuShader.Providers;

/// <summary>
/// Resident seam contract: loads Godot shader resources on behalf of the engine-agnostic T3
/// service. Godot Shader/ShaderMaterial objects never cross this boundary — only DTOs do.
/// </summary>
public interface IShaderGraphBackend : IDisposable
{
    /// <summary>Load the shader resource at the given res:// path and describe it.</summary>
    Task<GpuShaderInspection> InspectShaderAsync(
        string resourcePath,
        CancellationToken cancellationToken = default);
}
