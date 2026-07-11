namespace FantaSim.App.Presentation;

internal readonly record struct ScrubApplySchedule(int Generation, long DueAtMs);

internal sealed class ScrubApplyScheduler
{
    private readonly long _restDelayMs;
    private long? _pendingTick;
    private long _dueAtMs;
    private int _generation;

    public ScrubApplyScheduler(long restDelayMs)
    {
        if (restDelayMs < 0L)
            throw new ArgumentOutOfRangeException(nameof(restDelayMs));

        _restDelayMs = restDelayMs;
    }

    public bool HasPending => _pendingTick is not null;

    internal long RestDelayMs => _restDelayMs;

    public ScrubApplySchedule? RecordPreview(long tick, bool heavyRequested, long nowMs)
    {
        if (!heavyRequested && _pendingTick is null)
            return null;

        _pendingTick = tick;
        _dueAtMs = nowMs + _restDelayMs;
        _generation++;
        return new ScrubApplySchedule(_generation, _dueAtMs);
    }

    public long? ConsumeDue(int generation, long nowMs)
    {
        if (_pendingTick is null || generation != _generation || nowMs < _dueAtMs)
            return null;

        var tick = _pendingTick.Value;
        Clear();
        return tick;
    }

    public long? ConsumeCommit(long tick)
    {
        if (_pendingTick is null)
            return null;

        Clear();
        return tick;
    }

    public void Clear()
    {
        _pendingTick = null;
        _dueAtMs = 0L;
        _generation++;
    }
}
