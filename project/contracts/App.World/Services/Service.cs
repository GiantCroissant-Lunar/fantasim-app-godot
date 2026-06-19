using ServiceArchi.Contracts;
using ServiceArchi.Contracts.Attributes;

namespace FantaSim.App.World.Services.Proxy;

[RealizeService(typeof(IService))]
public sealed partial class Service
{
    private readonly IRegistry _registry;
    public Service(IRegistry registry)
        => _registry = registry ?? throw new ArgumentNullException(nameof(registry));
}
