using System;
using System.Collections.Generic;

namespace FantaSim.App.Camera;

/// <summary>
/// Keyed pending-configuration state for seams whose target resources appear asynchronously.
/// </summary>
public sealed class PendingConfigurationById<TRequest>
{
    private readonly Dictionary<string, TRequest> _pending = new(StringComparer.Ordinal);

    public int Count => _pending.Count;

    public bool HasPending(string id) => _pending.ContainsKey(id);

    public bool ApplyOrPend(
        string id,
        TRequest request,
        Func<string, bool> isReady,
        Action<string, TRequest> apply)
    {
        if (isReady is null) throw new ArgumentNullException(nameof(isReady));
        if (apply is null) throw new ArgumentNullException(nameof(apply));

        if (!isReady(id))
        {
            _pending[id] = request;
            return false;
        }

        _pending.Remove(id);
        apply(id, request);
        return true;
    }

    public bool TryApplyPending(
        string id,
        Func<string, bool> isReady,
        Action<string, TRequest> apply)
    {
        if (isReady is null) throw new ArgumentNullException(nameof(isReady));
        if (apply is null) throw new ArgumentNullException(nameof(apply));

        if (!_pending.TryGetValue(id, out var request))
            return false;

        if (!isReady(id))
            return false;

        apply(id, request);
        _pending.Remove(id);
        return true;
    }

    public void Remove(string id) => _pending.Remove(id);
}
