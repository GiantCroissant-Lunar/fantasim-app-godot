using System.Diagnostics;
using System.Threading.Tasks;
using Akka.Actor;
using CompleteApp.Akka;
using Godot;

namespace CompleteApp;

public sealed partial class Main : Node
{
    public override async void _Ready()
    {
        GD.Print("[Main] _Ready: waiting for AkkaHost greeter");

        // AkkaHost spins up the ActorSystem off-thread; poll briefly until ready.
        var akkaHost = GetNode<AkkaHost>("/root/AkkaHost");
        IActorRef? greeter = null;
        for (var i = 0; i < 50 && greeter == null; i++)
        {
            greeter = akkaHost.Greeter;
            if (greeter == null) await Task.Delay(100);
        }

        if (greeter == null)
        {
            GD.PushError("[Main] AkkaHost.Greeter never came online");
            GetTree().Quit(1);
            return;
        }

        GD.Print("[Main] got greeter ref, sending messages");

        // Ask + await pattern: demonstrates request/response across threads.
        var reply1 = await greeter.Ask<string>("ping-1");
        GD.Print($"[Main] reply1: {reply1}");

        var reply2 = await greeter.Ask<string>("ping-2");
        GD.Print($"[Main] reply2: {reply2}");

        // Fire-and-forget Tell.
        greeter.Tell("fire-and-forget");

        // Give the actor a beat to flush, then quit.
        await ToSignal(GetTree().CreateTimer(0.5), "timeout");
        GD.Print("[Main] verification complete, quitting");
        GetTree().Quit(0);
    }
}
