using System;
using Microsoft.Extensions.DependencyInjection;

namespace FantaSim.App.SceneFlow;

/// <summary>
/// A scene's live activation: owns its child scope provider; disposing tears it down (the SceneFlow
/// service enforces child-before-parent ordering). Created by <see cref="SceneActivatorBase"/>; the
/// per-tier StageActivation/AssistActivation/TimelineActivation classes collapsed into this one.
/// </summary>
internal sealed class SceneActivation : ISceneActivation
{
    private readonly ServiceProvider _provider;

    public SceneActivation(string sceneId, ServiceProvider provider)
    {
        SceneId = sceneId;
        _provider = provider;
    }

    public string SceneId { get; }

    public IServiceProvider Services => _provider;

    public void Dispose() => _provider.Dispose();
}
