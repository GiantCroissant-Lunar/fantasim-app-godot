namespace FantaSim.App.Ui.Providers;

public interface IViewHost
{
    void Mount(string viewId);

    void Unmount(string viewId);

    /// <summary>
    /// Unmounts the view on the Godot main thread and completes only after the unmount has actually
    /// run -- the <c>ViewRenderer</c> is disposed and its bundle-typed <c>IViewSource</c> reference is
    /// dropped. Unlike the fire-and-forget <see cref="Unmount"/>, an awaiting caller is guaranteed the
    /// resident reference into a collectible bundle's ALC is gone before the ALC is unloaded.
    /// Returns true if a view was actually mounted for <paramref name="viewId"/> (and is now removed).
    /// </summary>
    Task<bool> UnmountAndWaitAsync(string viewId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronously unmounts the view. MUST be called on the Godot main thread (the caller
    /// guarantees it). Drops the bundle-typed <c>IViewSource</c> reference immediately (no
    /// deferral). Returns true if a view was actually mounted for <paramref name="viewId"/>
    /// (and is now removed). This is the main-thread unmount seam the reload handler uses so
    /// the rest of the reload stays on the main thread (no deferred-TCS thread hop).
    /// </summary>
    bool UnmountNow(string viewId);
}
