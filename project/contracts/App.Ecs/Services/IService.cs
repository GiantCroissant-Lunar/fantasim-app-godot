using ServiceArchi.Contracts;
using ServiceArchi.Contracts.Attributes;

namespace FantaSim.App.Ecs;

[ServiceContract]
public interface IService
{
    EcsWorldInfo CreateWorld(EcsWorldSpec spec);
    bool DestroyWorld(string worldId);
    EcsWorldInfo GetWorld(string worldId);
    IReadOnlyList<EcsWorldInfo> ListWorlds();
    EcsWorldInfo InitializeWorld(string worldId);
    void UpdateWorld(string worldId, float deltaTime);
    void UpdateAll(float deltaTime);
}
