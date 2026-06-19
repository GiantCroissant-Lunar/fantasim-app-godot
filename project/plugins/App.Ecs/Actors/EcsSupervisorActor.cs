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
            if (_worldActors.TryGetValue(m.Spec.WorldId, out var existing))
            {
                // Forward so the world actor's GetWorldSnapshot reply returns to the original
                // sender (the IService.CreateWorld caller), not to this supervisor.
                existing.Forward(new GetWorldSnapshot(m.Spec.WorldId));
                return;
            }
            var child = Context.ActorOf(
                Props.Create(() => new EcsWorldActor(m.Spec, _loggerFactory)),
                m.Spec.WorldId);
            _worldActors[m.Spec.WorldId] = child;
            Context.Watch(child);
            _log.LogInformation("Created ECS world: {WorldId}", m.Spec.WorldId);
            child.Forward(new GetWorldSnapshot(m.Spec.WorldId));
        });

        Receive<DestroyWorld>(m =>
        {
            if (_worldActors.TryGetValue(m.WorldId, out var child))
            {
                // Drop the mapping before forwarding so ListWorlds/UpdateAll never
                // route to a child that is stopping. Terminated is only a backup.
                _worldActors.Remove(m.WorldId);
                child.Forward(m);
            }
            else
                Sender.Tell(false);
        });

        // Remove dead world actors from the map so ListWorlds/UpdateAll no longer
        // route to a stopped child (which would silently drop or time out).
        Receive<Terminated>(m =>
        {
            var id = m.ActorRef.Path.Name;
            if (_worldActors.TryGetValue(id, out var child) && child.Equals(m.ActorRef))
                _worldActors.Remove(id);
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
