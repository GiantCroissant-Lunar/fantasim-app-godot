using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;

namespace FantaSim.App.SceneFlow.Services;

internal sealed class SceneFlowProvider
{
    private readonly IServiceProvider _rootProvider;
    private readonly IRegistry _registry;
    private readonly ILogger _logger;
    private readonly Dictionary<string, (ISceneActivation Activation, SceneSession Session)> _active = new(StringComparer.Ordinal);

    public SceneFlowProvider(IServiceProvider rootProvider, IRegistry registry, ILoggerFactory loggerFactory)
    {
        _rootProvider = rootProvider ?? throw new ArgumentNullException(nameof(rootProvider));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = (loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory))).CreateLogger("App.SceneFlow.Provider");
    }

    public async Task<SceneSession> LoadAsync(SceneRequest request, CancellationToken cancellationToken = default)
    {
        var activator = _registry.GetAll<ISceneActivator>().FirstOrDefault(candidate => candidate.SceneId == request.SceneId);
        if (activator is null)
        {
            var resource = _registry.TryGet<FantaSim.App.Resource.IService>();
            if (resource is not null)
            {
                _logger.LogInformation("Loading scene bundle: {Scene}", request.SceneId);
                await resource.LoadFromDirectoryAsync(request.SceneId, cancellationToken);
                activator = _registry.GetAll<ISceneActivator>().FirstOrDefault(candidate => candidate.SceneId == request.SceneId);
            }

            if (activator is null)
                throw new InvalidOperationException($"No scene activator registered for '{request.SceneId}'.");
        }

        IServiceProvider parent;
        if (request.ParentSceneId is null)
        {
            parent = _rootProvider;
        }
        else if (_active.TryGetValue(request.ParentSceneId, out var parentEntry))
        {
            parent = parentEntry.Activation.Services;
        }
        else
        {
            throw new InvalidOperationException($"Parent scene '{request.ParentSceneId}' is not active.");
        }

        var activation = await activator.ActivateAsync(parent, cancellationToken);
        var session = new SceneSession(request.SceneId, request.ParentSceneId);
        _active[request.SceneId] = (activation, session);
        return session;
    }

    public Task UnloadAsync(string sceneId, CancellationToken cancellationToken = default)
    {
        if (!TryDisposeActivation(sceneId))
            return Task.CompletedTask;

        var resource = _registry.TryGet<FantaSim.App.Resource.IService>();
        return resource?.UnloadAsync(sceneId, cancellationToken) ?? Task.CompletedTask;
    }

    private bool TryDisposeActivation(string sceneId)
    {
        if (!_active.Remove(sceneId, out var entry))
            return false;

        entry.Activation.Dispose();
        return true;
    }
}
