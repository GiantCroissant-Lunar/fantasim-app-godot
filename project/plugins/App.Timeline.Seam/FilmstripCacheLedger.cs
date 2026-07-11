using System.Collections.Generic;

namespace FantaSim.App.Timeline.Seam;

/// <summary>
/// Insertion-ordered FIFO ledger for the filmstrip texture cache: tracks keys up to a cap and
/// yields the evictee when full. Godot-free bookkeeping only — the owner maps keys to textures.
/// Split from TimelineFace 2026-07-11 (vault/plans/2026-07-11-timelineface-split-plan.md).
/// </summary>
internal sealed class FilmstripCacheLedger
{
    private readonly int _cap;
    private readonly LinkedList<string> _order = new();
    private readonly HashSet<string> _keys = new();

    public FilmstripCacheLedger(int cap)
    {
        _cap = cap;
    }

    public int Count => _order.Count;

    public bool Contains(string key) => _keys.Contains(key);

    /// <summary>
    /// Records a key; returns the evicted oldest key when the cap is exceeded, else null.
    /// Duplicate keys return null and do NOT change their eviction position (matches the
    /// current TimelineFace semantics: the Queue.Enqueue only fires on isNewKey).
    /// </summary>
    public string? Record(string key)
    {
        if (_keys.Contains(key))
            return null;

        string? evicted = null;
        if (_order.Count >= _cap && _order.Count > 0)
        {
            evicted = _order.First!.Value;
            _order.RemoveFirst();
            _keys.Remove(evicted);
        }

        _order.AddLast(key);
        _keys.Add(key);
        return evicted;
    }

    public IReadOnlyCollection<string> Keys => _order;

    public void Clear()
    {
        _order.Clear();
        _keys.Clear();
    }
}