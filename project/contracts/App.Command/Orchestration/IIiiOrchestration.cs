using System.Threading;
using System.Threading.Tasks;

namespace FantaSim.App.Command.Orchestration;

/// <summary>
/// The iii-axis orchestration seam: orchestration that crosses the process/agent boundary
/// (dataflow DAGs over external capability workers, agent-driven commands). Peer to the
/// (dormant) <see cref="IWorldOrchestration"/>; both sit behind App.Command.IService, the router.
/// iii owns this seam; World owns the other. App.Command dispatches by command id.
/// </summary>
public interface IIiiOrchestration
{
    /// <summary>Trigger an iii-axis command (e.g. <c>pipeline.run_text_to_3d</c>).</summary>
    Task<CommandResult> TriggerAsync(CommandRequest request, CancellationToken cancellationToken = default);

    /// <summary>Health of the iii axis: bridge up? engine reachable?</summary>
    Task<CommandHealth> HealthAsync(CancellationToken cancellationToken = default);
}
