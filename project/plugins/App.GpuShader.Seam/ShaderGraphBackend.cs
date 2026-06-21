using System;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.GpuShader.Providers;
using Godot;
using Microsoft.Extensions.Logging;

namespace FantaSim.App.GpuShader.Seam;

/// <summary>
/// Resident Godot shader seam. Loads Shader resources (including bundle PCK content) and reports
/// DTO descriptions; Godot Shader/ShaderMaterial objects never leak into T1/T3 or collectible bundles.
/// </summary>
public sealed class ShaderGraphBackend : IShaderGraphBackend
{
    private readonly ILogger _log;
    private bool _disposed;

    public ShaderGraphBackend(ILoggerFactory loggerFactory)
    {
        if (loggerFactory is null) throw new ArgumentNullException(nameof(loggerFactory));
        _log = loggerFactory.CreateLogger("App.GpuShader.ShaderGraphBackend");
    }

    public Task<GpuShaderInspection> InspectShaderAsync(
        string resourcePath,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ShaderGraphBackend));
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // CacheMode.Replace so a restaged bundle PCK is re-read instead of served stale.
            var shader = ResourceLoader.Load<Shader>(resourcePath, "", ResourceLoader.CacheMode.Replace);
            if (shader is null)
                return Task.FromResult(new GpuShaderInspection(resourcePath, Found: false, Error: "Shader resource not found or not a Shader."));

            var mode = shader.GetMode();
            var kind = mode switch
            {
                Shader.Mode.Spatial => "spatial",
                Shader.Mode.CanvasItem => "canvas_item",
                Shader.Mode.Particles => "particles",
                Shader.Mode.Sky => "sky",
                Shader.Mode.Fog => "fog",
                _ => mode.ToString(),
            };
            var sourceLength = shader.Code?.Length ?? 0;
            return Task.FromResult(new GpuShaderInspection(resourcePath, Found: true, kind, sourceLength));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Shader inspection failed for {Path}.", resourcePath);
            return Task.FromResult(new GpuShaderInspection(resourcePath, Found: false, Error: ex.Message));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _log.LogDebug("GPU shader graph backend disposed.");
    }
}
