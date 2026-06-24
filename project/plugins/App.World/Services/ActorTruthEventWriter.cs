#if USE_PROJECT_REFERENCES
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

    public async Task<StreamHead?> GetHeadAsync(TruthStreamIdentity stream, CancellationToken ct = default)
    {
        var result = await AskAsync<TruthHeadResult>(new GetTruthHead(stream), ct).ConfigureAwait(false);
        return result.Head;
    }

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

        Receive<AppendTruthEvents>(message =>
            Reply(() => _store.AppendAsync(message.Stream, message.Drafts).GetAwaiter().GetResult()));
        Receive<GetTruthHead>(message =>
            Reply(() => new TruthHeadResult(_store.GetHeadAsync(message.Stream).GetAwaiter().GetResult())));
    }

    private void Reply<T>(Func<T> operation)
    {
        try
        {
            Sender.Tell(operation(), Self);
        }
        catch (Exception ex)
        {
            Sender.Tell(new Status.Failure(ex), Self);
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
            drafts.Select(TruthEventDraftSnapshot.From).ToArray());
    }
}

internal sealed record GetTruthHead(TruthStreamIdentity Stream);

internal sealed record TruthHeadResult(StreamHead? Head);

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
}
#endif
