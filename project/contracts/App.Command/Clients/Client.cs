using ServiceArchi.Contracts;
using ServiceArchi.Contracts.Attributes;

namespace FantaSim.App.Command.Clients.Proxy;

[RealizeService(typeof(IClient))]
public sealed partial class Client
{
    private readonly IRegistry _registry;

    public Client(IRegistry registry)
        => _registry = registry ?? throw new ArgumentNullException(nameof(registry));
}