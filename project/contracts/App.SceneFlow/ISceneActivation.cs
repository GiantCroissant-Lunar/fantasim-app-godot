namespace FantaSim.App.SceneFlow;

public interface ISceneActivation : IDisposable
{
    string SceneId { get; }

    IServiceProvider Services { get; }
}
