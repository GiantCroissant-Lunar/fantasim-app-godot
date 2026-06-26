using ServiceArchi.Contracts;
using ServiceArchi.Contracts.Attributes;
using FantaSim.App.World.Dto;

namespace FantaSim.App.World;

[ServiceContract]
public interface IService
{
    WorldOverview GetOverviewAsync();
    WorldFieldValues GetFieldValuesAsync(WorldFieldValuesRequest request);
    WorldScalarFieldValues GetScalarFieldValuesAsync(WorldScalarFieldValuesRequest request);
    WorldRenderSnapshot GetRenderSnapshotAsync();
    WorldGenerationProductsView GetGenerationProductsAsync();
    PlanetPresentationDocument GetPlanetPresentationAsync();
    WorldGenerationResult RunGenerationAsync(WorldGenerationRequest request);
    IDisposable SubscribeGenerationChanged(Action<WorldGenerationChangedEvent> callback);
}
