using System;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace FantaSim.App.Presentation.Tunnel;

/// <summary>
/// Completes tunnel lifecycle teardown on Godot's main thread before a resource watcher may
/// continue into provider unload. RuntimeChanging is synchronous, so the worker must wait for the
/// queued main-thread callback instead of leaving an outgoing-ALC closure racing the unload.
/// </summary>
internal static class TunnelRuntimeChangeThreadGate
{
    public static void Run(
        Func<bool> isMainThread,
        Action<Action> deferToMainThread,
        Action applyOnMainThread)
    {
        ArgumentNullException.ThrowIfNull(isMainThread);
        ArgumentNullException.ThrowIfNull(deferToMainThread);
        ArgumentNullException.ThrowIfNull(applyOnMainThread);

        if (isMainThread())
        {
            applyOnMainThread();
            return;
        }

        using var completed = new ManualResetEventSlim(initialState: false);
        ExceptionDispatchInfo? failure = null;
        deferToMainThread(() =>
        {
            try
            {
                applyOnMainThread();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                completed.Set();
            }
        });

        completed.Wait();
        failure?.Throw();
    }
}
