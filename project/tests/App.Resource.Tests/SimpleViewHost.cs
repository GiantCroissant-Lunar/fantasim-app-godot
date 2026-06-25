#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.Ui;
using FantaSim.App.Ui.Providers;

namespace App.Resource.Tests;

// Minimal in-memory IViewHost used only by the S1 ALC-collection tests. It holds the
// bundle-typed IViewSource directly so we can prove a managed reference pins a collectible
// ALC. No Godot, no CallDeferred, no registry resolution -- Mount(viewId, source) is the
// test-only seam that hands the host the already-constructed real source.
internal sealed class SimpleViewHost : IViewHost
{
    private readonly Dictionary<string, IViewSource> _active = new();

    // Test helper: hand the host a real, already-constructed IViewSource.
    public void Mount(string viewId, IViewSource source)
    {
        _active[viewId] = source;
    }

    // Contract member: no-op in this stand-in (prod resolves the source from a registry).
    public void Mount(string viewId)
    {
    }

    public void Unmount(string viewId)
    {
        _active.Remove(viewId);
    }

    public Task<bool> UnmountAndWaitAsync(string viewId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_active.Remove(viewId));
    }
}