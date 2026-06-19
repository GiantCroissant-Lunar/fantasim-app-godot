using Akka.Actor;
using Microsoft.Extensions.Logging;
using UnifyECS;

namespace FantaSim.App.Ecs.Actors;

internal sealed class EcsWorldActor : ReceiveActor
{
    private readonly EcsWorldSpec _spec;
    private readonly ArchWorld _world;
    private readonly ArchSystemRunner _runner;
    private readonly ILogger _log;
    private bool _initialized;

    public EcsWorldActor(EcsWorldSpec spec, ILoggerFactory loggerFactory)
    {
        _spec = spec;
        _log = loggerFactory.CreateLogger($"EcsWorldActor[{spec.WorldId}]");

        _world = (ArchWorld)WorldFactory.Create(EcsBackend.Arch, new WorldConfig
        {
            Name = spec.DisplayName ?? spec.WorldId,
            InitialEntityCapacity = spec.InitialEntityCapacity,
            DebugMode = spec.DebugMode,
        });
        _runner = new ArchSystemRunner(_world);

        Receive<RegisterSystem>(m =>
        {
            if (_initialized) throw new InvalidOperationException("Already initialized.");
            _runner.Register(m.System);
        });

        Receive<InitializeWorld>(_ =>
        {
            if (_initialized) return;
            _runner.Initialize();
            _initialized = true;
            Sender.Tell(new WorldInitialized(_spec.WorldId));
        });

        Receive<UpdateWorld>(m => _runner.Update(m.DeltaTime));

        Receive<GetWorldSnapshot>(_ => Sender.Tell(MakeSnapshot()));

        Receive<DestroyWorld>(_ =>
        {
            _runner.Dispose();
            _world.Dispose();
            Sender.Tell(true);
            Context.Stop(Self);
        });
    }

    private EcsWorldInfo MakeSnapshot() => new(
        _spec.WorldId, _spec.Backend,
        _spec.DisplayName ?? _spec.WorldId,
        _world.EntityCount, 0, _initialized);

    protected override void PostStop()
    {
        _runner.Dispose();
        _world.Dispose();
        _log.LogInformation("ECS world actor stopped: {WorldId}", _spec.WorldId);
    }
}
