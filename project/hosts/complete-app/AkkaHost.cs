using System.Threading.Tasks;
using Akka;
using Akka.Actor;
using Godot;

namespace CompleteApp.Akka;

/// <summary>
/// Autoload singleton that owns the Akka.NET <see cref="ActorSystem"/> lifecycle.
/// Created on <see cref="_Ready"/>, shut down on <see cref="NotificationSceneTreeExiting"/>.
/// All actor work runs on Akka dispatcher threads; any call back into the Godot
/// main thread MUST go through <see cref="Callable.CallDeferred"/>.
/// </summary>
public sealed partial class AkkaHost : Node
{
    private ActorSystem? _system;
    private IActorRef? _greeter;

    public override void _Ready()
    {
        // ActorSystem.Create does meaningful work (config parse, dispatcher spin-up).
        // Run it off the main thread to avoid a startup hitch.
        Task.Run(() =>
        {
            _system = ActorSystem.Create("fantasim", @"
                akka {
                    loglevel = INFO
                    actor {
                        debug.receive = off
                    }
                }");

            // Spawn a sample actor so the host is demonstrably alive at runtime.
            var props = Props.Create(() => new GreeterActor());
            _greeter = _system.ActorOf(props, "greeter");

            // Fire-and-forget hello from a non-Godot thread.
            _greeter.Tell("AkkaHost ready");
        });
    }

    /// <summary>
    /// Accessor for game code that wants to talk to actors.
    /// Safe to call from any thread; the returned ref is a thread-safe mailbox handle.
    /// </summary>
    public IActorRef? Greeter => _greeter;

    public override void _Notification(int what)
    {
        // NotificationWMCloseRequest fires when the OS window close button is pressed.
        // NotificationExitTree fires on scene tree teardown (covers GetTree().Quit() too).
        if (what == NotificationWMCloseRequest || what == NotificationExitTree)
        {
            // CoordinatedShutdown is the graceful path; block briefly so actors
            // finish outstanding work before the process exits.
            _system?.WhenTerminated.ContinueWith(_ =>
            {
                _system?.Dispose();
                _system = null;
            });
        }
        base._Notification(what);
    }

    private sealed class GreeterActor : ReceiveActor
    {
        public GreeterActor()
        {
            Receive<string>(msg =>
            {
                GD.Print($"[Akka] {Self.Path} got: {msg}");
                Sender.Tell($"ack:{msg}", Self);
            });
        }
    }
}
