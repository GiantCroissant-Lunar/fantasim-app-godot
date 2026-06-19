using System;
using FantaSim.App.SceneFlow;                   // ISceneActivation
using Microsoft.Extensions.DependencyInjection; // ServiceProvider

namespace FantaSim.App.Stage;

/// <summary>The App.Stage tier's live activation: owns its child scope provider; disposing tears it down.</summary>
internal sealed class StageActivation : ISceneActivation
{
    private readonly ServiceProvider _provider;

    public StageActivation(string sceneId, ServiceProvider provider)
    {
        SceneId = sceneId;
        _provider = provider;
    }

    public string SceneId { get; }

    public IServiceProvider Services => _provider;

    public void Dispose() => _provider.Dispose();
}
