using Akka.Actor;
using CrosscutFoundation.Messaging;
using Microsoft.Extensions.Logging;

namespace FantaSim.App.Ecs.Actors;

internal sealed class EcsSupervisorActor : ReceiveActor
{
    private readonly Dictionary<string, IActorRef> _worldActors = new();
    private readonly IMessageBus? _bus;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _log;

    public EcsSupervisorActor(IMessageBus? bus, ILoggerFactory loggerFactory)
    {
        _bus = bus;
        _loggerFactory = loggerFactory;
        _log = loggerFactory.CreateLogger("EcsSupervisorActor");

        Receive<CreateWorld>(m =>
        {
            if (_worldActors.ContainsKey(m.Spec.WorldId))
            {
                _worldActors[m.Spec.WorldId].Tell(new GetWorldSnapshot(m.Spec.WorldId));
                return;
            }
            var child = Context.ActorOf(
                Props.Create(() => new EcsWorldActor(m.Spec, _loggerFactory)),
                m.Spec.WorldId);
            _worldActors[m.Spec.WorldId] = child;
            _log.LogInformation("Created ECS world: {WorldId}", m.Spec.WorldId);
            child.Tell(new GetWorldSnapshot(m.Spec.WorldId));
        });

        Receive<DestroyWorld>(m =>
        {
            if (_worldActors.TryGetValue(m.WorldId, out var child))
                child.Forward(m);
            else
                Sender.Tell(false);
        });

        Receive<UpdateWorld>(m =>
        {
            if (_worldActors.TryGetValue(m.WorldId, out var child))
                child.Tell(m);
        });

        Receive<UpdateAll>(m =>
        {
            foreach (var child in _worldActors.Values)
                child.Tell(new UpdateWorld(child.Path.Name, m.DeltaTime));
        });

        Receive<GetWorldSnapshot>(m =>
        {
            if (_worldActors.TryGetValue(m.WorldId, out var child))
                child.Forward(m);
        });

        Receive<ListWorlds>(_ =>
        {
            var snapshots = _worldActors.Values
                .Select(c => c.Ask<EcsWorldInfo>(new GetWorldSnapshot(c.Path.Name), TimeSpan.FromSeconds(2)))
                .ToArray();
            Task.WhenAll(snapshots)
                .ContinueWith(t => new ListWorldsResult(t.Result.ToList()))
                .PipeTo(Sender);
        });
    }

    protected override SupervisorStrategy SupervisorStrategy()
        => new OneForOneStrategy(3, TimeSpan.FromSeconds(30),
            ex => ex is ObjectDisposedException ? Directive.Restart : Directive.Escalate);
}
