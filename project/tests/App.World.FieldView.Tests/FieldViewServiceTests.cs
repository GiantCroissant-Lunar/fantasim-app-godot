using System.Collections.ObjectModel;
using FantaSim.App.World;
using FantaSim.App.World.Dto;
using FantaSim.App.World.FieldView.Services;
using FantaSim.App.World.FieldView.ViewModels;
using R3;
using Xunit;

namespace FantaSim.App.World.FieldView.Tests;

/// <summary>
/// A minimal in-memory <see cref="IService"/> used by the projection tests. It records the
/// subscribed callback so the test can fire <see cref="WorldGenerationChangedEvent"/> at the
/// right time, and serves deterministic <see cref="WorldFieldValues"/>/
/// <see cref="WorldScalarFieldValues"/> payloads so the projection's collection sync is
/// observable without the sibling fantasim-world stack.
/// </summary>
internal sealed class FakeWorldService : IService
{
    public FantaSim.App.World.LayerFilmstripPreviewMap? GetLayerFilmstripPreview(FantaSim.App.World.LayerFilmstripPreviewRequest request, System.Threading.CancellationToken cancellationToken = default) => null;

    private Action<WorldGenerationChangedEvent>? _callback;
    private Dictionary<string, WorldFieldDescriptorDto> _fieldValues = new(0);
    private Dictionary<string, float> _scalarValues = new(0);

    public WorldOverview GetOverviewAsync()
        => new(WorldId: "fake", Name: "Fake", EntityCount: 0, FieldCount: _fieldValues.Count, IsDirty: false);

    public WorldFieldValues GetFieldValuesAsync(WorldFieldValuesRequest request)
        => new(FieldValues: _fieldValues);

    public WorldScalarFieldValues GetScalarFieldValuesAsync(WorldScalarFieldValuesRequest request)
        => new(ScalarValues: _scalarValues);

    public WorldRenderSnapshot GetRenderSnapshotAsync()
        => new(FrameIndex: 0, Entities: Array.Empty<RenderEntityDto>());

    public WorldGenerationProductsView GetGenerationProductsAsync()
        => new(0, Array.Empty<string>(), 0L);

    public PlanetPresentationDocument GetPlanetPresentationAsync()
        => new(
            PlanetId: "fake",
            SourceWorldId: "fake",
            ReferenceTick: 0L,
            Revision: 0,
            Layers: Array.Empty<PlanetPresentationLayer>(),
            RenderEntities: Array.Empty<RenderEntityDto>());

    public PlanetPresentationDocument GetPlanetPresentationAsync(long referenceTick)
        => GetPlanetPresentationAsync();

    public WorldGlobeSnapshot GetGlobeSnapshotAt(long tick)
        => throw new NotSupportedException();

    public byte[] GetGlobeBoundaryCellsAt(long tick)
        => throw new NotSupportedException();

    public IReadOnlyDictionary<int, double> GetContinentalFractionByCellAt(long tick)
        => throw new NotSupportedException();

    public MantleIsosurfaceSet GetMantleIsosurfacesAsync(long tick)
        => MantleIsosurfaceSet.Empty;

    public WorldGenerationResult RunGenerationAsync(WorldGenerationRequest request)
    {
        var evt = new WorldGenerationChangedEvent(request.WorldId, "generation", request.GenerationSpec);
        _callback?.Invoke(evt);
        return new WorldGenerationResult(Success: true, Message: "fake-gen", ResultWorldId: request.WorldId);
    }

    public IDisposable SubscribeGenerationChanged(Action<WorldGenerationChangedEvent> callback)
    {
        _callback = callback;
        return new CallbackSubscription(this, callback);
    }

    /// <summary>Test hook: seed the field/scalar payloads the service returns on the next read.</summary>
    public void Seed(
        IReadOnlyDictionary<string, WorldFieldDescriptorDto> fieldValues,
        IReadOnlyDictionary<string, float> scalarValues)
    {
        _fieldValues = new Dictionary<string, WorldFieldDescriptorDto>(fieldValues);
        _scalarValues = new Dictionary<string, float>(scalarValues);
    }

