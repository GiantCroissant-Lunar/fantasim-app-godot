using UnifyECS;

namespace FantaSim.App.Ecs.Actors;

internal sealed record CreateWorld(EcsWorldSpec Spec);
internal sealed record DestroyWorld(string WorldId);
internal sealed record UpdateWorld(string WorldId, float DeltaTime);
internal sealed record UpdateAll(float DeltaTime);
internal sealed record RegisterSystem(string WorldId, IUnifySystem System);
internal sealed record InitializeWorld(string WorldId);
internal sealed record GetWorldSnapshot(string WorldId);
internal sealed record ListWorlds;
internal sealed record ListWorldsResult(IReadOnlyList<EcsWorldInfo> Worlds);
internal sealed record WorldInitialized(string WorldId);
