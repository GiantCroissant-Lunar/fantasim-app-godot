using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FantaSim.App.Command.Orchestration;

/// <summary>
/// Deferred <see cref="IWorldOrchestration"/> stub that the native iii bridge (Rust/Hermes)
/// would replace. Every method throws <see cref="NotImplementedException"/> with a
/// documented message so callers fail fast and loudly instead of silently degrading.
/// </summary>
/// <remarks>
/// This stub exists to make the seam explicit: the local-first <see cref="LocalOrchestrator"/>
/// is the runtime used by the exported app today; the native iii bridge (Rust + Hermes/Python)
/// is a later slice and is intentionally not built here. No native files, no Hermes/Python
/// spawning, no App.Agent/App.Stage references.
/// </remarks>
public sealed class IiiBridgeOrchestrator : IWorldOrchestration
{
    private const string NotBuiltMessage =
        "The native iii bridge (Rust/Hermes) is not built in this slice. " +
        "Use LocalOrchestrator for in-process orchestration; this stub exists only to " +
        "make the IWorldOrchestration seam explicit and deferred.";

    private readonly ILogger _logger;

    public IiiBridgeOrchestrator(ILoggerFactory? loggerFactory = null)
    {
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<IiiBridgeOrchestrator>();
    }

    public Task<CommandResult> TriggerAsync(CommandRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("IiiBridgeOrchestrator.TriggerAsync invoked: native iii bridge is not built. command={Command}", request?.Command);
        throw new NotImplementedException(NotBuiltMessage);
    }

    public Task<CommandHealth> HealthAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("IiiBridgeOrchestrator.HealthAsync invoked: native iii bridge is not built.");
        throw new NotImplementedException(NotBuiltMessage);
    }
}