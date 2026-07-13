using FantaSim.App.World.Dto;
using FantaSim.App.World.Crust;
using FantaSim.App.World.History;
using FantaSim.World.Fields;
using FantaSim.World.Fields.Core;
using FantaSim.World.TruthStream;
using ServiceArchi.Contracts;
using TimeDete.Time.Primitives;

namespace FantaSim.App.World.Services;

/// <summary>
/// Real <see cref="IWorldHistoryCoordinator"/> composing the sibling fantasim-world stack at construction:
/// <list type="bullet">
/// <item><see cref="CompositeFieldCatalog"/> + <see cref="FieldReducerRegistry"/> +
///       <see cref="DefaultReducers"/> + <see cref="CatalogValidator"/> validate the field
///       system at startup (duplicate FieldId or an unregistered reducer fails fast).</item>
/// <item><see cref="ITruthEventWriter"/> backs generation appends so
///       <see cref="RunGeneration"/> exercises the truth-stream primitives.</item>
/// </list>
/// World-lib types never cross this boundary; every method maps onto the app-side DTOs.
/// </summary>
internal sealed class WorldHistoryCoordinator : IWorldHistoryCoordinator
{
    private static readonly TruthStreamIdentity RotationSelectionStream = new(
        VariantId: "app",
        BranchId: "main",
        LLevel: 0,
        Domain: "world",
        Model: "rotation-bindings");

    private readonly IFieldCatalog _catalog;
    private readonly IFieldReducerRegistry _reducers;
    private readonly ITruthEventReader _truthReader;
    private readonly ITruthEventWriter _truthWriter;
    private readonly RotationImportCoordinator _rotationImports;
    private readonly TruthStreamIdentity _streamId;
    // One authority gate covers both the durable selection transition and its in-memory
    // projection. Readers participate in the same gate, so no caller can observe the interval
    // after the selection CAS commits but before the provider cache changes.
    private readonly object _rotationStateGate = new();
    private MaterializedRotationProvider? _activeRotationProvider;
    private long _activeRotationOnsetTick;

    // Composed at construction. The descriptor seed is intentionally minimal here (one
    // continuous field driven by the built-in WeightedAverage reducer); Task 7 will expand the
    // field set via per-sphere catalog modules. CatalogValidator.Validate throws on a duplicate
    // FieldId or a reducer/kind mismatch — that is the startup composition contract.
    public WorldHistoryCoordinator(
        IRegistry registry,
        ITruthEventReader truthReader,
        ITruthEventWriter truthWriter)
        : this(registry, descriptors: null, truthReader, truthWriter)
    {
    }

    // Test hook: lets App.World.Tests inject explicit descriptor/writer dependencies to prove
    // startup validation and app-side truth-stream coordination without touching fantasim-world.
    internal WorldHistoryCoordinator(
        IRegistry registry,
        IReadOnlyList<FieldDescriptor>? descriptors,
        ITruthEventReader truthReader,
        ITruthEventWriter truthWriter)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _truthReader = truthReader ?? throw new ArgumentNullException(nameof(truthReader));
        _truthWriter = truthWriter ?? throw new ArgumentNullException(nameof(truthWriter));
        _rotationImports = new RotationImportCoordinator(_truthReader, _truthWriter);

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

        _streamId = new TruthStreamIdentity(
            VariantId: "app",
            BranchId: "main",
            LLevel: 0,
            Domain: "world",
            Model: "default");

