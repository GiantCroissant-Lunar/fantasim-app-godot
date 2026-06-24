#if USE_PROJECT_REFERENCES
using Akka.Actor;
using FantaSim.App.Ecs;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Services;
using FantaSim.World.Fields;
using FantaSim.World.Fields.Core;
using FantaSim.World.TruthStream;
using FantaSim.World.TruthStream.Core;
using ServiceArchi.Contracts;
using ServiceArchi.Core;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Catalog validation and DTO mapping proof for the App.World T3 orchestrator. These tests
/// exercise the real <see cref="WorldRuntime"/> composition path (sibling fantasim-world
/// project refs); they only compile/run under <c>UseProjectReferences=true</c> because the
/// local lunar-horse feed does not yet carry FantaSim.World.* packages.
/// </summary>
public class WorldRuntimeTests
{
    private static IRegistry NewRegistry() => new ServiceRegistry();

    // ---------------------------------------------------------------------
    // Behavior 1: Startup composition with a valid seed catalog validates and
    // GetOverview reports the composed field count.
    // ---------------------------------------------------------------------
    [Fact]
    public void GetOverview_reports_composed_field_count()
    {
        // Given a registry and a WorldRuntime composed with the default seed catalog
        var registry = NewRegistry();
        using var runtime = new WorldRuntime(registry);

        // When GetOverview is called
        var overview = runtime.GetOverview();

        // Then the overview reports one field and a stream-key world id
        Assert.Equal(1, overview.FieldCount);
        Assert.False(string.IsNullOrEmpty(overview.WorldId));
        Assert.False(overview.IsDirty);
    }

    // ---------------------------------------------------------------------
    // Behavior 2: A duplicate FieldId in the composed descriptors triggers
    // startup validation (CompositeFieldCatalog throws InvalidOperationException).
    // This is the plan's acceptance criterion: "duplicate field id validation
    // test throws as expected."
    // ---------------------------------------------------------------------
    [Fact]
    public void Duplicate_field_id_in_catalog_throws_at_composition()
    {
        // Given a registry and a descriptor list with a duplicate FieldId
        var registry = NewRegistry();
        var dupId = new FieldId("app.elevation-m");
        var descriptors = new[]
        {
            new FieldDescriptor(dupId, "m", WellKnownReducers.WeightedAverage, ValueKind.Continuous),
            new FieldDescriptor(dupId, "m", WellKnownReducers.WeightedAverage, ValueKind.Continuous),
        };

        // When WorldRuntime is composed with the duplicate descriptors
        // Then CompositeFieldCatalog throws InvalidOperationException at construction
        var ex = Assert.Throws<InvalidOperationException>(
            () => new WorldRuntime(registry, descriptors));
        Assert.Contains("Duplicate field id", ex.Message);
    }

    // ---------------------------------------------------------------------
    // Behavior 3: A descriptor referencing an unregistered reducer triggers
    // CatalogValidator at startup (not a silent no-op).
    // ---------------------------------------------------------------------
    [Fact]
    public void Unregistered_reducer_in_catalog_throws_at_composition()
    {
        // Given a registry and a descriptor whose reducer is not registered
        var registry = NewRegistry();
        var descriptors = new[]
        {
            new FieldDescriptor(
                Id: new FieldId("app.unknown-reducer"),
                Unit: "m",
                Reducer: new ReducerId("world.fields.does-not-exist"),
                Kind: ValueKind.Continuous),
        };

        // When WorldRuntime is composed
        // Then CatalogValidator throws InvalidOperationException naming the unregistered reducer
        var ex = Assert.Throws<InvalidOperationException>(
            () => new WorldRuntime(registry, descriptors));
        Assert.Contains("unregistered reducer", ex.Message);
    }

    // ---------------------------------------------------------------------
    // Behavior 4: RunGeneration appends to the truth stream and flips
    // IsDirty on the next overview (DTO mapping proof across the world-lib
    // boundary).
    // ---------------------------------------------------------------------
    [Fact]
    public void RunGeneration_appends_truth_event_and_marks_world_dirty()
    {
        // Given a composed runtime with a clean stream
        var registry = NewRegistry();
        using var runtime = new WorldRuntime(registry);
        Assert.False(runtime.GetOverview().IsDirty);

        // When a generation request is run
        var result = runtime.RunGeneration(new WorldGenerationRequest(
            WorldId: "w1", GenerationSpec: "spec-a", Parameters: new()));

        // Then the result reports success and the overview now reports dirty
        Assert.True(result.Success);
        Assert.Equal("w1", result.ResultWorldId);
        Assert.True(runtime.GetOverview().IsDirty);
    }

