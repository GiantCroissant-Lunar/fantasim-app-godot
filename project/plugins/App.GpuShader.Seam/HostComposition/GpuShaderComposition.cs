using Godot;
using ServiceArchi.Contracts;
using FantaSim.App.Common;

namespace FantaSim.App.GpuShader.Seam;

public static class GpuShaderComposition
{
    public static FantaSim.App.GpuShader.Services.Service ComposeGpuShader(HostCompositionContext ctx)
    {
        var registry = ctx.Registry;
        var loggerFactory = ctx.LoggerFactory;

        var backend = new FantaSim.App.GpuShader.Seam.ShaderGraphBackend(loggerFactory);
        var service = new FantaSim.App.GpuShader.Services.Service(backend, loggerFactory);
        registry.Register<FantaSim.App.GpuShader.IService>(
            service,
            new ServiceRegistration { Tags = new[] { "gpu-shader", "gpu", "shader" }, Description = "GPU shader-graph authoring service" });
        GD.Print("[Host] registered: GpuShader (authoring service, resident Godot Shader seam)");
        return service;
    }
}
