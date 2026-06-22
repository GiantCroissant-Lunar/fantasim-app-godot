using System;
using FantaSim.App.SceneFlow;                   // ISceneActivation
using Microsoft.Extensions.DependencyInjection; // ServiceProvider

namespace FantaSim.App.Timeline;

/// <summary>The App.Timeline hud-view tier's live activation: owns its child scope; disposing tears it down.</summary>
internal sealed class TimelineActivation : ISceneActivation
{
    private readonly ServiceProvider _provider;

    public TimelineActivation(string sceneId, ServiceProvider provider)
    {
        SceneId = sceneId;
        _provider = provider;
    }

    public string SceneId { get; }

    public IServiceProvider Services => _provider;

    public void Dispose() => _provider.Dispose();
}
