#if USE_PROJECT_REFERENCES
using FantaSim.App.World.Dto;
using FantaSim.World.Fields;
using FantaSim.World.Fields.Core;
using FantaSim.World.TruthStream;
using FantaSim.World.TruthStream.Core;
using ServiceArchi.Contracts;
using TimeDete.Time.Primitives;

namespace FantaSim.App.World.Services;

/// <summary>
/// Real <see cref="IWorldRuntime"/> composing the sibling fantasim-world stack at construction:
/// <list type="bullet">
/// <item><see cref="CompositeFieldCatalog"/> + <see cref="FieldReducerRegistry"/> +
///       <see cref="DefaultReducers"/> + <see cref="CatalogValidator"/> validate the field
///       system at startup (duplicate FieldId or an unregistered reducer fails fast).</item>
/// <item><see cref="InMemoryTruthEventStore"/> backs generation appends so
///       <see cref="RunGeneration"/> exercises the truth-stream primitives.</item>
/// </list>
/// World-lib types never cross this boundary; every method maps onto the app-side DTOs.
/// </summary>
internal sealed class WorldRuntime : IWorldRuntime
{
    private readonly IFieldCatalog _catalog;
    private readonly IFieldReducerRegistry _reducers;
    private readonly ITruthEventStore _truthStore;
    private readonly TruthStreamIdentity _streamId;
    private readonly object _truthCommitGate = new();

    // Composed at construction. The descriptor seed is intentionally minimal here (one
    // continuous field driven by the built-in WeightedAverage reducer); Task 7 will expand the
    // field set via per-sphere catalog modules. CatalogValidator.Validate throws on a duplicate
    // FieldId or a reducer/kind mismatch — that is the startup composition contract.
    public WorldRuntime(IRegistry registry)
        : this(registry, descriptors: null)
    {
    }

    // Test hook: lets App.World.Tests inject explicit descriptor/store dependencies to prove
    // startup validation and app-side truth-stream coordination without touching fantasim-world.
    internal WorldRuntime(
        IRegistry registry,
        IReadOnlyList<FieldDescriptor>? descriptors,
        ITruthEventStore? truthStore = null)
    {
        _reducers = new FieldReducerRegistry();
        DefaultReducers.RegisterAll(_reducers);

        var seed = descriptors ??
            (IReadOnlyList<FieldDescriptor>)new[]
            {
                new FieldDescriptor(
                    Id: new FieldId("app.elevation-m"),
                    Unit: "m",
                    Reducer: WellKnownReducers.WeightedAverage,
                    Kind: ValueKind.Continuous,
                    Min: -11000.0,
                    Max: 9000.0)
            };
        // CompositeFieldCatalog throws on a duplicate FieldId here; CatalogValidator throws
        // if a descriptor references an unregistered reducer or a kind the reducer rejects.
        _catalog = new CompositeFieldCatalog(seed);
        CatalogValidator.Validate(_catalog, _reducers);

        _truthStore = truthStore ?? new InMemoryTruthEventStore();
        _streamId = new TruthStreamIdentity(
            VariantId: "app",
            BranchId: "main",
            LLevel: 0,
            Domain: "world",
            Model: "default");
    }

    public WorldOverview GetOverview()
    {
        var head = _truthStore.GetHeadAsync(_streamId).GetAwaiter().GetResult();
        return new WorldOverview(
            WorldId: _streamId.ToStreamKey(),
            Name: "FantaSimWorld",
            EntityCount: 0,
            FieldCount: _catalog.All.Count,
            IsDirty: head is not null);
    }

    public WorldFieldValues GetFieldValues(WorldFieldValuesRequest request)
    {
        var values = new Dictionary<string, object>(request.FieldIds.Count, StringComparer.Ordinal);
        foreach (var rawId in request.FieldIds)
        {
            var id = new FieldId(rawId);
            if (_catalog.TryGet(id, out var descriptor))
                values[rawId] = new { unit = descriptor.Unit, kind = descriptor.Kind.ToString(), reducer = descriptor.Reducer.Value };
        }
        return new WorldFieldValues(FieldValues: values);
    }

    public WorldScalarFieldValues GetScalarFieldValues(WorldScalarFieldValuesRequest request)
    {
        var scalars = new Dictionary<string, float>(request.ScalarFieldIds.Count, StringComparer.Ordinal);
        foreach (var rawId in request.ScalarFieldIds)
        {
            var id = new FieldId(rawId);
            if (_catalog.TryGet(id, out var descriptor) && descriptor.Kind == ValueKind.Continuous)
                scalars[rawId] = 0f; // No ECS contributions yet; Task 7 feeds real reduced values.
        }
        return new WorldScalarFieldValues(ScalarValues: scalars);
    }

    public WorldRenderSnapshot GetRenderSnapshot()
        => new(FrameIndex: 0, Entities: Array.Empty<RenderEntityDto>());

    public WorldGenerationResult RunGeneration(WorldGenerationRequest request)
    {
        // Exercise the truth-stream primitive: append one generation event so the stream head
        // advances. The payload is the raw generation spec bytes; the materialization path is
        // deferred to Task 7/9. This proves the store is live and identity-stable.
        var payload = System.Text.Encoding.UTF8.GetBytes(request.GenerationSpec);
        var draft = new TruthEventDraft(
            Stream: _streamId,
            EventType: "world.generation",
            Payload: payload,
            Tick: CanonicalTick.Genesis);
        lock (_truthCommitGate)
        {
            _truthStore.AppendAsync(_streamId, new ITruthEventDraft[] { draft }).GetAwaiter().GetResult();
        }

        return new WorldGenerationResult(
            Success: true,
            Message: "generation-appended",
            ResultWorldId: request.WorldId);
    }

    public void Dispose() { }
}

/// <summary>Concrete draft implementation for appending generation events.</summary>
internal sealed record TruthEventDraft(
    TruthStreamIdentity Stream,
    string EventType,
    ReadOnlyMemory<byte> Payload,
    CanonicalTick Tick) : ITruthEventDraft;
#endif
