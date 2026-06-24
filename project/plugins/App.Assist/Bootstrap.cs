using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts; // IRegistry

namespace FantaSim.App.Assist;

/// <summary>
/// App.Assist's service setup (mirrors App.Stage's Bootstrap). Constructor-injected with the SHARED
/// kernel — the <see cref="IRegistry"/> forwarded from the dynamic parent (stage) by AssistActivator —
/// and the logger factory. <see cref="RunAsync"/> logs the kernel registry's identity hash so a reader
/// can confirm (same hash as stage and the app boot) that assist shares the one kernel through its
/// parent chain, across two collectible ALCs.
/// </summary>
public sealed class Bootstrap
{
    private readonly IRegistry _kernel;
    private readonly ILogger _log;

    public Bootstrap(IRegistry kernel, ILoggerFactory loggerFactory)
    {
        _kernel = kernel;
        _log = loggerFactory.CreateLogger("Assist.Bootstrap");
    }

    public Task RunAsync(CancellationToken cancellationToken = default)
    {
        _log.LogInformation(
            "Assist tier active — sharing the app kernel registry #{Kernel}.",
            RuntimeHelpers.GetHashCode(_kernel));

        var smoke = new GpuSmokeChecks(_kernel, _log);
        _ = smoke.RunComputeSmokeAsync(cancellationToken);
        _ = smoke.RunShaderSmokeAsync(cancellationToken);

        return Task.CompletedTask;
    }
}
