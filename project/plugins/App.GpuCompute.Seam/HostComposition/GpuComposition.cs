using Godot;
using ServiceArchi.Contracts;
using FantaSim.App.Common;

namespace FantaSim.App.GpuCompute.Seam;

public static class GpuComposition
{
    public static FantaSim.App.GpuCompute.Services.Service ComposeGpu(HostCompositionContext ctx)
    {
        var registry = ctx.Registry;
        var loggerFactory = ctx.LoggerFactory;

        var backend = new FantaSim.App.GpuCompute.Seam.GodotComputeBackend(loggerFactory);
        var service = new FantaSim.App.GpuCompute.Services.Service(backend, loggerFactory);
        registry.Register<FantaSim.App.GpuCompute.IService>(
            service,
            new ServiceRegistration { Tags = new[] { "gpu-compute", "gpu", "compute" }, Description = "GPU compute shader service" });
        GD.Print("[Host] registered: Gpu (compute service, resident RenderingDevice seam)");
        return service;
    }
}
