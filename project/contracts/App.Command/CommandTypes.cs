namespace FantaSim.App.Command;

public sealed record CommandDescriptor(
    string Id,
    string Title,
    string Description,
    string Category,
    string? ParamsSchema = null);

public sealed record CommandError(string Type, string Message, string? Detail = null);

public sealed record CommandRequest(
    string Command,
    string? PayloadJson = null,
    string? CorrelationId = null,
    string? ActorKind = null,
    string? ActorId = null);

public sealed record CommandResult(
    string Id,
    bool Ok,
    string? ResultJson = null,
    CommandError? Error = null);

public sealed record CommandHealth(bool Ok, int Commands);

public sealed record CommandStatus(string App, IReadOnlyList<CommandDescriptor> Commands);

public delegate Task<string?> CommandHandler(string? payloadJson, CancellationToken cancellationToken);
