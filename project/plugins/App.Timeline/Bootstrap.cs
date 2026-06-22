using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts; // IRegistry

namespace FantaSim.App.Timeline;

/// <summary>
/// App.Timeline's service setup (mirrors App.Assist's Bootstrap). Constructor-injected with the
/// SHARED kernel — the <see cref="IRegistry"/> forwarded from the dynamic parent by
/// <see cref="TimelineActivator"/> — and the logger factory. <see cref="RunAsync"/> logs the
/// kernel registry's identity hash so a reader can confirm the shared kernel flows across the
/// collectible ALC boundary.
/// </summary>
public sealed class Bootstrap
{
    private readonly IRegistry _kernel;
    private readonly ILogger _log;

    public Bootstrap(IRegistry kernel, ILoggerFactory loggerFactory)
    {
        _kernel = kernel;
        _log = loggerFactory.CreateLogger("Timeline.Bootstrap");
    }

    public Task RunAsync(CancellationToken cancellationToken = default)
    {
        _log.LogInformation(
            "Timeline hud-view tier active — sharing the app kernel registry #{Kernel}.",
            RuntimeHelpers.GetHashCode(_kernel));
        return Task.CompletedTask;
    }
}
