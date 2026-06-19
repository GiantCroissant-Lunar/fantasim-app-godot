using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FantaSim.App.Command.Clients;

/// <summary>
/// In-process <see cref="IClient"/> implementation: routes health/status/command calls to the
/// registered <see cref="IService"/>. Restored from prior-art App.Command pattern, trimmed to
/// the contract surface this slice needs.
/// </summary>
public sealed class InProcessClient : IClient
{
    private readonly IService _commands;
    private readonly ILogger _logger;

    public InProcessClient(IService commands, ILoggerFactory? loggerFactory = null)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<InProcessClient>();
    }

    public Task<CommandHealth> HealthAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new CommandHealth(true, _commands.Commands.Count));
    }

    public Task<CommandStatus> StatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new CommandStatus("fantasim", _commands.Commands));
    }

    public Task<CommandResult> CommandAsync(CommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _logger.LogInformation("Dispatching command {Command}", request.Command);
        return _commands.ExecuteAsync(request, cancellationToken);
    }
}