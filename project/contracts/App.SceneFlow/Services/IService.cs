using ServiceArchi.Contracts;
using ServiceArchi.Contracts.Attributes;

namespace FantaSim.App.SceneFlow;

[ServiceContract]
public interface IService
{
    Task<SceneSession> EnterAsync(SceneRequest request, CancellationToken cancellationToken = default);

    Task ExitAsync(string sceneId, CancellationToken cancellationToken = default);

    IReadOnlyList<SceneSession> ActiveScenes { get; }
}
