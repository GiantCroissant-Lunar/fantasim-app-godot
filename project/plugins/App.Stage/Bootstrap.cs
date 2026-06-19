using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts; // IRegistry

namespace FantaSim.App.Stage;

/// <summary>
/// App.Stage's service setup. Constructor-injected with the SHARED kernel — the <see cref="IRegistry"/>
/// forwarded from the dynamic parent by <see cref="StageActivator"/> — and the logger factory.
/// <see cref="RunAsync"/> starts the stage tier; it logs the kernel registry's identity hash so a
/// reader can confirm (same hash as the app boot) that the tier shares the one app kernel across the
/// collectible ALC boundary.
/// </summary>
public sealed class Bootstrap
{
    private readonly IRegistry _kernel;
    private readonly ILogger _log;

    public Bootstrap(IRegistry kernel, ILoggerFactory loggerFactory)
    {
        _kernel = kernel;
        _log = loggerFactory.CreateLogger("Stage.Bootstrap");
    }

    public Task RunAsync(CancellationToken cancellationToken = default)
    {
        _log.LogInformation(
            "Stage tier active — sharing the app kernel registry #{Kernel}.",
            RuntimeHelpers.GetHashCode(_kernel));
        return Task.CompletedTask;
    }
}
