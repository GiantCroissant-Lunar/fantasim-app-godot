using Akka.Actor;
using CrosscutFoundation.Messaging;
using FantaSim.App.Ecs.Actors;
using Microsoft.Extensions.Logging;
using UnifyECS;

namespace FantaSim.App.Ecs.Services;

public sealed class Service : IService, IDisposable
{
    private readonly IActorRef _supervisor;
    private readonly ActorSystem _system;

    public Service(ActorSystem system, ILoggerFactory loggerFactory, IMessageBus? bus = null)
    {
        _system = system;
        _supervisor = system.ActorOf(
            Props.Create(() => new EcsSupervisorActor(bus, loggerFactory)),
            "ecs-supervisor");
    }

    public EcsWorldInfo CreateWorld(EcsWorldSpec spec)
        => _supervisor.Ask<EcsWorldInfo>(new CreateWorld(spec), TimeSpan.FromSeconds(5))
            .GetAwaiter().GetResult();

    public bool DestroyWorld(string worldId)
        => _supervisor.Ask<bool>(new DestroyWorld(worldId), TimeSpan.FromSeconds(5))
            .GetAwaiter().GetResult();

    public EcsWorldInfo GetWorld(string worldId)
        => _supervisor.Ask<EcsWorldInfo>(new GetWorldSnapshot(worldId), TimeSpan.FromSeconds(5))
            .GetAwaiter().GetResult();

    public IReadOnlyList<EcsWorldInfo> ListWorlds()
        => _supervisor.Ask<ListWorldsResult>(new ListWorlds(), TimeSpan.FromSeconds(5))
            .GetAwaiter().GetResult().Worlds;

    public EcsWorldInfo InitializeWorld(string worldId)
    {
        var ack = _supervisor.Ask<WorldInitialized>(
            new InitializeWorld(worldId), TimeSpan.FromSeconds(5))
            .GetAwaiter().GetResult();
        if (ack is null || ack.WorldId != worldId)
            throw new InvalidOperationException($"Unknown ECS world: '{worldId}'.");
        return GetWorld(worldId);
    }

    public void RegisterSystem(string worldId, IUnifySystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        if (string.IsNullOrWhiteSpace(worldId))
            throw new ArgumentNullException(nameof(worldId));
        _supervisor.Tell(new RegisterSystem(worldId, system));
    }

    public void UpdateWorld(string worldId, float dt)
        => _supervisor.Tell(new UpdateWorld(worldId, dt));

    public void UpdateAll(float dt)
        => _supervisor.Tell(new UpdateAll(dt));

    public void Dispose()
        => _supervisor.GracefulStop(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
}
