using System;
using System.Threading;
using System.Threading.Tasks;

namespace FantaSim.App.Command.Orchestration;

public interface IWorldOrchestration
{
    /// <summary>
    /// Triggers an orchestrated command sequence across the world runtime.
    /// </summary>
    Task<CommandResult> TriggerAsync(CommandRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports the health status of the world orchestration layer.
    /// </summary>
    Task<CommandHealth> HealthAsync(CancellationToken cancellationToken = default);
}
