using System;

namespace FantaSim.App.Camera;

/// <summary>
/// Poll-based bind-once state machine for resolving a resource that appears asynchronously on the
/// Godot main thread. Each <see cref="TryResolve"/> call asks the provider for the value; the first
/// non-null result triggers <see cref="OnBound"/> exactly once and the machine becomes inert.
/// Subsequent calls are no-ops (they do not re-invoke the provider).
/// </summary>
/// <remarks>
/// Extracted from <c>GlobeOrbitControls</c> so the retry/bind-once contract is unit-testable without
/// a Godot scene tree: the resident camera rig registers its <c>PhantomCameraHost</c> via a deferred
/// callable, so a mount-time fetch races the rig's deferred build. The controls poll the rig each
/// <c>_Process</c> frame until the host appears, then bind exactly once. That poll/bind shape lives
/// here (Godot-free, generic over <typeparamref name="T"/>); only the host-fetch + apply step touches
/// Godot, and that stays in the Seam.
/// </remarks>
public sealed class LazyBindOnce<T> where T : class
{
    /// <summary>The bind step, run at most once on the first non-null resolution.</summary>
    public Action<T> OnBound { get; }

    private T? _value;
    private bool _bound;

    public LazyBindOnce(Action<T> onBound)
    {
        OnBound = onBound ?? throw new ArgumentNullException(nameof(onBound));
    }

    /// <summary>True once a non-null value has been resolved and <see cref="OnBound"/> has run.</summary>
    public bool IsBound => _bound;

    /// <summary>The resolved value, or null while unbound.</summary>
    public T? Value => _value;

    /// <summary>
    /// Poll <paramref name="resolve"/>. Returns true once bound (idempotent after the first success).
    /// Returns false while the provider returns null. The bind callback runs at most once.
    /// </summary>
    public bool TryResolve(Func<T?> resolve)
    {
        if (_bound)
            return true;

        var value = resolve();
        if (value is null)
            return false;

        _value = value;
        _bound = true;
        OnBound(value);
        return true;
    }
}
