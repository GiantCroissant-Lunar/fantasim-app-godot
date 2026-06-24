#if USE_PROJECT_REFERENCES
using FantaSim.World.TruthStream;

namespace FantaSim.App.World.Services;

internal interface ITruthEventWriter : IDisposable
{
    Task<StreamHead> AppendAsync(
        TruthStreamIdentity stream,
        IReadOnlyList<ITruthEventDraft> drafts,
        CancellationToken ct = default);

    Task<StreamHead?> GetHeadAsync(
        TruthStreamIdentity stream,
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
        => _store.AppendAsync(stream, drafts, ct);

    public Task<StreamHead?> GetHeadAsync(TruthStreamIdentity stream, CancellationToken ct = default)
        => _store.GetHeadAsync(stream, ct);

    public void Dispose()
    {
    }
}
#endif
