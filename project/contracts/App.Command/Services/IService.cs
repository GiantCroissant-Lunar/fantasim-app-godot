using ServiceArchi.Contracts;
using ServiceArchi.Contracts.Attributes;

namespace FantaSim.App.Command;

[ServiceContract]
public interface IService
{
    void Register(CommandDescriptor descriptor, CommandHandler handler);

    IReadOnlyList<CommandDescriptor> Commands { get; }

    Task<CommandResult> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken = default);
}