    /// <summary>Test hook: fire a generation-changed event to all subscribers.</summary>
    public void FireGenerationChanged(WorldGenerationChangedEvent evt)
        => _callback?.Invoke(evt);

    private sealed class CallbackSubscription : IDisposable
    {
        private readonly FakeWorldService _owner;
        private readonly Action<WorldGenerationChangedEvent> _callback;
        private bool _disposed;

        public CallbackSubscription(FakeWorldService owner, Action<WorldGenerationChangedEvent> callback)
        {
            _owner = owner;
            _callback = callback;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            if (ReferenceEquals(_owner._callback, _callback))
                _owner._callback = null;
        }
    }
}

public class FieldViewServiceTests
{
    private static readonly WorldFieldDescriptorDto ElevationDescriptor =
        new(Unit: "m", Kind: "Continuous", Reducer: "world.fields.weighted-average");

    private static readonly WorldFieldDescriptorDto TemperatureDescriptor =
        new(Unit: "K", Kind: "Continuous", Reducer: "world.fields.weighted-average");

    // ---------------------------------------------------------------------
    // Behavior 1: After construction, the observable collection count matches
    // the seeded fields the world service reports.
    // ---------------------------------------------------------------------
    [Fact]
    public void Collection_count_matches_seeded_fields_after_construction()
    {
        // Given a fake world service seeded with two fields
        var world = new FakeWorldService();
        world.Seed(
            fieldValues: new Dictionary<string, WorldFieldDescriptorDto>
            {
                ["app.elevation-m"] = ElevationDescriptor,
                ["app.temperature-k"] = TemperatureDescriptor,
            },
            scalarValues: new Dictionary<string, float>
            {
                ["app.elevation-m"] = 12.5f,
            });

        // When a projection is created over both field ids
        using var projection = new FieldViewService(
            world,
            fieldIds: new[] { "app.elevation-m", "app.temperature-k" },
            scalarFieldIds: new[] { "app.elevation-m" });

        // Then the collection has one view model per requested field id, with the scalar
        // populated for the scalar-bearing field and null for the non-scalar one
        Assert.Equal(2, projection.Fields.Count);
        var elev = Assert.Single(projection.Fields, vm => vm.FieldId == "app.elevation-m");
        Assert.Equal(ElevationDescriptor, elev.Value);
        Assert.Equal(12.5f, elev.Scalar);
        var temp = Assert.Single(projection.Fields, vm => vm.FieldId == "app.temperature-k");
        Assert.Equal(TemperatureDescriptor, temp.Value);
        Assert.Null(temp.Scalar);
    }

    // ---------------------------------------------------------------------
    // Behavior 2: Subscribed R3 observable emits after a generation change
    // fires on the world service. This is the plan's acceptance criterion:
    // "subscribed observable emits after generation change."
    // ---------------------------------------------------------------------
    [Fact]
    public void GenerationChanged_observable_emits_after_generation_change()
    {
        // Given a projection with one subscribed observer
        var world = new FakeWorldService();
        world.Seed(
            fieldValues: new Dictionary<string, WorldFieldDescriptorDto>
            {
                ["app.elevation-m"] = ElevationDescriptor,
            },
            scalarValues: new Dictionary<string, float> { ["app.elevation-m"] = 0f });
        using var projection = new FieldViewService(
            world,
            fieldIds: new[] { "app.elevation-m" },
            scalarFieldIds: new[] { "app.elevation-m" });

        var emitted = new List<WorldGenerationChangedEvent>();
        var sub = projection.GenerationChanged.Subscribe(e => emitted.Add(e));
        try
        {
            Assert.Empty(emitted);

            // When the world service fires a generation change
            world.FireGenerationChanged(new WorldGenerationChangedEvent("w1", "generation", "spec-a"));

            // Then the observer received exactly that event
            var evt = Assert.Single(emitted);
            Assert.Equal("w1", evt.WorldId);
            Assert.Equal("generation", evt.ChangeType);
            Assert.Equal("spec-a", evt.Detail);
        }
        finally
        {
            sub.Dispose();
        }
    }