    // ---------------------------------------------------------------------
    // Behavior 5: The actor-backed truth writer is the single-writer boundary
    // for generation commits. Concurrent callers may enter RunGeneration, but
    // only one truth append may be in flight at a time for the shared app stream.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task RunGeneration_serializes_truth_stream_appends_through_writer_actor()
    {
        // Given a composed runtime whose truth writes go through a dedicated actor
        var registry = NewRegistry();
        var actorSystem = ActorSystem.Create("world-truth-writer-tests");
        var truthStore = new ConcurrentAppendProbeTruthEventStore(TimeSpan.FromMilliseconds(75));
        try
        {
            using var truthWriter = ActorTruthEventWriter.Start(
                actorSystem,
                truthStore,
                actorName: $"truth-writer-{Guid.NewGuid():N}",
                askTimeout: TimeSpan.FromSeconds(5));
            using var runtime = new WorldRuntime(registry, descriptors: null, truthWriter);

            // When multiple callers ask generation to append concurrently
            var tasks = Enumerable.Range(0, 12)
                .Select(i => Task.Run(() => runtime.RunGeneration(new WorldGenerationRequest(
                    WorldId: $"w{i}",
                    GenerationSpec: $"spec-{i}",
                    Parameters: new()))))
                .ToArray();
            await Task.WhenAll(tasks);

            // Then the writer actor has serialized the appends before they reach the store
            Assert.Equal(12, truthStore.AppendCount);
            Assert.Equal(1, truthStore.MaxConcurrentAppends);
        }
        finally
        {
            await actorSystem.Terminate();
        }
    }

    // ---------------------------------------------------------------------
    // Behavior 6: GetFieldValues maps the composed catalog onto the DTO by
    // field id, returning the descriptor's unit/kind/reducer for known fields
    // and omitting unknown ones.
    // ---------------------------------------------------------------------
    [Fact]
    public void GetFieldValues_maps_catalog_descriptors_to_dto()
    {
        // Given a composed runtime with the default seed field "app.elevation-m"
        var registry = NewRegistry();
        using var runtime = new WorldRuntime(registry);

        // When GetFieldValues is called for a known and an unknown field id
        var values = runtime.GetFieldValues(new WorldFieldValuesRequest(
            WorldId: "w1",
            FieldIds: new[] { "app.elevation-m", "app.does-not-exist" }));

        // Then the known field is present and the unknown field is absent
        Assert.True(values.FieldValues.ContainsKey("app.elevation-m"));
        Assert.False(values.FieldValues.ContainsKey("app.does-not-exist"));
    }

    private sealed class ConcurrentAppendProbeTruthEventStore : ITruthEventStore
    {
        private readonly InMemoryTruthEventStore _inner = new();
        private readonly TimeSpan _appendDelay;
        private int _activeAppends;
        private int _appendCount;
        private int _maxConcurrentAppends;

        public ConcurrentAppendProbeTruthEventStore(TimeSpan appendDelay)
        {
            _appendDelay = appendDelay;
        }

        public int AppendCount => Volatile.Read(ref _appendCount);
        public int MaxConcurrentAppends => Volatile.Read(ref _maxConcurrentAppends);

        public async Task<StreamHead> AppendAsync(
            TruthStreamIdentity stream,
            IReadOnlyList<ITruthEventDraft> drafts,
            CancellationToken ct = default)
        {
            EnterAppend();
            try
            {
                await Task.Delay(_appendDelay, ct);
                return await _inner.AppendAsync(stream, drafts, ct);
            }
            finally
            {
                Interlocked.Decrement(ref _activeAppends);
            }
        }

        public Task<StreamHead> AppendIfHeadAsync(
            TruthStreamIdentity stream,
            IReadOnlyList<ITruthEventDraft> drafts,
            StreamHead? expectedHead,
            CancellationToken ct = default)
            => _inner.AppendIfHeadAsync(stream, drafts, expectedHead, ct);

        public IAsyncEnumerable<ITruthEvent> ReadAsync(
            TruthStreamIdentity stream,
            long fromSequence = 0,
            CancellationToken ct = default)
            => _inner.ReadAsync(stream, fromSequence, ct);

        public Task<StreamHead?> GetHeadAsync(TruthStreamIdentity stream, CancellationToken ct = default)
            => _inner.GetHeadAsync(stream, ct);

        private void EnterAppend()
        {
            Interlocked.Increment(ref _appendCount);
            var active = Interlocked.Increment(ref _activeAppends);
            int snapshot;
            do
            {
                snapshot = Volatile.Read(ref _maxConcurrentAppends);
                if (active <= snapshot)
                    return;
            } while (Interlocked.CompareExchange(ref _maxConcurrentAppends, active, snapshot) != snapshot);
        }
    }
}
#endif
