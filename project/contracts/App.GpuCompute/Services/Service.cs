using System;
using ServiceArchi.Contracts;
using ServiceArchi.Contracts.Attributes;

namespace FantaSim.App.GpuCompute.Services.Proxy;

// Service-locator proxy for IService. ServiceArchi.SourceGen generates the forwarding partial that
// resolves the active T3 implementation from the registry.
[RealizeService(typeof(IService))]
[SelectionStrategy(SelectionMode.HighestPriority)]
public sealed partial class Service
{
    private readonly IRegistry _registry;

    public Service(IRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }
}
