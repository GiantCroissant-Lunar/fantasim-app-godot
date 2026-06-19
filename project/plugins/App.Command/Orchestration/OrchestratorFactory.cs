using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;

namespace FantaSim.App.Command.Orchestration;

/// <summary>
/// Selects the active <see cref="IWorldOrchestration"/>. The default is the local-first
/// <see cref="LocalOrchestrator"/>; the native <see cref="IiiBridgeOrchestrator"/> is
/// selectable explicitly for diagnostic/future wiring but is not built in this slice.
/// </summary>
internal static class OrchestratorFactory
{
    public enum Mode
    {
        /// <summary>In-process orchestration over App.World/App.Ecs. Default.</summary>
        Local,
        /// <summary>Deferred native iii bridge stub. Throws <see cref="NotImplementedException"/>.</summary>
        IiiBridge,
    }

    public static IWorldOrchestration Create(IRegistry registry, ILoggerFactory loggerFactory, Mode mode = Mode.Local)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        return mode switch
        {
            Mode.Local => new LocalOrchestrator(registry, loggerFactory),
            Mode.IiiBridge => new IiiBridgeOrchestrator(loggerFactory),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown orchestrator mode."),
        };
    }
}