        RestoreActiveRotationSource();
    }

    public WorldOverview GetOverview()
    {
        var head = _truthReader.GetHeadAsync(_streamId).GetAwaiter().GetResult();
        return new WorldOverview(
            WorldId: _streamId.ToStreamKey(),
            Name: "FantaSimWorld",
            EntityCount: 0,
            FieldCount: _catalog.All.Count,
            IsDirty: head is not null);
    }

    public WorldFieldValues GetFieldValues(WorldFieldValuesRequest request)
    {
        var values = new Dictionary<string, WorldFieldDescriptorDto>(request.FieldIds.Count, StringComparer.Ordinal);
        foreach (var rawId in request.FieldIds)
        {
            var id = new FieldId(rawId);
            if (_catalog.TryGet(id, out var descriptor))
                values[rawId] = new WorldFieldDescriptorDto(descriptor.Unit, descriptor.Kind.ToString(), descriptor.Reducer.Value);
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
        _truthWriter.AppendAsync(_streamId, new ITruthEventDraft[] { draft }).GetAwaiter().GetResult();

        return new WorldGenerationResult(
            Success: true,
            Message: "generation-appended",
            ResultWorldId: request.WorldId);
    }

    public void UseGeneratedRotationSource()
    {
        lock (_rotationStateGate)
        {
            var terminal = RecordRotationSelection(importedBoundCursor: null);
            if (!SelectionStateMatches(terminal, importedBoundCursor: null))
                return; // A newer re-entrant selection owns both terminal truth and projection.
            _activeRotationProvider = null;
            _activeRotationOnsetTick = 0L;
        }
    }

    public void ImportRotationSource(
        string worldId,
        string branchId,
        RotationSourceRecipe recipe,
        long onsetTick)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (recipe.Kind != RotationSourceKind.Imported || string.IsNullOrWhiteSpace(recipe.RotText))
            throw new ArgumentException("An imported rotation recipe with non-empty source bytes is required.", nameof(recipe));

        var outcome = _rotationImports.ImportAsync(new RotationImportRequest(
                worldId,
                branchId,
                recipe.SourceName,
                System.Text.Encoding.UTF8.GetBytes(recipe.RotText),
                new CanonicalTick(onsetTick)))
            .GetAwaiter()
            .GetResult();

        lock (_rotationStateGate)
        {
            var terminal = RecordRotationSelection(outcome.BoundCursor);
            if (!SelectionStateMatches(terminal, outcome.BoundCursor))
                return; // A newer re-entrant selection owns both terminal truth and projection.
            _activeRotationProvider = outcome.Provider;
            _activeRotationOnsetTick = onsetTick;
        }
    }

    public IPlateRotationProvider? GetActiveRotationProvider(long onsetTick)
    {
        lock (_rotationStateGate)
            return _activeRotationProvider is not null && _activeRotationOnsetTick == onsetTick
                ? _activeRotationProvider
                : null;
    }

    public void Dispose()
    {
        // Service owns and disposes the injected reader/store and writer after this coordinator.
        // Clearing the materialized projection releases its circuit without changing the durable
        // active-source selection: a later collectible instance must recover the same authority.
        lock (_rotationStateGate)
        {
            _activeRotationProvider = null;
            _activeRotationOnsetTick = 0L;
        }
    }

    private void RestoreActiveRotationSource()
    {
        var selection = ReadRotationSelectionAsync().GetAwaiter().GetResult();
        if (selection.ImportedBoundCursor is not { } boundCursor)
            return;

        var recovered = _rotationImports.RecoverBoundAsync(boundCursor).GetAwaiter().GetResult();
        lock (_rotationStateGate)
        {
            _activeRotationProvider = recovered.Provider;
            _activeRotationOnsetTick = recovered.OnsetTick;
        }
    }

    private RotationSelectionState RecordRotationSelection(TruthEventCursor? importedBoundCursor)
    {
        var observed = ReadRotationSelectionAsync().GetAwaiter().GetResult();
        if (SelectionStateMatches(observed, importedBoundCursor))
            return observed;

        var eventType = importedBoundCursor is null
            ? RotationSourceSelectionEventTypes.Generated
            : RotationSourceSelectionEventTypes.Imported;
        var payload = importedBoundCursor is { } bound
            ? RotationSourceSelectionCodec.EncodeImported(bound)
            : RotationSourceSelectionCodec.EncodeGenerated();
        var eventTick = importedBoundCursor is { } imported
            ? new CanonicalTick(Math.Max(observed.Head?.LastTick.Value ?? 0L, imported.Tick.Value))
            : observed.Head?.LastTick ?? CanonicalTick.Genesis;
        var draft = new TruthEventDraft(RotationSelectionStream, eventType, payload, eventTick);

        try
        {
            _truthWriter.AppendIfHeadAsync(
                    RotationSelectionStream,
                    new ITruthEventDraft[] { draft },
                    observed.Head)
                .GetAwaiter()
                .GetResult();
            // Reread rather than assuming our append stayed terminal: an injected writer or
            // callback may perform a newer re-entrant selection before returning.
            return ReadRotationSelectionAsync().GetAwaiter().GetResult();
        }
        catch (TruthStreamConcurrencyException ex)
        {
            // A racing writer is allowed to converge on the identical selection. A different
            // marker is ambiguous authority and therefore fails closed instead of using last-wins.
            var concurrent = ReadRotationSelectionAsync().GetAwaiter().GetResult();
            if (!concurrent.HasSelection
                || !SelectionsEqual(concurrent.ImportedBoundCursor, importedBoundCursor))
            {
                throw new InvalidOperationException(
                    "Rotation active-source index changed concurrently to a different selection.",
                    ex);
            }
            return concurrent;
        }
    }

    private async Task<RotationSelectionState> ReadRotationSelectionAsync(CancellationToken ct = default)
    {
        ReadOnlyMemory<byte> expectedPreviousHash = ReadOnlyMemory<byte>.Empty;
        long expectedSequence = 0;
        ITruthEvent? terminal = null;
        TruthEventCursor? importedBoundCursor = null;

        await foreach (var evt in _truthReader.ReadAsync(RotationSelectionStream, 0, ct).ConfigureAwait(false))
        {
            if (evt.StreamIdentity != RotationSelectionStream || evt.Sequence != expectedSequence)
                throw new InvalidOperationException("Rotation active-source index identity/sequence mismatch.");
            if (!evt.PreviousHash.Span.SequenceEqual(expectedPreviousHash.Span))
                throw new InvalidOperationException("Rotation active-source index hash-chain break.");

            var recomputed = FantaSim.World.TruthStream.Core.EventHasher.ComputeHash(
                new FantaSim.World.TruthStream.Core.TruthEvent(
                    evt.EventId,
                    evt.Tick,
                    evt.Sequence,
                    evt.StreamIdentity,
                    evt.PreviousHash,
                    evt.Hash,
                    evt.EventType,
                    evt.Payload));
            if (!recomputed.AsSpan().SequenceEqual(evt.Hash.Span))
                throw new InvalidOperationException("Rotation active-source index hash recomputation failed.");

            if (string.Equals(evt.EventType, RotationSourceSelectionEventTypes.Imported, StringComparison.Ordinal))
            {
                var decoded = RotationSourceSelectionCodec.DecodeImported(evt.Payload);
                if (!RotationSourceSelectionCodec.EncodeImported(decoded).AsSpan().SequenceEqual(evt.Payload.Span))
                    throw new InvalidOperationException("Rotation imported-selection payload is not canonical.");
                importedBoundCursor = decoded;
            }
            else if (string.Equals(evt.EventType, RotationSourceSelectionEventTypes.Generated, StringComparison.Ordinal))
            {
                RotationSourceSelectionCodec.ValidateGenerated(evt.Payload);
                importedBoundCursor = null;
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unknown rotation active-source index event '{evt.EventType}'.");
            }

            terminal = evt;
            expectedPreviousHash = evt.Hash;
            expectedSequence++;
        }

        var head = await _truthReader.GetHeadAsync(RotationSelectionStream, ct).ConfigureAwait(false);
        if (terminal is null)
        {
            if (head is not null)
                throw new InvalidOperationException("Rotation active-source index head exists without events.");
            return new RotationSelectionState(null, HasSelection: false, ImportedBoundCursor: null);
        }

        if (head is null
            || terminal.Sequence != head.Value.Sequence
            || terminal.Tick != head.Value.LastTick
            || !terminal.Hash.Span.SequenceEqual(head.Value.Hash))
        {
            throw new InvalidOperationException("Rotation active-source index head changed during verification.");
        }

        return new RotationSelectionState(
            new StreamHead(head.Value.Sequence, head.Value.Hash.ToArray(), head.Value.LastTick),
            HasSelection: true,
            importedBoundCursor);
    }

    private static bool SelectionsEqual(TruthEventCursor? left, TruthEventCursor? right)
    {
        if (left is null || right is null)
            return left is null && right is null;
        return left.Value == right.Value;
    }

    private static bool SelectionStateMatches(
        RotationSelectionState state,
        TruthEventCursor? importedBoundCursor)
        => state.HasSelection
            && SelectionsEqual(state.ImportedBoundCursor, importedBoundCursor);

    private sealed record RotationSelectionState(
        StreamHead? Head,
        bool HasSelection,
        TruthEventCursor? ImportedBoundCursor);
}

/// <summary>Concrete draft implementation for appending generation events.</summary>
internal sealed record TruthEventDraft(
    TruthStreamIdentity Stream,
    string EventType,
    ReadOnlyMemory<byte> Payload,
    CanonicalTick Tick) : ITruthEventDraft;
