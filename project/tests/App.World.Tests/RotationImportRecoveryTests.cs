using System.Text;
using FantaSim.App.World.History;
using FantaSim.App.World.Services;
using FantaSim.Geosphere.Plate.Rotation;
using FantaSim.World.Contracts.Units;
using FantaSim.World.TruthStream;
using FantaSim.World.TruthStream.Core;
using ServiceArchi.Core;
using TimeDete.Time.Primitives;
using UnifyMaths;
using Xunit;

namespace FantaSim.App.World.Tests;

public sealed class RotationImportRecoveryTests
{
    private const long OnsetTick = 42_000_000L;
    private const string Source = """
        001 0 90 0 30 000
        001 4 90 0 36 000
        001 8 90 0 42 000
        002 0 0 90 0 000
        002 8 0 90 20 000
        """;

    [Theory]
    [InlineData("# only a comment\n")]
    [InlineData("1 0 90 0\n")]
    public async Task Invalid_source_appends_nothing(string source)
    {
        var store = new InMemoryTruthEventStore();
        using var writer = new DirectTruthEventWriter(store);
        var coordinator = new RotationImportCoordinator(store, writer);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            coordinator.ImportAsync(Request(source)));

        Assert.True(
            error.Message.Contains("no rotations", StringComparison.OrdinalIgnoreCase)
            || error.Message.Contains("parse issue", StringComparison.OrdinalIgnoreCase));
        Assert.Null(await store.GetHeadAsync(PlateStream));
        Assert.Null(await store.GetHeadAsync(ControlStream));
    }

    [Fact]
    public async Task Valid_source_commits_prepare_plate_bind_and_serves_committed_rotations()
    {
        var store = new InMemoryTruthEventStore();
        using var writer = new DirectTruthEventWriter(store);
        var coordinator = new RotationImportCoordinator(store, writer);

        var outcome = await coordinator.ImportAsync(Request(Source));

        Assert.False(outcome.WasAlreadyBound);
        Assert.Equal(new[] { 1, 2 }, outcome.Provider.ServedPlateIds.OrderBy(id => id));
        Assert.Equal(Quaternion.Identity, outcome.Provider.RotationFromOnsetTo(999, OnsetTick + 500_000L));
        AssertRotationOfUnitX(outcome.Provider.RotationFromOnsetTo(1, OnsetTick), 0.0);
        AssertRotationOfUnitX(
            outcome.Provider.RotationFromOnsetTo(1, OnsetTick + UnitConverter.MegaAnnumToTickDelta(2.0)),
            3.0);

        var control = await ReadAllAsync(store, ControlStream);
        Assert.Equal(
            new[] { RotationImportControlEventTypes.Prepared, RotationImportControlEventTypes.Bound },
            control.Select(e => e.EventType));
        Assert.Equal(new[] { OnsetTick, OnsetTick }, control.Select(e => e.Tick.Value));

        var plate = await ReadAllAsync(store, PlateStream);
        Assert.Equal(5, plate.Count);
        Assert.All(plate, e => Assert.Equal(PlateRotationDraft.PlateRotationEventType, e.EventType));

        var bound = RotationImportPayloadCodec.DecodeBound(control[1].Payload);
        Assert.Equal(0L, bound.PreparedCursor.Sequence);
        Assert.Equal(plate[^1].Sequence, bound.PlateCursor.Sequence);
        Assert.True(bound.PlateCursor.EventHash.Span.SequenceEqual(plate[^1].Hash.Span));
    }

    [Fact]
    public async Task Repeating_same_source_is_idempotent_without_duplicate_events()
    {
        var store = new InMemoryTruthEventStore();
        using var writer = new DirectTruthEventWriter(store);
        var coordinator = new RotationImportCoordinator(store, writer);

        var first = await coordinator.ImportAsync(Request(Source));
        var firstPlateHead = await store.GetHeadAsync(PlateStream);
        var firstControlHead = await store.GetHeadAsync(ControlStream);
        var second = await coordinator.ImportAsync(Request(Source));

        Assert.False(first.WasAlreadyBound);
        Assert.True(second.WasAlreadyBound);
        AssertHeadEqual(firstPlateHead, await store.GetHeadAsync(PlateStream));
        AssertHeadEqual(firstControlHead, await store.GetHeadAsync(ControlStream));
        Assert.Equal(5, (await ReadAllAsync(store, PlateStream)).Count);
        Assert.Equal(2, (await ReadAllAsync(store, ControlStream)).Count);
    }

    [Fact]
    public async Task Different_source_on_bound_branch_fails_closed()
    {
        var store = new InMemoryTruthEventStore();
        using var writer = new DirectTruthEventWriter(store);
        var coordinator = new RotationImportCoordinator(store, writer);
        await coordinator.ImportAsync(Request(Source));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ImportAsync(Request(Source.Replace("42 000", "43 000", StringComparison.Ordinal))));

        Assert.Contains("new branch required", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(5, (await ReadAllAsync(store, PlateStream)).Count);
        Assert.Equal(2, (await ReadAllAsync(store, ControlStream)).Count);
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(3, 5)]
    public async Task Retry_recovers_after_prepare_or_plate_append_without_duplicate_events(
        int failBeforeCall,
        int expectedInterruptedPlateEvents)
    {
        var store = new InMemoryTruthEventStore();
        using (var failingWriter = new FailBeforeCasCallWriter(
                   new DirectTruthEventWriter(store),
                   failBeforeCall))
        {
            var interrupted = new RotationImportCoordinator(store, failingWriter);
            await Assert.ThrowsAsync<InjectedImportFailureException>(() => interrupted.ImportAsync(Request(Source)));
        }

        Assert.Equal(expectedInterruptedPlateEvents, (await ReadAllAsync(store, PlateStream)).Count);
        Assert.Single(await ReadAllAsync(store, ControlStream));

        using var recoveryWriter = new DirectTruthEventWriter(store);
        var recovered = await new RotationImportCoordinator(store, recoveryWriter).ImportAsync(Request(Source));

        Assert.False(recovered.WasAlreadyBound);
        Assert.Equal(5, (await ReadAllAsync(store, PlateStream)).Count);
        Assert.Equal(2, (await ReadAllAsync(store, ControlStream)).Count);
    }

    [Fact]
    public async Task Bound_cursor_hides_later_plate_head_from_playback()
    {
        var store = new InMemoryTruthEventStore();
        using var writer = new DirectTruthEventWriter(store);
        var coordinator = new RotationImportCoordinator(store, writer);
        await coordinator.ImportAsync(Request(Source));

        var laterPayload = new PlateRotationPayload("001", "000", 20.0, 90.0, 0.0, 120.0);
        var laterDraft = new PlateRotationDraft(
            PlateStream,
            laterPayload,
            new CanonicalTick(UnitConverter.MegaAnnumToTickDelta(laterPayload.TimeMa)));
        await store.AppendAsync(PlateStream, new ITruthEventDraft[] { laterDraft });

        var replay = await coordinator.ImportAsync(Request(Source));

        Assert.True(replay.WasAlreadyBound);
        AssertRotationOfUnitX(
            replay.Provider.RotationFromOnsetTo(1, OnsetTick + UnitConverter.MegaAnnumToTickDelta(20.0)),
            12.0);
    }

    [Fact]
    public void Reconstructed_history_coordinator_rediscovers_bound_provider_without_raw_reimport()
    {
        var store = new InMemoryTruthEventStore();
        using var firstWriter = new DirectTruthEventWriter(store);
        using (var first = new WorldHistoryCoordinator(new ServiceRegistry(), store, firstWriter))
        {
            first.ImportRotationSource(
                "world-a",
                "main",
                new FantaSim.App.World.Crust.RotationSourceRecipe(
                    FantaSim.App.World.Crust.RotationSourceKind.Imported,
                    Source,
                    "fixture.rot"),
                OnsetTick);
            Assert.IsType<FantaSim.App.World.Crust.MaterializedRotationProvider>(
                first.GetActiveRotationProjection(OnsetTick).Provider);
        }

        using var secondWriter = new DirectTruthEventWriter(store);
        using var reconstructed = new WorldHistoryCoordinator(
            new ServiceRegistry(),
            store,
            secondWriter);

        var recovered = Service.BuildRotationProvider(
            reconstructed.GetActiveRotationProjection(OnsetTick),
            Array.Empty<FantaSim.Geosphere.Plate.Topology.Plate>(),
            OnsetTick);

        Assert.IsType<FantaSim.App.World.Crust.MaterializedRotationProvider>(recovered);
        AssertRotationOfUnitX(
            recovered.RotationFromOnsetTo(1, OnsetTick + UnitConverter.MegaAnnumToTickDelta(2.0)),
            3.0);
    }

    [Fact]
    public void Recovered_import_fails_closed_when_queried_with_a_different_onset()
    {
        var store = new InMemoryTruthEventStore();
        using var firstWriter = new DirectTruthEventWriter(store);
        using (var first = new WorldHistoryCoordinator(new ServiceRegistry(), store, firstWriter))
        {
            first.ImportRotationSource(
                "world-a",
                "main",
                new FantaSim.App.World.Crust.RotationSourceRecipe(
                    FantaSim.App.World.Crust.RotationSourceKind.Imported,
                    Source,
                    "fixture.rot"),
                OnsetTick);
        }

        using var recoveredWriter = new DirectTruthEventWriter(store);
        using var recovered = new WorldHistoryCoordinator(new ServiceRegistry(), store, recoveredWriter);

        var error = Assert.Throws<InvalidOperationException>(
            () => recovered.GetActiveRotationProjection(OnsetTick + 1));

        Assert.Contains("materialized for onset tick", error.Message, StringComparison.Ordinal);
        Assert.NotNull(recovered.GetActiveRotationProjection(OnsetTick).Provider);
    }

    [Fact]
    public void Explicit_generated_selection_prevents_reload_from_resurrecting_prior_import()
    {
        var store = new InMemoryTruthEventStore();
        using var firstWriter = new DirectTruthEventWriter(store);
        using (var first = new WorldHistoryCoordinator(new ServiceRegistry(), store, firstWriter))
        {
            first.ImportRotationSource(
                "world-a",
                "main",
                new FantaSim.App.World.Crust.RotationSourceRecipe(
                    FantaSim.App.World.Crust.RotationSourceKind.Imported,
                    Source,
                    "fixture.rot"),
                OnsetTick);
            first.UseGeneratedRotationSource();
        }

        using var secondWriter = new DirectTruthEventWriter(store);
        using var reconstructed = new WorldHistoryCoordinator(new ServiceRegistry(), store, secondWriter);
        var selected = Service.BuildRotationProvider(
            reconstructed.GetActiveRotationProjection(OnsetTick),
            Array.Empty<FantaSim.Geosphere.Plate.Topology.Plate>(),
            OnsetTick);

        Assert.IsType<FantaSim.App.World.Crust.GeneratedEulerPoleRotationProvider>(selected);
    }

    [Fact]
    public async Task First_explicit_generated_selection_writes_one_canonical_idempotent_marker()
    {
        var store = new InMemoryTruthEventStore();
        using var writer = new DirectTruthEventWriter(store);
        using var history = new WorldHistoryCoordinator(new ServiceRegistry(), store, writer);

        history.UseGeneratedRotationSource();
        history.UseGeneratedRotationSource();

        var marker = Assert.Single(await ReadAllAsync(store, RotationSelectionStream));
        Assert.Equal(RotationSourceSelectionEventTypes.Generated, marker.EventType);
        RotationSourceSelectionCodec.ValidateGenerated(marker.Payload);
    }

    [Fact]
    public async Task Selection_commit_and_projection_update_are_atomic_to_readers()
    {
        var store = new InMemoryTruthEventStore();
        using var writer = new BlockingImportedSelectionWriter(new DirectTruthEventWriter(store));
        using var history = new WorldHistoryCoordinator(new ServiceRegistry(), store, writer);

        var importTask = Task.Run(() => history.ImportRotationSource(
            "world-a",
            "main",
            new FantaSim.App.World.Crust.RotationSourceRecipe(
                FantaSim.App.World.Crust.RotationSourceKind.Imported,
                Source,
                "fixture.rot"),
            OnsetTick));
        Assert.True(writer.ImportedSelectionCommitted.Wait(TimeSpan.FromSeconds(5)));

        var readerStarted = new ManualResetEventSlim();
        var readTask = Task.Run(() =>
        {
            readerStarted.Set();
            return history.GetActiveRotationProjection(OnsetTick).Provider;
        });
        Assert.True(readerStarted.Wait(TimeSpan.FromSeconds(5)));

        bool readerPassedCommittedSelectionGap;
        try
        {
            var first = await Task.WhenAny(readTask, Task.Delay(100));
            readerPassedCommittedSelectionGap = ReferenceEquals(first, readTask);
        }
        finally
        {
            writer.AllowSelectionWriteToReturn.Set();
        }

        await importTask;
        var provider = await readTask;
        Assert.False(readerPassedCommittedSelectionGap);
        Assert.IsType<FantaSim.App.World.Crust.MaterializedRotationProvider>(provider);
    }

    [Fact]
    public async Task Newer_generated_selection_cannot_be_overwritten_by_earlier_import_projection()
    {
        var store = new InMemoryTruthEventStore();
        using var writer = new ReentrantImportedSelectionWriter(new DirectTruthEventWriter(store));
        using var history = new WorldHistoryCoordinator(new ServiceRegistry(), store, writer);
        writer.AfterImportedSelectionCommitted = history.UseGeneratedRotationSource;

        history.ImportRotationSource(
            "world-a",
            "main",
            new FantaSim.App.World.Crust.RotationSourceRecipe(
                FantaSim.App.World.Crust.RotationSourceKind.Imported,
                Source,
                "fixture.rot"),
            OnsetTick);

        var markers = await ReadAllAsync(store, RotationSelectionStream);
        Assert.Equal(
            new[] { RotationSourceSelectionEventTypes.Imported, RotationSourceSelectionEventTypes.Generated },
            markers.Select(evt => evt.EventType));
        Assert.Null(history.GetActiveRotationProjection(OnsetTick).Provider);
        Assert.IsType<FantaSim.App.World.Crust.GeneratedEulerPoleRotationProvider>(
            Service.BuildRotationProvider(
                history.GetActiveRotationProjection(OnsetTick),
                Array.Empty<FantaSim.Geosphere.Plate.Topology.Plate>(),
                OnsetTick));
    }

    [Fact]
    public async Task Hash_valid_active_index_with_missing_bound_reference_fails_reconstruction()
    {
        var store = new InMemoryTruthEventStore();
        var missingBound = new TruthEventCursor(
            ControlStream,
            1,
            Enumerable.Repeat((byte)0xA5, 32).ToArray(),
            new CanonicalTick(OnsetTick));
        await store.AppendAsync(
            RotationSelectionStream,
            new ITruthEventDraft[]
            {
                new FantaSim.App.World.Services.TruthEventDraft(
                    RotationSelectionStream,
                    RotationSourceSelectionEventTypes.Imported,
                    RotationSourceSelectionCodec.EncodeImported(missingBound),
                    new CanonicalTick(OnsetTick)),
            });

        using var writer = new DirectTruthEventWriter(store);
        var error = Assert.Throws<InvalidOperationException>(() =>
            new WorldHistoryCoordinator(new ServiceRegistry(), store, writer));

        Assert.Contains("bound", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Bound_to_active_index_failure_does_not_complete_and_same_source_retry_converges()
    {
        var store = new InMemoryTruthEventStore();
        using (var failingWriter = new FailBeforeCasCallWriter(
            new DirectTruthEventWriter(store),
            failBeforeCall: 4))
        using (var first = new WorldHistoryCoordinator(new ServiceRegistry(), store, failingWriter))
        {
            Assert.Throws<InjectedImportFailureException>(() => first.ImportRotationSource(
                "world-a",
                "main",
                new FantaSim.App.World.Crust.RotationSourceRecipe(
                    FantaSim.App.World.Crust.RotationSourceKind.Imported,
                    Source,
                    "fixture.rot"),
                OnsetTick));
        }

        Assert.Equal(2, (await ReadAllAsync(store, ControlStream)).Count);
        Assert.Empty(await ReadAllAsync(store, RotationSelectionStream));

        using var retryWriter = new DirectTruthEventWriter(store);
        using (var retry = new WorldHistoryCoordinator(new ServiceRegistry(), store, retryWriter))
        {
            retry.ImportRotationSource(
                "world-a",
                "main",
                new FantaSim.App.World.Crust.RotationSourceRecipe(
                    FantaSim.App.World.Crust.RotationSourceKind.Imported,
                    Source,
                    "fixture.rot"),
                OnsetTick);
            Assert.IsType<FantaSim.App.World.Crust.MaterializedRotationProvider>(
                retry.GetActiveRotationProjection(OnsetTick).Provider);
        }

        Assert.Single(await ReadAllAsync(store, RotationSelectionStream));
    }

    [Fact]
    public async Task Hash_valid_bound_cursor_to_stale_prefix_is_rejected()
    {
        var store = new InMemoryTruthEventStore();
        var prepared = await PrepareOnlyAsync(store, Source);
        var drafts = DraftsFor(Source);
        await store.AppendIfHeadAsync(PlateStream, drafts, expectedHead: null);
        var plateEvents = await ReadAllAsync(store, PlateStream);

        await AppendFabricatedBoundAsync(store, prepared, Cursor(PlateStream, plateEvents[0]));

        using var writer = new DirectTruthEventWriter(store);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RotationImportCoordinator(store, writer).ImportAsync(Request(Source)));
        Assert.Contains("bound", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Hash_valid_bound_cursor_to_different_valid_batch_is_rejected()
    {
        var store = new InMemoryTruthEventStore();
        var prepared = await PrepareOnlyAsync(store, Source);
        var foreignSource = Source.Replace("42 000", "43 000", StringComparison.Ordinal);
        var foreignDrafts = DraftsFor(foreignSource);
        var foreignHead = await store.AppendIfHeadAsync(PlateStream, foreignDrafts, expectedHead: null);

        await AppendFabricatedBoundAsync(store, prepared, new TruthEventCursor(
            PlateStream,
            foreignHead.Sequence,
            foreignHead.Hash,
            foreignHead.LastTick));

        using var writer = new DirectTruthEventWriter(store);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RotationImportCoordinator(store, writer).ImportAsync(Request(Source)));
        Assert.Contains("differs", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Bound_context_must_reference_exact_prepared_then_plate_cursors()
    {
        var store = new InMemoryTruthEventStore();
        var prepared = await PrepareOnlyAsync(store, Source);
        var plateHead = await store.AppendIfHeadAsync(PlateStream, DraftsFor(Source), expectedHead: null);
        var plateCursor = new TruthEventCursor(PlateStream, plateHead.Sequence, plateHead.Hash, plateHead.LastTick);
        await AppendFabricatedBoundAsync(
            store,
            prepared,
            plateCursor,
            new[] { plateCursor, prepared.Cursor });

        using var writer = new DirectTruthEventWriter(store);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RotationImportCoordinator(store, writer).ImportAsync(Request(Source)));

        Assert.Contains("input cursors", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Control_event_envelope_tick_must_equal_payload_onset(bool wrongPreparedEnvelope)
    {
        var preparedTemplateStore = new InMemoryTruthEventStore();
        var template = await PrepareOnlyAsync(preparedTemplateStore, Source);
        var store = new InMemoryTruthEventStore();
        var preparedTick = wrongPreparedEnvelope
            ? new CanonicalTick(OnsetTick - 1)
            : new CanonicalTick(OnsetTick);
        var prepared = await AppendPreparedAsync(store, template.Payload, preparedTick);
        var plateHead = await store.AppendIfHeadAsync(PlateStream, DraftsFor(Source), expectedHead: null);
        var plateCursor = new TruthEventCursor(PlateStream, plateHead.Sequence, plateHead.Hash, plateHead.LastTick);
        await AppendFabricatedBoundAsync(
            store,
            prepared,
            plateCursor,
            boundEventTick: wrongPreparedEnvelope
                ? new CanonicalTick(OnsetTick)
                : new CanonicalTick(OnsetTick + 1));

        using var writer = new DirectTruthEventWriter(store);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RotationImportCoordinator(store, writer).ImportAsync(Request(Source)));

        Assert.Contains("envelope", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Corrupt_genesis_to_H0_prefix_is_rejected_before_bound_is_committed()
    {
        var store = new InMemoryTruthEventStore();
        await store.AppendAsync(
            PlateStream,
            new ITruthEventDraft[]
            {
                new TestDraft(PlateStream, "fixture.prefix.v1", new byte[] { 1 }, CanonicalTick.Genesis),
                new TestDraft(PlateStream, "fixture.prefix.v1", new byte[] { 2 }, CanonicalTick.Genesis),
                new TestDraft(PlateStream, "fixture.prefix.v1", new byte[] { 3 }, CanonicalTick.Genesis),
            });

        using var writer = new DirectTruthEventWriter(store);
        var tampered = new EventPayloadTamperingReader(store, PlateStream, sequence: 0);
        var coordinator = new RotationImportCoordinator(tampered, writer);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ImportAsync(Request(Source)));

        Assert.Contains("hash", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await ReadAllAsync(store, ControlStream));
    }

    [Fact]
    public async Task Simultaneous_same_source_imports_converge_on_one_binding()
    {
        var store = new InMemoryTruthEventStore();
        using var writer = new DirectTruthEventWriter(store);
        var first = new RotationImportCoordinator(store, writer);
        var second = new RotationImportCoordinator(store, writer);

        var outcomes = await Task.WhenAll(
            Task.Run(() => first.ImportAsync(Request(Source))),
            Task.Run(() => second.ImportAsync(Request(Source))));

        Assert.Single(outcomes, outcome => !outcome.WasAlreadyBound);
        Assert.Single(outcomes, outcome => outcome.WasAlreadyBound);
        Assert.Equal(5, (await ReadAllAsync(store, PlateStream)).Count);
        Assert.Equal(2, (await ReadAllAsync(store, ControlStream)).Count);
    }

    [Fact]
    public async Task Simultaneous_different_sources_produce_one_winner_and_one_conflict()
    {
        var store = new InMemoryTruthEventStore();
        using var writer = new DirectTruthEventWriter(store);
        var first = new RotationImportCoordinator(store, writer);
        var second = new RotationImportCoordinator(store, writer);
        var other = Source.Replace("42 000", "43 000", StringComparison.Ordinal);

        var results = await Task.WhenAll(
            Task.Run(() => CaptureAsync(() => first.ImportAsync(Request(Source)))),
            Task.Run(() => CaptureAsync(() => second.ImportAsync(Request(other)))));

        Assert.Single(results, result => result.Outcome is not null);
        var failure = Assert.Single(results, result => result.Error is not null).Error!;
        Assert.Contains("new branch required", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(5, (await ReadAllAsync(store, PlateStream)).Count);
        Assert.Equal(2, (await ReadAllAsync(store, ControlStream)).Count);
    }

    [Fact]
    public void Cas_actor_message_snapshots_expected_hash_and_drafts()
    {
        var expectedHash = Enumerable.Repeat((byte)7, 32).ToArray();
        var expected = new StreamHead(4, expectedHash, new CanonicalTick(8));
        var payload = new byte[] { 1, 2, 3 };
        var draft = new TestDraft(ControlStream, "test", payload, new CanonicalTick(9));

        var message = CasAppendTruthEvents.From(ControlStream, new[] { draft }, expected);
        expectedHash[0] = 99;
        payload[0] = 99;

        Assert.Equal(7, message.ExpectedHead!.Value.Hash[0]);
        Assert.Equal(1, message.Drafts[0].Payload.Span[0]);
    }

    [Fact]
    public async Task Bound_control_hash_chain_tampering_fails_closed()
    {
        var store = new InMemoryTruthEventStore();
        using var writer = new DirectTruthEventWriter(store);
        await new RotationImportCoordinator(store, writer).ImportAsync(Request(Source));

        var tamperedReader = new BoundPreviousHashTamperingReader(store);
        var replay = new RotationImportCoordinator(tamperedReader, writer);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => replay.ImportAsync(Request(Source)));
        Assert.Contains("hash-chain", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static RotationImportRequest Request(string source) => new(
        WorldId: "world-a",
        BranchId: "main",
        SourceName: "fixture.rot",
        SourceBytes: Encoding.UTF8.GetBytes(source),
        OnsetTick: new CanonicalTick(OnsetTick));

    private static IReadOnlyList<PlateRotationDraft> DraftsFor(string source)
    {
        var parsed = new RotParser().Parse("fixture.rot", new StringReader(source));
        Assert.Empty(parsed.Issues);
        return RotationStreamImporter.ToDrafts(parsed, PlateStream);
    }

    private static async Task<(RotationSourcePreparedPayload Payload, TruthEventCursor Cursor)> PrepareOnlyAsync(
        InMemoryTruthEventStore store,
        string source)
    {
        using (var writer = new FailBeforeCasCallWriter(new DirectTruthEventWriter(store), failBeforeCall: 2))
        {
            await Assert.ThrowsAsync<InjectedImportFailureException>(() =>
                new RotationImportCoordinator(store, writer).ImportAsync(Request(source)));
        }

        var control = Assert.Single(await ReadAllAsync(store, ControlStream));
        return (RotationImportPayloadCodec.DecodePrepared(control.Payload), Cursor(ControlStream, control));
    }

    private static async Task AppendFabricatedBoundAsync(
        InMemoryTruthEventStore store,
        (RotationSourcePreparedPayload Payload, TruthEventCursor Cursor) prepared,
        TruthEventCursor plateCursor,
        IReadOnlyList<TruthEventCursor>? contextCursors = null,
        CanonicalTick? boundEventTick = null)
    {
        var preparedContext = prepared.Payload.Context;
        var payload = new RotationSourceBoundPayload(
            new WorldEventContextV1(
                preparedContext.SchemaVersion,
                preparedContext.ProducerComponentId,
                preparedContext.ProducerSemanticVersion,
                preparedContext.ProducerReleaseDigest,
                preparedContext.ModelId,
                preparedContext.ModelVersion,
                preparedContext.CanonicalConfigurationDigest,
                contextCursors ?? new[] { prepared.Cursor, plateCursor },
                preparedContext.ArtifactReferences,
                preparedContext.Units,
                preparedContext.CoordinateReferenceFrameId),
            prepared.Payload.SourceName,
            prepared.Payload.SourceDigest,
            prepared.Payload.ParserReleaseDigest,
            prepared.Payload.OnsetTick,
            prepared.Cursor,
            prepared.Payload.TargetPlateStream,
            plateCursor);
        ITruthEventDraft draft = new FantaSim.App.World.Services.TruthEventDraft(
            ControlStream,
            RotationImportControlEventTypes.Bound,
            RotationImportPayloadCodec.Encode(payload),
            boundEventTick ?? prepared.Payload.OnsetTick);
        await store.AppendIfHeadAsync(
            ControlStream,
            new[] { draft },
            new StreamHead(prepared.Cursor.Sequence, prepared.Cursor.EventHash.ToArray(), prepared.Cursor.Tick));
    }

    private static async Task<(RotationSourcePreparedPayload Payload, TruthEventCursor Cursor)> AppendPreparedAsync(
        InMemoryTruthEventStore store,
        RotationSourcePreparedPayload payload,
        CanonicalTick eventTick)
    {
        var head = await store.AppendIfHeadAsync(
            ControlStream,
            new ITruthEventDraft[]
            {
                new FantaSim.App.World.Services.TruthEventDraft(
                    ControlStream,
                    RotationImportControlEventTypes.Prepared,
                    RotationImportPayloadCodec.Encode(payload),
                    eventTick),
            },
            expectedHead: null);
        return (payload, new TruthEventCursor(ControlStream, head.Sequence, head.Hash, head.LastTick));
    }

    private static TruthEventCursor Cursor(TruthStreamIdentity stream, ITruthEvent evt)
        => new(stream, evt.Sequence, evt.Hash.ToArray(), evt.Tick);

    private static async Task<(RotationImportOutcome? Outcome, Exception? Error)> CaptureAsync(
        Func<Task<RotationImportOutcome>> operation)
    {
        try
        {
            return (await operation(), null);
        }
        catch (Exception ex)
        {
            return (null, ex);
        }
    }

    private static TruthStreamIdentity PlateStream =>
        new("world-a", "main", 2, "geosphere", "plates");

    private static TruthStreamIdentity ControlStream =>
        new("world-a", "main", 2, "world", "imports");

    private static TruthStreamIdentity RotationSelectionStream =>
        new("app", "main", 2, "world", "rotation-bindings");

    private static async Task<List<ITruthEvent>> ReadAllAsync(
        ITruthEventReader reader,
        TruthStreamIdentity stream)
    {
        var result = new List<ITruthEvent>();
        await foreach (var evt in reader.ReadAsync(stream))
            result.Add(evt);
        return result;
    }

    private static void AssertHeadEqual(StreamHead? expected, StreamHead? actual)
    {
        Assert.Equal(expected?.Sequence, actual?.Sequence);
        Assert.Equal(expected?.LastTick, actual?.LastTick);
        Assert.True(expected?.Hash.AsSpan().SequenceEqual(actual?.Hash) ?? actual is null);
    }

    private static void AssertRotationOfUnitX(Quaternion rotation, double expectedDegrees)
    {
        var actual = rotation.Rotate(new Vector3D(1.0, 0.0, 0.0));
        var radians = expectedDegrees * Math.PI / 180.0;
        Assert.InRange(actual.X, Math.Cos(radians) - 1e-9, Math.Cos(radians) + 1e-9);
        Assert.InRange(actual.Y, Math.Sin(radians) - 1e-9, Math.Sin(radians) + 1e-9);
        Assert.InRange(actual.Z, -1e-9, 1e-9);
    }

    private sealed record TestDraft(
        TruthStreamIdentity Stream,
        string EventType,
        ReadOnlyMemory<byte> Payload,
        CanonicalTick Tick) : ITruthEventDraft;

    private sealed class FailBeforeCasCallWriter : ITruthEventWriter
    {
        private readonly ITruthEventWriter _inner;
        private readonly int _failBeforeCall;
        private int _casCalls;

        public FailBeforeCasCallWriter(ITruthEventWriter inner, int failBeforeCall)
        {
            _inner = inner;
            _failBeforeCall = failBeforeCall;
        }

        public Task<StreamHead> AppendAsync(
            TruthStreamIdentity stream,
            IReadOnlyList<ITruthEventDraft> drafts,
            CancellationToken ct = default)
            => _inner.AppendAsync(stream, drafts, ct);

        public Task<StreamHead> AppendIfHeadAsync(
            TruthStreamIdentity stream,
            IReadOnlyList<ITruthEventDraft> drafts,
            StreamHead? expectedHead,
            CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _casCalls) == _failBeforeCall)
                throw new InjectedImportFailureException();
            return _inner.AppendIfHeadAsync(stream, drafts, expectedHead, ct);
        }

        public void Dispose() => _inner.Dispose();
    }

    private sealed class BlockingImportedSelectionWriter : ITruthEventWriter
    {
        private readonly ITruthEventWriter _inner;

        public BlockingImportedSelectionWriter(ITruthEventWriter inner) => _inner = inner;

        public ManualResetEventSlim ImportedSelectionCommitted { get; } = new();
        public ManualResetEventSlim AllowSelectionWriteToReturn { get; } = new();

        public Task<StreamHead> AppendAsync(
            TruthStreamIdentity stream,
            IReadOnlyList<ITruthEventDraft> drafts,
            CancellationToken ct = default)
            => _inner.AppendAsync(stream, drafts, ct);

        public async Task<StreamHead> AppendIfHeadAsync(
            TruthStreamIdentity stream,
            IReadOnlyList<ITruthEventDraft> drafts,
            StreamHead? expectedHead,
            CancellationToken ct = default)
        {
            var result = await _inner.AppendIfHeadAsync(stream, drafts, expectedHead, ct);
            if (stream == RotationSelectionStream
                && drafts.Count == 1
                && drafts[0].EventType == RotationSourceSelectionEventTypes.Imported)
            {
                ImportedSelectionCommitted.Set();
                AllowSelectionWriteToReturn.Wait(ct);
            }
            return result;
        }

        public void Dispose()
        {
            ImportedSelectionCommitted.Dispose();
            AllowSelectionWriteToReturn.Dispose();
            _inner.Dispose();
        }
    }

    private sealed class ReentrantImportedSelectionWriter : ITruthEventWriter
    {
        private readonly ITruthEventWriter _inner;
        private int _callbackInvoked;

        public ReentrantImportedSelectionWriter(ITruthEventWriter inner) => _inner = inner;

        public Action? AfterImportedSelectionCommitted { get; set; }

        public Task<StreamHead> AppendAsync(
            TruthStreamIdentity stream,
            IReadOnlyList<ITruthEventDraft> drafts,
            CancellationToken ct = default)
            => _inner.AppendAsync(stream, drafts, ct);

        public async Task<StreamHead> AppendIfHeadAsync(
            TruthStreamIdentity stream,
            IReadOnlyList<ITruthEventDraft> drafts,
            StreamHead? expectedHead,
            CancellationToken ct = default)
        {
            var result = await _inner.AppendIfHeadAsync(stream, drafts, expectedHead, ct);
            if (stream == RotationSelectionStream
                && drafts.Count == 1
                && drafts[0].EventType == RotationSourceSelectionEventTypes.Imported
                && Interlocked.Exchange(ref _callbackInvoked, 1) == 0)
            {
                AfterImportedSelectionCommitted?.Invoke();
            }
            return result;
        }

        public void Dispose() => _inner.Dispose();
    }

    private sealed class BoundPreviousHashTamperingReader(ITruthEventReader inner) : ITruthEventReader
    {
        public async IAsyncEnumerable<ITruthEvent> ReadAsync(
            TruthStreamIdentity stream,
            long fromSequence = 0,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await foreach (var evt in inner.ReadAsync(stream, fromSequence, ct))
            {
                if (stream == ControlStream && evt.Sequence == 1)
                {
                    yield return new TruthEvent(
                        evt.EventId,
                        evt.Tick,
                        evt.Sequence,
                        evt.StreamIdentity,
                        ReadOnlyMemory<byte>.Empty,
                        evt.Hash,
                        evt.EventType,
                        evt.Payload);
                }
                else
                {
                    yield return evt;
                }
            }
        }

        public Task<StreamHead?> GetHeadAsync(TruthStreamIdentity stream, CancellationToken ct = default)
            => inner.GetHeadAsync(stream, ct);
    }

    private sealed class EventPayloadTamperingReader(
        ITruthEventReader inner,
        TruthStreamIdentity targetStream,
        long sequence) : ITruthEventReader
    {
        public async IAsyncEnumerable<ITruthEvent> ReadAsync(
            TruthStreamIdentity stream,
            long fromSequence = 0,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await foreach (var evt in inner.ReadAsync(stream, fromSequence, ct))
            {
                if (stream == targetStream && evt.Sequence == sequence)
                    yield return new TamperedTruthEvent(evt, evt.Payload.ToArray().Concat(new byte[] { 0xFF }).ToArray());
                else
                    yield return evt;
            }
        }

        public Task<StreamHead?> GetHeadAsync(TruthStreamIdentity stream, CancellationToken ct = default)
            => inner.GetHeadAsync(stream, ct);
    }

    private sealed class TamperedTruthEvent(ITruthEvent inner, ReadOnlyMemory<byte> payload) : ITruthEvent
    {
        public Guid EventId => inner.EventId;
        public CanonicalTick Tick => inner.Tick;
        public long Sequence => inner.Sequence;
        public TruthStreamIdentity StreamIdentity => inner.StreamIdentity;
        public ReadOnlyMemory<byte> PreviousHash => inner.PreviousHash;
        public ReadOnlyMemory<byte> Hash => inner.Hash;
        public string EventType => inner.EventType;
        public ReadOnlyMemory<byte> Payload => payload;
    }

    private sealed class InjectedImportFailureException : Exception;
}
