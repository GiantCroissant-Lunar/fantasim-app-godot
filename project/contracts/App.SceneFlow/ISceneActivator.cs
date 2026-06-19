namespace FantaSim.App.SceneFlow;

public interface ISceneActivator
{
    string SceneId { get; }

    Task<ISceneActivation> ActivateAsync(IServiceProvider parent, CancellationToken cancellationToken = default);
}
