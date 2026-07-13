using FantaSim.World.TruthStream;

namespace FantaSim.App.World.Services;

internal interface ITruthEventWriter : IDisposable
{
    Task<StreamHead> AppendAsync(
        TruthStreamIdentity stream,
        IReadOnlyList<ITruthEventDraft> drafts,
        CancellationToken ct = default);

    Task<StreamHead> AppendIfHeadAsync(
        TruthStreamIdentity stream,
        IReadOnlyList<ITruthEventDraft> drafts,
        StreamHead? expectedHead,
        CancellationToken ct = default);
}

internal sealed class DirectTruthEventWriter : ITruthEventWriter
{
    private readonly ITruthEventStore _store;

    public DirectTruthEventWriter(ITruthEventStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public Task<StreamHead> AppendAsync(
        TruthStreamIdentity stream,
        IReadOnlyList<ITruthEventDraft> drafts,
        CancellationToken ct = default)
        => _store.AppendAsync(stream, TruthEventDraftSnapshot.FromMany(drafts), ct);

    public Task<StreamHead> AppendIfHeadAsync(
        TruthStreamIdentity stream,
        IReadOnlyList<ITruthEventDraft> drafts,
        StreamHead? expectedHead,
        CancellationToken ct = default)
        => _store.AppendIfHeadAsync(
            stream,
            TruthEventDraftSnapshot.FromMany(drafts),
            StreamHeadSnapshot.Copy(expectedHead),
            ct);

    public void Dispose()
    {
    }
}
