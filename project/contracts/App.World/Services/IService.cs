using ServiceArchi.Contracts;
using ServiceArchi.Contracts.Attributes;
using FantaSim.App.World.Dto;

namespace FantaSim.App.World;

[ServiceContract]
[SelectionStrategy(SelectionMode.HighestPriority)]
public interface IService
{
    WorldOverview GetOverviewAsync();
    WorldFieldValues GetFieldValuesAsync(WorldFieldValuesRequest request);
    WorldScalarFieldValues GetScalarFieldValuesAsync(WorldScalarFieldValuesRequest request);
    WorldRenderSnapshot GetRenderSnapshotAsync();
    WorldGenerationResult RunGenerationAsync(WorldGenerationRequest request);
    void SubscribeGenerationChanged(Action<WorldGenerationChangedEvent> callback);
}
