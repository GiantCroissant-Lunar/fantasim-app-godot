using System.Security.Cryptography;
using System.Text;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Crust;
using FantaSim.App.World.Services;
using FantaSim.Geosphere.Plate.Reconstruction;
using FantaSim.Geosphere.Plate.Rotation;
using FantaSim.World.TruthStream;
using TimeDete.Time.Primitives;

namespace FantaSim.App.World.History;

internal sealed record RotationImportRequest(
    string WorldId,
    string BranchId,
    string SourceName,
    ReadOnlyMemory<byte> SourceBytes,
    CanonicalTick OnsetTick);

internal sealed record RotationImportOutcome(
    MaterializedRotationProvider Provider,
    TruthEventCursor PreparedCursor,
    TruthEventCursor PlateCursor,
    TruthEventCursor BoundCursor,
    long OnsetTick,
    bool WasAlreadyBound);

/// <summary>
/// Commits imported finite rotations through the recoverable prepare -&gt; plate CAS -&gt; bind
/// protocol. The injected reader and writer remain owned by the outer service; this coordinator
/// never disposes either capability and never reparses source bytes for playback.
/// </summary>
internal sealed class RotationImportCoordinator
{
    private const string ProducerComponentId = "fantasim-app.world.rotation-import";
    private const string ProducerSemanticVersion = "0.1.11";
    private const string ModelId = "gplates-total-finite-rotation";
    private const string ModelVersion = "1";
    private const string Units = "Ma,degrees,canonical-tick";
    private const string ReferenceFrame = "gplates-anchor-plate";
    private const int MaxConcurrencyRetries = 8;

