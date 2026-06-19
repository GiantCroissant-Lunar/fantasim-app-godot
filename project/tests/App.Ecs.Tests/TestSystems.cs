using Arch.Core;
using UnifyECS;

namespace FantaSim.App.Ecs.Tests;

/// <summary>
/// A minimal ECS system that records how many times it was ticked and creates one
/// entity per tick. Implements both the backend-neutral <see cref="IUnifySystem"/> marker
/// and the Arch-specific <see cref="IArchSystem"/> so <c>ArchSystemRunner.Update</c>
/// actually dispatches to <see cref="Execute"/>. Used to make world-update behavior
/// observable through <see cref="Arch.Core.World.EntityCount"/> without exercising field systems.
/// </summary>
internal sealed class CountingEntitySystem : IUnifySystem, IArchSystem
{
    public int Ticks { get; private set; }

    public void Execute(Arch.Core.World world, float deltaTime)
    {
        Ticks++;
        world.Create();
    }
}