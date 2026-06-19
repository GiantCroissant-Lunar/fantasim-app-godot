using System.Collections.ObjectModel;
using FantaSim.App.World;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Projection.ViewModels;
using R3;

namespace FantaSim.App.World.Projection.Services;

/// <summary>
/// T3 field projection. Attaches to an <see cref="FantaSim.App.World.IService"/> app-world T1
/// service, listens for <see cref="WorldGenerationChangedEvent"/> notifications, re-emits them on
/// an R3 <see cref="Observable{T}"/>, and maintains an
/// <see cref="ObservableCollection{T}"/> of <see cref="FieldValueViewModel"/> built from the
/// world T1 DTOs (<c>WorldFieldValues</c> / <c>WorldScalarFieldValues</c>).
/// </summary>
/// <remarks>
/// Pure app-side: no Godot, no fantasim-world types, no Akka, no iii. The only inputs are the
/// app-side DTOs returned by the T1 contract. The projection owns the lifetime of its R3 subject
/// and the binding between world changes and the collection; it never calls into world-lib.
/// </remarks>
public sealed class FieldProjectionService : IDisposable
{
    private readonly IService _worldService;
    private readonly IReadOnlyList<string> _fieldIds;
    private readonly IReadOnlyList<string> _scalarFieldIds;
    private readonly Subject<WorldGenerationChangedEvent> _generationChanged = new();
    private readonly object _gate = new();
    private bool _disposed;

    /// <summary>
    /// Creates a projection over the given field ids. The projection seeds the collection from
    /// the current world state immediately, then refreshes on every generation change.
    /// </summary>
    public FieldProjectionService(
        IService worldService,
        IReadOnlyList<string> fieldIds,
        IReadOnlyList<string> scalarFieldIds)
    {
        _worldService = worldService ?? throw new ArgumentNullException(nameof(worldService));
        _fieldIds = fieldIds ?? throw new ArgumentNullException(nameof(fieldIds));
        _scalarFieldIds = scalarFieldIds ?? throw new ArgumentNullException(nameof(scalarFieldIds));

        Fields = new ObservableCollection<FieldValueViewModel>();
        GenerationChanged = _generationChanged;

        // Attach the world change callback once. The T1 contract exposes only add-callback
        // (no unsubscribe), so Dispose guards later invocations with the _disposed flag.
        _worldService.SubscribeGenerationChanged(OnWorldGenerationChanged);

        // Seed the collection from the current world state so consumers bind to a non-empty view.
        Refresh(projectionTick: 0);
    }

    /// <summary>Bindable collection of projected field view models.</summary>
    public ObservableCollection<FieldValueViewModel> Fields { get; }

    /// <summary>R3 observable stream of generation-changed events from the world T1 service.</summary>
    public Observable<WorldGenerationChangedEvent> GenerationChanged { get; }

    private void OnWorldGenerationChanged(WorldGenerationChangedEvent evt)
    {
        if (_disposed) return;
        // Re-emit on the R3 subject, then refresh the collection. Subscriber faults on the
        // subject are isolated by R3; collection refresh faults would leave the projection in
        // an inconsistent state, so we let them propagate to the world service's caller
        // (the T3 service isolates subscriber faults already — see Service.cs).
        _generationChanged.OnNext(evt);
        Refresh(projectionTick: 0);
    }

    private void Refresh(long projectionTick)
    {
        // Pull the latest DTOs from the T1 contract and reconcile the observable collection.
        // We replace entries in-place by index to keep bound UI controls stable; unknown ids
        // that appear are appended, ids that vanish are removed.
        var fieldValues = SafeGetFieldValues();
        var scalarValues = SafeGetScalarFieldValues();

        lock (_gate)
        {
            if (_disposed) return;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < Fields.Count;)
            {
                var current = Fields[i];
                if (!_fieldIds.Contains(current.FieldId))
                {
                    Fields.RemoveAt(i);
                    continue;
                }

                Fields[i] = BuildViewModel(current.FieldId, fieldValues, scalarValues, projectionTick);
                seen.Add(current.FieldId);
                i++;
            }

            foreach (var id in _fieldIds)
            {
                if (seen.Contains(id)) continue;
                Fields.Add(BuildViewModel(id, fieldValues, scalarValues, projectionTick));
            }
        }
    }

    private WorldFieldValues SafeGetFieldValues()
    {
        try
        {
            return _worldService.GetFieldValuesAsync(new WorldFieldValuesRequest(
                WorldId: string.Empty, FieldIds: _fieldIds));
        }
        catch
        {
            return new WorldFieldValues(new Dictionary<string, object>(0));
        }
    }

    private WorldScalarFieldValues SafeGetScalarFieldValues()
    {
        try
        {
            return _worldService.GetScalarFieldValuesAsync(new WorldScalarFieldValuesRequest(
                WorldId: string.Empty, ScalarFieldIds: _scalarFieldIds));
        }
        catch
        {
            return new WorldScalarFieldValues(new Dictionary<string, float>(0));
        }
    }

    private static FieldValueViewModel BuildViewModel(
        string fieldId,
        WorldFieldValues fieldValues,
        WorldScalarFieldValues scalarValues,
        long projectionTick)
    {
        fieldValues.FieldValues.TryGetValue(fieldId, out var raw);
        float? scalar = null;
        if (scalarValues.ScalarValues.TryGetValue(fieldId, out var s))
            scalar = s;

        return new FieldValueViewModel
        {
            FieldId = fieldId,
            Value = raw,
            Scalar = scalar,
            ProjectedTick = projectionTick
        };
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _generationChanged.Dispose();
    }
}