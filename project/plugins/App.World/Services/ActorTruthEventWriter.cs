using Akka.Actor;
using FantaSim.World.TruthStream;
using TimeDete.Time.Primitives;

namespace FantaSim.App.World.Services;

internal sealed class ActorTruthEventWriter : ITruthEventWriter
{
    private static readonly TimeSpan DefaultAskTimeout = TimeSpan.FromSeconds(5);

    private readonly IActorRef _actor;
    private readonly TimeSpan _askTimeout;
    private int _disposed;

    private ActorTruthEventWriter(IActorRef actor, TimeSpan askTimeout)
    {
        _actor = actor;
        _askTimeout = askTimeout;
    }

    public static ActorTruthEventWriter Start(
        ActorSystem actorSystem,
        ITruthEventStore store,
        string actorName = "world-truth-writer",
        TimeSpan? askTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(actorSystem);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorName);

        var timeout = askTimeout ?? DefaultAskTimeout;
        var actor = actorSystem.ActorOf(
            Props.Create(() => new TruthEventWriterActor(store)),
            actorName);
        return new ActorTruthEventWriter(actor, timeout);
    }

    public Task<StreamHead> AppendAsync(
        TruthStreamIdentity stream,
        IReadOnlyList<ITruthEventDraft> drafts,
        CancellationToken ct = default)
        => AskAsync<StreamHead>(AppendTruthEvents.From(stream, drafts), ct);

    public Task<StreamHead> AppendIfHeadAsync(
        TruthStreamIdentity stream,
        IReadOnlyList<ITruthEventDraft> drafts,
        StreamHead? expectedHead,
        CancellationToken ct = default)
        => AskAsync<StreamHead>(CasAppendTruthEvents.From(stream, drafts, expectedHead), ct);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _actor.GracefulStop(_askTimeout).GetAwaiter().GetResult();
    }

    private async Task<T> AskAsync<T>(object message, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await _actor.Ask<T>(message, _askTimeout).WaitAsync(ct).ConfigureAwait(false);
    }
}

internal sealed class TruthEventWriterActor : ReceiveActor
{
    private readonly ITruthEventStore _store;

    public TruthEventWriterActor(ITruthEventStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));

        ReceiveAsync<AppendTruthEvents>(message =>
            ReplyAsync(() => _store.AppendAsync(message.Stream, message.Drafts)));
        ReceiveAsync<CasAppendTruthEvents>(message =>
            ReplyAsync(() => _store.AppendIfHeadAsync(message.Stream, message.Drafts, message.ExpectedHead)));
    }

    private async Task ReplyAsync<T>(Func<Task<T>> operation)
    {
        var replyTo = Sender;
        var self = Self;
        try
        {
            replyTo.Tell(await operation().ConfigureAwait(false), self);
        }
        catch (Exception ex)
        {
            replyTo.Tell(new Status.Failure(ex), self);
        }
    }
}

internal sealed record AppendTruthEvents(
    TruthStreamIdentity Stream,
    IReadOnlyList<ITruthEventDraft> Drafts)
{
    public static AppendTruthEvents From(
        TruthStreamIdentity stream,
        IReadOnlyList<ITruthEventDraft> drafts)
    {
        ArgumentNullException.ThrowIfNull(drafts);
        return new AppendTruthEvents(
            stream,
            TruthEventDraftSnapshot.FromMany(drafts));
    }
}

internal sealed record CasAppendTruthEvents(
    TruthStreamIdentity Stream,
    IReadOnlyList<ITruthEventDraft> Drafts,
    StreamHead? ExpectedHead)
{
    public static CasAppendTruthEvents From(
        TruthStreamIdentity stream,
        IReadOnlyList<ITruthEventDraft> drafts,
        StreamHead? expectedHead)
    {
        ArgumentNullException.ThrowIfNull(drafts);
        return new CasAppendTruthEvents(
            stream,
            TruthEventDraftSnapshot.FromMany(drafts),
            StreamHeadSnapshot.Copy(expectedHead));
    }
}

internal sealed record TruthEventDraftSnapshot(
    TruthStreamIdentity Stream,
    string EventType,
    byte[] PayloadBytes,
    CanonicalTick Tick) : ITruthEventDraft
{
    public ReadOnlyMemory<byte> Payload => PayloadBytes;

    public static TruthEventDraftSnapshot From(ITruthEventDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return new TruthEventDraftSnapshot(
            draft.Stream,
            draft.EventType,
            draft.Payload.ToArray(),
            draft.Tick);
    }

    public static TruthEventDraftSnapshot[] FromMany(IReadOnlyList<ITruthEventDraft> drafts)
    {
        ArgumentNullException.ThrowIfNull(drafts);
        return drafts.Select(From).ToArray();
    }
}

internal static class StreamHeadSnapshot
{
    public static StreamHead? Copy(StreamHead? head)
        => head is { } value
            ? new StreamHead(value.Sequence, value.Hash.ToArray(), value.LastTick)
            : null;
}
