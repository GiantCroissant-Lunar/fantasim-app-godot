using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;

namespace FantaSim.App.SceneFlow.Services;

public sealed class Service : IService
{
    private readonly SceneFlowProvider _provider;
    private readonly ILogger _logger;
    private readonly List<SceneSession> _active = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Service(IServiceProvider rootProvider, IRegistry registry, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(rootProvider);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _provider = new SceneFlowProvider(rootProvider, registry, loggerFactory);
        _logger = loggerFactory.CreateLogger("App.SceneFlow.Service");
    }

    public IReadOnlyList<SceneSession> ActiveScenes => _active;

    public async Task<SceneSession> EnterAsync(SceneRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = _active.FirstOrDefault(scene => scene.SceneId == request.SceneId);
            if (existing is not null)
            {
                if (!string.Equals(existing.ParentSceneId, request.ParentSceneId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Scene '{request.SceneId}' is already active under '{existing.ParentSceneId ?? "<root>"}'.");
                }

                return existing;
            }

            var session = await _provider.LoadAsync(request, cancellationToken).ConfigureAwait(false);
            _active.Add(session);
            _logger.LogInformation("Scene entered: {Scene}", session.SceneId);
            return session;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ExitAsync(string sceneId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneId);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ExitCoreAsync(sceneId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ExitCoreAsync(string sceneId, CancellationToken cancellationToken)
    {
        var children = _active.Where(scene => scene.ParentSceneId == sceneId).Select(scene => scene.SceneId).ToArray();
        foreach (var child in children)
            await ExitCoreAsync(child, cancellationToken).ConfigureAwait(false);

        await _provider.UnloadAsync(sceneId, cancellationToken).ConfigureAwait(false);
        _active.RemoveAll(scene => scene.SceneId == sceneId);
        _logger.LogInformation("Scene exited: {Scene}", sceneId);
    }
}
