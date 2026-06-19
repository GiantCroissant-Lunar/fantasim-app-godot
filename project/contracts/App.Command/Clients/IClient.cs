using ServiceArchi.Contracts;
using ServiceArchi.Contracts.Attributes;

namespace FantaSim.App.Command;

[ServiceContract]
public interface IClient
{
    Task<CommandHealth> HealthAsync(CancellationToken cancellationToken = default);

    Task<CommandStatus> StatusAsync(CancellationToken cancellationToken = default);

    Task<CommandResult> CommandAsync(CommandRequest request, CancellationToken cancellationToken = default);
}