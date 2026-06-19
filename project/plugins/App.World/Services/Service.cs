using FantaSim.App.World.Dto;
using ServiceArchi.Contracts;

namespace FantaSim.App.World.Services;

/// <summary>
/// T3 orchestrator for the App.World service. Implements <see cref="IService"/> by composing
/// the sibling fantasim-world field/catalog/reducer stack at construction and mapping its pure
/// types onto the app-side DTOs defined in <c>FantaSim.App.World.Dto</c>.
/// </summary>
/// <remarks>
/// World-lib types (FantaSim.World.Fields.*, World.TruthStream.*, World.Parameters.*) stay
/// inside this T3 assembly and never leak through the T1 contract surface. The composition is
/// only compiled when <c>UseProjectReferences=true</c> (the local lunar-horse feed does not yet
/// carry FantaSim.World.* packages); the default build path uses a no-op runtime so the solution
/// stays green without the sibling repo wired in.
/// </remarks>
public sealed class Service : IService, IDisposable
{
    private readonly IRegistry _registry;
    private readonly IWorldRuntime _runtime;
    private readonly List<Action<WorldGenerationChangedEvent>> _subscribers = new();
    private readonly object _subscribersGate = new();
    private bool _disposed;

    public Service(IRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _runtime = WorldRuntimeFactory.Create(registry);
    }

    public WorldOverview GetOverviewAsync()
        => _runtime.GetOverview();

    public WorldFieldValues GetFieldValuesAsync(WorldFieldValuesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _runtime.GetFieldValues(request);
    }

    public WorldScalarFieldValues GetScalarFieldValuesAsync(WorldScalarFieldValuesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _runtime.GetScalarFieldValues(request);
    }

    public WorldRenderSnapshot GetRenderSnapshotAsync()
        => _runtime.GetRenderSnapshot();

    public WorldGenerationResult RunGenerationAsync(WorldGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = _runtime.RunGeneration(request);
        EmitGenerationChanged(new WorldGenerationChangedEvent(result.ResultWorldId, "generation", request.GenerationSpec));
        return result;
    }

    public void SubscribeGenerationChanged(Action<WorldGenerationChangedEvent> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (_subscribersGate) _subscribers.Add(callback);
    }

    private void EmitGenerationChanged(WorldGenerationChangedEvent evt)
    {
        Action<WorldGenerationChangedEvent>[] snapshot;
        lock (_subscribersGate) snapshot = _subscribers.ToArray();
        foreach (var cb in snapshot)
        {
            try { cb(evt); } catch { /* subscriber faults are isolated; world runtime stays up */ }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _runtime.Dispose();
        _disposed = true;
    }
}