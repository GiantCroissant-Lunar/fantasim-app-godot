using System;
using ServiceArchi.Contracts;
using ServiceArchi.Contracts.Attributes;

namespace FantaSim.App.Camera.Services.Proxy;

// Service-locator proxy for IService (ServiceArchi Tier 2). ServiceArchi.SourceGen generates the
// forwarding partial that implements IService by resolving the active T3 from the registry.
// Lives alongside the contract (T1) per this repo's layout.
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