    // ---------------------------------------------------------------------
    // Behavior 3: After a generation change, the collection refreshes from
    // the latest world DTOs. This is the plan's acceptance criterion:
    // "collection count matches seeded fields."
    // ---------------------------------------------------------------------
    [Fact]
    public void Collection_refreshes_to_latest_values_after_generation_change()
    {
        // Given a projection seeded with one field at value 1.0
        var world = new FakeWorldService();
        world.Seed(
            fieldValues: new Dictionary<string, WorldFieldDescriptorDto>
            {
                ["app.elevation-m"] = ElevationDescriptor,
            },
            scalarValues: new Dictionary<string, float> { ["app.elevation-m"] = 1.0f });
        using var projection = new FieldViewService(
            world,
            fieldIds: new[] { "app.elevation-m" },
            scalarFieldIds: new[] { "app.elevation-m" });

        var firstVm = Assert.Single(projection.Fields);
        Assert.Equal(1.0f, firstVm.Scalar);

        // When the world service reports a new scalar and fires a generation change
        world.Seed(
            fieldValues: new Dictionary<string, WorldFieldDescriptorDto>
            {
                ["app.elevation-m"] = ElevationDescriptor,
            },
            scalarValues: new Dictionary<string, float> { ["app.elevation-m"] = 42.0f });
        world.FireGenerationChanged(new WorldGenerationChangedEvent("w1", "generation", "spec-b"));

        // Then the collection still has one entry, but its scalar value was refreshed
        var refreshed = Assert.Single(projection.Fields);
        Assert.Equal("app.elevation-m", refreshed.FieldId);
        Assert.Equal(42.0f, refreshed.Scalar);
    }

    // ---------------------------------------------------------------------
    // Behavior 4: After a generation change, fields the world service stops
    // reporting are kept in the collection (the projection is configured with
    // a fixed field-id set) but their Value/Scalar go null, while fields that
    // are still reported keep their latest values.
    // ---------------------------------------------------------------------
    [Fact]
    public void Collection_keeps_configured_fields_but_clears_unreported_values_after_change()
    {
        // Given a projection configured with two field ids, both seeded
        var world = new FakeWorldService();
        world.Seed(
            fieldValues: new Dictionary<string, WorldFieldDescriptorDto>
            {
                ["app.elevation-m"] = ElevationDescriptor,
                ["app.temperature-k"] = TemperatureDescriptor,
            },
            scalarValues: new Dictionary<string, float>
            {
                ["app.elevation-m"] = 10f,
                ["app.temperature-k"] = 300f,
            });
        using var projection = new FieldViewService(
            world,
            fieldIds: new[] { "app.elevation-m", "app.temperature-k" },
            scalarFieldIds: new[] { "app.elevation-m", "app.temperature-k" });
        Assert.Equal(2, projection.Fields.Count);

        // When the world service stops reporting temperature-k and fires a change
        world.Seed(
            fieldValues: new Dictionary<string, WorldFieldDescriptorDto>
            {
                ["app.elevation-m"] = ElevationDescriptor,
            },
            scalarValues: new Dictionary<string, float> { ["app.elevation-m"] = 99f });
        world.FireGenerationChanged(new WorldGenerationChangedEvent("w1", "generation", "spec-c"));

        // Then the collection still has both configured fields; elevation refreshed, temperature cleared
        Assert.Equal(2, projection.Fields.Count);
        var elev = Assert.Single(projection.Fields, vm => vm.FieldId == "app.elevation-m");
        Assert.Equal(99f, elev.Scalar);
        var temp = Assert.Single(projection.Fields, vm => vm.FieldId == "app.temperature-k");
        Assert.Null(temp.Value);
        Assert.Null(temp.Scalar);
    }
}