    private static readonly byte[] ConfigurationDigest =
        RotationImportPayloadCodec.ComputeCanonicalConfigurationDigest(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["draft-order"] = "tick,moving-plate-id,fixed-plate-id",
                ["onset-ma"] = "0",
                ["time-direction"] = "increasing-canonical-tick-is-increasing-Ma-before-present",
            });

    private static readonly byte[] ProducerReleaseDigest =
        RotationImportPayloadCodec.ComputeProducerReleaseDigest(
            ProducerComponentId,
            ProducerSemanticVersion,
            new ReadOnlyMemory<byte>[]
            {
                SHA256.HashData(Encoding.UTF8.GetBytes("FantaSim.Geosphere.Plate.Rotation.RotParser/v1")),
                SHA256.HashData(Encoding.UTF8.GetBytes("world.rotation-source-control/v1")),
            },
            ConfigurationDigest);

    private readonly ITruthEventReader _reader;
    private readonly ITruthEventWriter _writer;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public RotationImportCoordinator(ITruthEventReader reader, ITruthEventWriter writer)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public async Task<RotationImportOutcome> ImportAsync(
        RotationImportRequest request,
        CancellationToken ct = default)
    {
        var candidate = ParseCandidate(request);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            for (var attempt = 0; attempt < MaxConcurrencyRetries; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                ControlState state;
                try
                {
                    state = await ReadControlStateAsync(candidate.ControlStream, ct).ConfigureAwait(false);
                }
                catch (ControlStateChangedException) when (attempt + 1 < MaxConcurrencyRetries)
                {
                    continue;
                }

                if (state.Bound is { } boundState)
                {
                    EnsureSameCandidate(candidate, boundState.Prepared.Payload);
                    await VerifyPreparedBatchAsync(
                        boundState.Prepared,
                        boundState.Payload,
                        candidate.Drafts,
                        ct).ConfigureAwait(false);
                    return await MaterializeAsync(boundState, wasAlreadyBound: true, ct).ConfigureAwait(false);
                }

                if (state.Prepared is not { } preparedState)
                {
                    var priorPlateHead = await _reader.GetHeadAsync(candidate.PlateStream, ct).ConfigureAwait(false);
                    var preparedPayload = BuildPrepared(candidate, priorPlateHead);
                    var preparedDraft = Draft(
                        candidate.ControlStream,
                        RotationImportControlEventTypes.Prepared,
                        RotationImportPayloadCodec.Encode(preparedPayload),
                        candidate.Request.OnsetTick);

                    try
                    {
                        await _writer.AppendIfHeadAsync(
                            candidate.ControlStream,
                            new[] { preparedDraft },
                            state.Head,
                            ct).ConfigureAwait(false);
                    }
                    catch (TruthStreamConcurrencyException) when (attempt + 1 < MaxConcurrencyRetries)
                    {
                        continue;
                    }

                    continue;
                }

                EnsureSameCandidate(candidate, preparedState.Payload);
                var plateHead = await _reader.GetHeadAsync(candidate.PlateStream, ct).ConfigureAwait(false);
                StreamHead committedPlateHead;

                if (HeadsEqual(plateHead, preparedState.Payload.ExpectedPriorPlateHead))
                {
                    try
                    {
                        committedPlateHead = await _writer.AppendIfHeadAsync(
                            candidate.PlateStream,
                            candidate.Drafts,
                            preparedState.Payload.ExpectedPriorPlateHead,
                            ct).ConfigureAwait(false);
                    }
                    catch (TruthStreamConcurrencyException) when (attempt + 1 < MaxConcurrencyRetries)
                    {
                        continue;
                    }
                }
                else
                {
                    if (plateHead is null)
                        throw Conflict("Prepared import expected an appended plate batch, but the plate stream is empty.");
                    committedPlateHead = plateHead.Value;
                }

                var preparedCursor = preparedState.Cursor;
                var plateCursor = ToCursor(candidate.PlateStream, committedPlateHead);
                var boundPayload = BuildBound(candidate, preparedCursor, plateCursor);
                await VerifyPreparedBatchAsync(
                    preparedState,
                    boundPayload,
                    candidate.Drafts,
                    ct).ConfigureAwait(false);
                var model = await MaterializeBoundModelAsync(
                    candidate.PlateStream,
                    plateCursor,
                    ct).ConfigureAwait(false);
                var boundDraft = Draft(
                    candidate.ControlStream,
                    RotationImportControlEventTypes.Bound,
                    RotationImportPayloadCodec.Encode(boundPayload),
                    candidate.Request.OnsetTick);

                try
                {
                    var preparedHead = ToHead(preparedCursor);
                    var boundHead = await _writer.AppendIfHeadAsync(
                        candidate.ControlStream,
                        new[] { boundDraft },
                        preparedHead,
                        ct).ConfigureAwait(false);
                    var boundCursor = ToCursor(candidate.ControlStream, boundHead);

                    return new RotationImportOutcome(
                        new MaterializedRotationProvider(model, candidate.Request.OnsetTick.Value),
                        preparedCursor,
                        plateCursor,
                        boundCursor,
                        candidate.Request.OnsetTick.Value,
                        WasAlreadyBound: false);
                }
                catch (TruthStreamConcurrencyException) when (attempt + 1 < MaxConcurrencyRetries)
                {
                    continue;
                }
            }

            throw Conflict("Import did not converge after repeated concurrent control-stream changes.");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Rehydrates an imported source from an app-owned active-selection cursor. The cursor is only
    /// an index: the referenced control event, its prepared predecessor, and the exact plate batch
    /// are reread and hash-verified before any provider becomes active.
    /// </summary>
    public async Task<RotationImportOutcome> RecoverBoundAsync(
        TruthEventCursor boundCursor,
        CancellationToken ct = default)
    {
        if (!boundCursor.IsBounded
            || boundCursor.Stream.LLevel != WorldStreamVocabulary.WorldLLevel
            || !string.Equals(boundCursor.Stream.Domain, "world", StringComparison.Ordinal)
            || !string.Equals(boundCursor.Stream.Model, "imports", StringComparison.Ordinal))
        {
            throw Conflict("Active-source index does not reference a bounded rotation import control event.");
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var state = await ReadControlStateAsync(boundCursor.Stream, ct).ConfigureAwait(false);
            if (state.Bound is not { } bound || !CursorsEqual(bound.Cursor, boundCursor))
                throw Conflict("Active-source index does not reference the exact bound control-stream head.");

            await VerifyPreparedBatchAsync(bound.Prepared, bound.Payload, expectedDrafts: null, ct)
                .ConfigureAwait(false);
            return await MaterializeAsync(bound, wasAlreadyBound: true, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<RotationImportOutcome> MaterializeAsync(
        BoundControlState state,
        bool wasAlreadyBound,
        CancellationToken ct)
    {
        var model = await MaterializeBoundModelAsync(
            state.Payload.PlateStream,
            state.Payload.PlateCursor,
            ct).ConfigureAwait(false);
        return new RotationImportOutcome(
            new MaterializedRotationProvider(model, state.Payload.OnsetTick.Value),
            state.Payload.PreparedCursor,
            state.Payload.PlateCursor,
            state.Cursor,
            state.Payload.OnsetTick.Value,
            wasAlreadyBound);
    }

    private async Task<RotationModel> MaterializeBoundModelAsync(
        TruthStreamIdentity plateStream,
        TruthEventCursor plateCursor,
        CancellationToken ct)
    {
        // Anchor spelling is part of the committed semantic events (real files commonly use
        // either "0" or "000"). Resolve it from the bounded event prefix instead of reparsing raw
        // source bytes, then let the engine verify that same prefix and build the circuit.
        var movingIds = new HashSet<string>(StringComparer.Ordinal);
        var fixedIds = new HashSet<string>(StringComparer.Ordinal);
        await foreach (var evt in _reader.ReadAsync(plateStream, 0, ct).ConfigureAwait(false))
        {
            if (evt.Sequence > plateCursor.Sequence)
                break;
            if (evt.EventType == PlateRotationDraft.PlateRotationEventType)
            {
                var payload = PlateRotationPayloadCodec.Decode(evt.Payload);
                movingIds.Add(payload.MovingPlateId);
                fixedIds.Add(payload.FixedPlateId);
            }
            if (evt.Sequence == plateCursor.Sequence)
                break;
        }

        var roots = fixedIds.Where(id => !movingIds.Contains(id)).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        if (roots.Length != 1)
        {
            throw new InvalidOperationException(
                $"Bound rotation history must resolve exactly one anchor plate; found {roots.Length}.");
        }

        return await RotationModelMaterializer.MaterializeAsync(
            _reader,
            plateStream,
            plateCursor,
            anchorPlateId: roots[0],
            ct).ConfigureAwait(false);
    }

    private static ImportCandidate ParseCandidate(RotationImportRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorldId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BranchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceName);
        if (request.SourceBytes.IsEmpty)
            throw new ArgumentException("Imported rotation source contained no rotations.", nameof(request));

        string sourceText;
        try
        {
            sourceText = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(request.SourceBytes.Span);
        }
        catch (DecoderFallbackException ex)
        {
            throw new ArgumentException("Imported rotation source is not valid UTF-8.", nameof(request), ex);
        }

        var parsed = new RotParser().Parse(request.SourceName, new StringReader(sourceText));
        if (parsed.Issues.Count > 0)
        {
            throw new ArgumentException(
                $"Imported rotation source '{request.SourceName}' has {parsed.Issues.Count} parse issue(s): "
                + string.Join("; ", parsed.Issues.Select(issue =>
                    $"line {issue.LineNumber} [{issue.Code}]: {issue.Message}")),
                nameof(request));
        }

        var plateStream = WorldStreamVocabulary.Plates(request.WorldId, request.BranchId);
        var controlStream = WorldStreamVocabulary.ImportsControl(request.WorldId, request.BranchId);
        var drafts = RotationStreamImporter.ToDrafts(parsed, plateStream);
        if (drafts.Count == 0)
        {
            throw new ArgumentException(
                $"Imported rotation source '{request.SourceName}' contained no rotations.",
                nameof(request));
        }

        return new ImportCandidate(
            request,
            plateStream,
            controlStream,
            drafts,
            RotationImportPayloadCodec.ComputeSourceDigest(request.SourceBytes),
            RotationImportPayloadCodec.ComputeDraftDigest(drafts));
    }

    private async Task<ControlState> ReadControlStateAsync(
        TruthStreamIdentity controlStream,
        CancellationToken ct)
    {
        var events = new List<ITruthEvent>();
        ReadOnlyMemory<byte> expectedPreviousHash = ReadOnlyMemory<byte>.Empty;
        long expectedSequence = 0;
        await foreach (var evt in _reader.ReadAsync(controlStream, 0, ct).ConfigureAwait(false))
        {
            if (evt.StreamIdentity != controlStream || evt.Sequence != expectedSequence)
            {
                throw new InvalidOperationException(
                    $"Import control stream identity/sequence mismatch at sequence {evt.Sequence}; "
                    + $"expected {expectedSequence} in '{controlStream.ToStreamKey()}'.");
            }
            if (!evt.PreviousHash.Span.SequenceEqual(expectedPreviousHash.Span))
            {
                throw new InvalidOperationException(
                    $"Import control hash-chain break at sequence {evt.Sequence}.");
            }

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
            {
                throw new InvalidOperationException(
                    $"Import control hash-chain recomputation mismatch at sequence {evt.Sequence}.");
            }

            events.Add(evt);
            expectedPreviousHash = evt.Hash.ToArray();
            expectedSequence++;
        }

        var head = await _reader.GetHeadAsync(controlStream, ct).ConfigureAwait(false);
        if (events.Count == 0)
        {
            if (head is not null)
                throw new ControlStateChangedException();
            return new ControlState(null, null, null);
        }

        if (head is null || !HeadsEqual(head, ToHead(ToCursor(controlStream, events[^1]))))
            throw new ControlStateChangedException();

        PreparedControlState? prepared = null;
        BoundControlState? bound = null;
        foreach (var evt in events)
        {
            if (evt.EventType == RotationImportControlEventTypes.Prepared)
            {
                if (prepared is not null || bound is not null)
                    throw Conflict("Control stream contains more than one import state transition sequence.");
                prepared = new PreparedControlState(
                    RotationImportPayloadCodec.DecodePrepared(evt.Payload),
                    ToCursor(controlStream, evt));
                EnsureControlEnvelopeTick(
                    prepared.Cursor,
                    prepared.Payload.OnsetTick,
                    "prepared");
                continue;
            }

            if (evt.EventType == RotationImportControlEventTypes.Bound)
            {
                if (prepared is null || bound is not null)
                    throw Conflict("Bound control event does not follow exactly one prepared event.");
                bound = new BoundControlState(
                    RotationImportPayloadCodec.DecodeBound(evt.Payload),
                    ToCursor(controlStream, evt),
                    prepared);
                EnsureControlEnvelopeTick(
                    bound.Cursor,
                    bound.Payload.OnsetTick,
                    "bound");
                continue;
            }

            throw Conflict($"Unknown import control event '{evt.EventType}'.");
        }

        if (bound is not null && bound.Cursor.Sequence != events[^1].Sequence)
            throw Conflict("Bound import is not the exact control-stream head.");
        if (bound is null && prepared?.Cursor.Sequence != events[^1].Sequence)
            throw Conflict("Prepared import is not the exact control-stream head.");

        return new ControlState(head, prepared, bound);
    }

    /// <summary>
    /// Shared proof gate for orphan recovery, already-bound replay, and cold reload. A hash-valid
    /// cursor alone is not authority: it must terminate precisely the batch committed by the
    /// prepared event, whose ordered semantic bytes are committed by DraftDigest.
    /// </summary>
    private async Task VerifyPreparedBatchAsync(
        PreparedControlState prepared,
        RotationSourceBoundPayload bound,
        IReadOnlyList<PlateRotationDraft>? expectedDrafts,
        CancellationToken ct)
    {
        EnsureBoundMatchesPrepared(prepared, bound);

        var priorHead = prepared.Payload.ExpectedPriorPlateHead;
        if (prepared.Payload.DraftCount <= 0)
            throw Conflict("Prepared rotation source has a non-positive draft count.");

        long firstSequence;
        long expectedTerminalSequence;
        try
        {
            firstSequence = checked((priorHead?.Sequence ?? -1L) + 1L);
            expectedTerminalSequence = checked(firstSequence + prepared.Payload.DraftCount - 1L);
        }
        catch (OverflowException)
        {
            throw Conflict("Prepared rotation batch sequence range overflows Int64.");
        }

        if (bound.PlateCursor.Sequence != expectedTerminalSequence)
        {
            throw Conflict(
                "Bound plate cursor does not terminate the exact prepared draft-count range.");
        }

        ReadOnlyMemory<byte> expectedPreviousHash = ReadOnlyMemory<byte>.Empty;
        if (priorHead is { } h0)
        {
            await VerifyPrefixThroughAsync(
                prepared.Payload.TargetPlateStream,
                h0,
                ct).ConfigureAwait(false);
            expectedPreviousHash = h0.Hash;
        }

        var actualDrafts = new List<PlateRotationDraft>(prepared.Payload.DraftCount);
        var observedCount = 0;
        ITruthEvent? terminal = null;
        await foreach (var evt in _reader.ReadAsync(
            prepared.Payload.TargetPlateStream,
            firstSequence,
            ct).ConfigureAwait(false))
        {
            if (evt.Sequence > expectedTerminalSequence)
                break;

            var expectedSequence = firstSequence + observedCount;
            if (evt.StreamIdentity != prepared.Payload.TargetPlateStream || evt.Sequence != expectedSequence)
                throw Conflict($"Plate batch identity/sequence mismatch at offset {observedCount}.");
            if (!evt.PreviousHash.Span.SequenceEqual(expectedPreviousHash.Span))
                throw Conflict($"Plate batch hash-chain break at offset {observedCount}.");
            VerifyEventHash(evt, $"plate batch event at offset {observedCount}");
            if (!string.Equals(evt.EventType, PlateRotationDraft.PlateRotationEventType, StringComparison.Ordinal))
                throw Conflict($"Plate batch has an unexpected event type at offset {observedCount}.");

            PlateRotationDraft canonicalDraft;
            try
            {
                canonicalDraft = new PlateRotationDraft(
                    prepared.Payload.TargetPlateStream,
                    PlateRotationPayloadCodec.Decode(evt.Payload),
                    evt.Tick);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw Conflict($"Plate batch payload cannot be decoded at offset {observedCount}: {ex.Message}");
            }
            if (!canonicalDraft.Payload.Span.SequenceEqual(evt.Payload.Span))
                throw Conflict($"Plate batch payload is not canonical at offset {observedCount}.");

            if (expectedDrafts is { } candidateDrafts)
            {
                if (observedCount >= candidateDrafts.Count)
                    throw Conflict("Plate batch contains more events than the recomputed candidate drafts.");
                var candidateDraft = candidateDrafts[observedCount];
                if (!string.Equals(evt.EventType, candidateDraft.EventType, StringComparison.Ordinal)
                    || evt.Tick != candidateDraft.Tick
                    || !evt.Payload.Span.SequenceEqual(candidateDraft.Payload.Span))
                {
                    throw Conflict(
                        $"Plate event at recovery offset {observedCount} differs from the recomputed candidate draft batch.");
                }
            }

            actualDrafts.Add(canonicalDraft);
            observedCount++;
            terminal = evt;
            expectedPreviousHash = evt.Hash;
        }

        if (observedCount != prepared.Payload.DraftCount
            || expectedDrafts is { } expected && observedCount != expected.Count)
        {
            throw Conflict("Plate stream does not contain exactly the prepared deterministic draft batch.");
        }

        var actualDigest = RotationImportPayloadCodec.ComputeDraftDigest(actualDrafts);
        if (!actualDigest.AsSpan().SequenceEqual(prepared.Payload.DraftDigest.Span))
            throw Conflict("Plate batch ordered draft digest does not match the prepared digest.");

        if (terminal is null
            || terminal.Sequence != bound.PlateCursor.Sequence
            || terminal.Tick != bound.PlateCursor.Tick
            || !terminal.Hash.Span.SequenceEqual(bound.PlateCursor.EventHash.Span))
        {
            throw Conflict("Plate batch terminal event does not match the exact bound cursor.");
        }
    }

    private async Task VerifyPrefixThroughAsync(
        TruthStreamIdentity stream,
        StreamHead expectedHead,
        CancellationToken ct)
    {
        ReadOnlyMemory<byte> expectedPreviousHash = ReadOnlyMemory<byte>.Empty;
        long expectedSequence = 0;
        ITruthEvent? terminal = null;
        await foreach (var evt in _reader.ReadAsync(stream, 0, ct).ConfigureAwait(false))
        {
            if (evt.Sequence > expectedHead.Sequence)
                break;
            if (evt.StreamIdentity != stream || evt.Sequence != expectedSequence)
                throw Conflict($"Plate prefix identity/sequence mismatch before H0 at sequence {expectedSequence}.");
            if (!evt.PreviousHash.Span.SequenceEqual(expectedPreviousHash.Span))
                throw Conflict($"Plate prefix hash-chain break before H0 at sequence {expectedSequence}.");
            VerifyEventHash(evt, $"plate prefix event at sequence {expectedSequence}");
            terminal = evt;
            expectedPreviousHash = evt.Hash;
            expectedSequence++;
        }

        if (terminal is null
            || terminal.Sequence != expectedHead.Sequence
            || terminal.Tick != expectedHead.LastTick
            || !terminal.Hash.Span.SequenceEqual(expectedHead.Hash))
        {
            throw Conflict("Prepared prior plate head H0 does not match the verified genesis-to-H0 prefix.");
        }
    }

    private static void VerifyEventHash(ITruthEvent evt, string role)
    {
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
            throw Conflict($"Hash recomputation failed for {role}.");
    }

    private static RotationSourcePreparedPayload BuildPrepared(
        ImportCandidate candidate,
        StreamHead? priorPlateHead)
        => new(
            BuildContext(Array.Empty<TruthEventCursor>()),
            candidate.Request.SourceName,
            candidate.SourceDigest,
            ProducerReleaseDigest,
            candidate.Request.OnsetTick,
            candidate.PlateStream,
            candidate.DraftDigest,
            candidate.Drafts.Count,
            StreamHeadSnapshot.Copy(priorPlateHead));

    private static RotationSourceBoundPayload BuildBound(
        ImportCandidate candidate,
        TruthEventCursor preparedCursor,
        TruthEventCursor plateCursor)
        => new(
            BuildContext(new[] { preparedCursor, plateCursor }),
            candidate.Request.SourceName,
            candidate.SourceDigest,
            ProducerReleaseDigest,
            candidate.Request.OnsetTick,
            preparedCursor,
            candidate.PlateStream,
            plateCursor);

    private static WorldEventContextV1 BuildContext(IReadOnlyList<TruthEventCursor> inputCursors)
        => new(
            SchemaVersion: 1,
            ProducerComponentId,
            ProducerSemanticVersion,
            ProducerReleaseDigest,
            ModelId,
            ModelVersion,
            ConfigurationDigest,
            inputCursors,
            Array.Empty<ArtifactReference>(),
            Units,
            ReferenceFrame);

    private static void EnsureSameCandidate(
        ImportCandidate candidate,
        RotationSourcePreparedPayload prepared)
    {
        if (prepared.TargetPlateStream != candidate.PlateStream
            || prepared.OnsetTick != candidate.Request.OnsetTick
            || prepared.DraftCount != candidate.Drafts.Count
            || !prepared.SourceDigest.Span.SequenceEqual(candidate.SourceDigest)
            || !prepared.DraftDigest.Span.SequenceEqual(candidate.DraftDigest))
        {
            throw Conflict("A different rotation source is already prepared or bound for this world branch.");
        }
    }

    private static void EnsureBoundMatchesPrepared(
        PreparedControlState prepared,
        RotationSourceBoundPayload bound)
    {
        if (bound.PlateStream != prepared.Payload.TargetPlateStream
            || bound.PlateCursor.Stream != prepared.Payload.TargetPlateStream
            || !string.Equals(bound.SourceName, prepared.Payload.SourceName, StringComparison.Ordinal)
            || bound.OnsetTick != prepared.Payload.OnsetTick
            || !bound.SourceDigest.Span.SequenceEqual(prepared.Payload.SourceDigest.Span)
            || !bound.ParserReleaseDigest.Span.SequenceEqual(prepared.Payload.ParserReleaseDigest.Span)
            || !CursorsEqual(bound.PreparedCursor, prepared.Cursor))
        {
            throw Conflict("Bound rotation source does not match its prepared import or requested source.");
        }

        var cursors = bound.Context.InputEventCursors;
        if (cursors.Count != 2
            || !CursorsEqual(cursors[0], prepared.Cursor)
            || !CursorsEqual(cursors[1], bound.PlateCursor))
        {
            throw Conflict("Bound context input cursors must be exactly [prepared cursor, plate cursor].");
        }

        if (prepared.Payload.Context.InputEventCursors.Count != 0
            || !prepared.Payload.ParserReleaseDigest.Span.SequenceEqual(
                prepared.Payload.Context.ProducerReleaseDigest.Span)
            || !bound.ParserReleaseDigest.Span.SequenceEqual(bound.Context.ProducerReleaseDigest.Span)
            || !ContextMetadataEqual(prepared.Payload.Context, bound.Context))
        {
            throw Conflict("Prepared or bound world-event context is not the canonical import context.");
        }
    }

    private static bool ContextMetadataEqual(WorldEventContextV1 left, WorldEventContextV1 right)
    {
        if (left.SchemaVersion != 1
            || right.SchemaVersion != 1
            || !string.Equals(left.ProducerComponentId, right.ProducerComponentId, StringComparison.Ordinal)
            || !string.Equals(left.ProducerSemanticVersion, right.ProducerSemanticVersion, StringComparison.Ordinal)
            || !left.ProducerReleaseDigest.Span.SequenceEqual(right.ProducerReleaseDigest.Span)
            || !string.Equals(left.ModelId, right.ModelId, StringComparison.Ordinal)
            || !string.Equals(left.ModelVersion, right.ModelVersion, StringComparison.Ordinal)
            || !left.CanonicalConfigurationDigest.Span.SequenceEqual(right.CanonicalConfigurationDigest.Span)
            || !string.Equals(left.Units, right.Units, StringComparison.Ordinal)
            || !string.Equals(left.CoordinateReferenceFrameId, right.CoordinateReferenceFrameId, StringComparison.Ordinal)
            || left.ArtifactReferences.Count != right.ArtifactReferences.Count)
        {
            return false;
        }

        for (var index = 0; index < left.ArtifactReferences.Count; index++)
        {
            var leftArtifact = left.ArtifactReferences[index];
            var rightArtifact = right.ArtifactReferences[index];
            if (!string.Equals(leftArtifact.Algorithm, rightArtifact.Algorithm, StringComparison.Ordinal)
                || !leftArtifact.Digest.Span.SequenceEqual(rightArtifact.Digest.Span)
                || leftArtifact.ByteLength != rightArtifact.ByteLength
                || !string.Equals(leftArtifact.MediaType, rightArtifact.MediaType, StringComparison.Ordinal)
                || !string.Equals(leftArtifact.SchemaId, rightArtifact.SchemaId, StringComparison.Ordinal)
                || leftArtifact.SchemaVersion != rightArtifact.SchemaVersion
                || !string.Equals(leftArtifact.Compression, rightArtifact.Compression, StringComparison.Ordinal)
                || !string.Equals(leftArtifact.DimensionalShape, rightArtifact.DimensionalShape, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static TruthEventDraft Draft(
        TruthStreamIdentity stream,
        string eventType,
        byte[] payload,
        CanonicalTick tick)
        => new(stream, eventType, payload, tick);

    private static TruthEventCursor ToCursor(TruthStreamIdentity stream, StreamHead head)
        => new(stream, head.Sequence, head.Hash.ToArray(), head.LastTick);

    private static TruthEventCursor ToCursor(TruthStreamIdentity stream, ITruthEvent evt)
        => new(stream, evt.Sequence, evt.Hash.ToArray(), evt.Tick);

    private static void EnsureControlEnvelopeTick(
        TruthEventCursor cursor,
        CanonicalTick payloadOnsetTick,
        string transition)
    {
        if (cursor.Tick != payloadOnsetTick)
        {
            throw Conflict(
                $"The {transition} control-event envelope tick must equal its payload onset tick.");
        }
    }

    private static StreamHead ToHead(TruthEventCursor cursor)
        => new(cursor.Sequence, cursor.EventHash.ToArray(), cursor.Tick);

    private static bool HeadsEqual(StreamHead? left, StreamHead? right)
    {
        if (left is null || right is null)
            return left is null && right is null;
        return left.Value.Sequence == right.Value.Sequence
            && left.Value.LastTick == right.Value.LastTick
            && left.Value.Hash.AsSpan().SequenceEqual(right.Value.Hash);
    }

    private static bool CursorsEqual(TruthEventCursor left, TruthEventCursor right)
        => left.Stream == right.Stream
            && left.Sequence == right.Sequence
            && left.Tick == right.Tick
            && left.EventHash.Span.SequenceEqual(right.EventHash.Span);

    private static InvalidOperationException Conflict(string detail)
        => new($"Rotation import conflict: {detail} A new branch required before importing this source.");

    private sealed record ImportCandidate(
        RotationImportRequest Request,
        TruthStreamIdentity PlateStream,
        TruthStreamIdentity ControlStream,
        IReadOnlyList<PlateRotationDraft> Drafts,
        byte[] SourceDigest,
        byte[] DraftDigest);

    private sealed record PreparedControlState(
        RotationSourcePreparedPayload Payload,
        TruthEventCursor Cursor);

    private sealed record BoundControlState(
        RotationSourceBoundPayload Payload,
        TruthEventCursor Cursor,
        PreparedControlState Prepared);

    private sealed record ControlState(
        StreamHead? Head,
        PreparedControlState? Prepared,
        BoundControlState? Bound);

    private sealed class ControlStateChangedException : Exception;
}
