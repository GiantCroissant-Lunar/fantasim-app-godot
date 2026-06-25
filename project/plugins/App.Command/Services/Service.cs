using FantaSim.App.Activity;
using FantaSim.App.Command.Orchestration;
using FantaSim.App.Command.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceArchi.Contracts;

namespace FantaSim.App.Command.Services;

/// <summary>
/// Trimmed T3 command service implementing <see cref="IService"/>. It owns a command-handler
/// registry and routes <see cref="CommandRequest"/> to the registered handler. A built-in
/// <c>world.orchestrate</c> command delegates to the active <see cref="IWorldOrchestration"/>
/// (resolved from the <see cref="IRegistry"/> or supplied at construction). No App.Agent,
/// App.Stage, or Hermes wiring lives here.
/// </summary>
public sealed class Service : IService, IDisposable
{
    internal const string OrchestrateCommandId = "world.orchestrate";

    private readonly IRegistry _registry;
    private readonly IMainThreadDispatcher _mainThread;
    private readonly ILogger _logger;
    private readonly Dictionary<string, (CommandDescriptor Descriptor, CommandHandler Handler)> _handlers = new(StringComparer.Ordinal);
    private readonly object _handlerGate = new();
    private readonly IWorldOrchestration _orchestration;
    private bool _disposed;

    public Service(
        IMainThreadDispatcher mainThread,
        IRegistry registry,
        ILoggerFactory loggerFactory,
        IWorldOrchestration? orchestration = null)
    {
        _mainThread = mainThread ?? throw new ArgumentNullException(nameof(mainThread));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<Service>();
        _orchestration = orchestration ?? new LocalOrchestrator(registry, loggerFactory ?? NullLoggerFactory.Instance);
        RegisterBuiltIns();
    }

    public IReadOnlyList<CommandDescriptor> Commands
    {
        get
        {
            lock (_handlerGate)
                return _handlers.Values
                    .Select(e => e.Descriptor)
                    .OrderBy(d => d.Id, StringComparer.Ordinal)
                    .ToArray();
        }
    }

    public void Register(CommandDescriptor descriptor, CommandHandler handler)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Id);

        lock (_handlerGate)
            _handlers[descriptor.Id] = (descriptor, handler);
    }

    public void Unregister(string commandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);

        lock (_handlerGate)
            _handlers.Remove(commandId);
    }

    public async Task<CommandResult> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Command);

        var commandId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : request.CorrelationId;

        (CommandDescriptor Descriptor, CommandHandler Handler) entry;
        lock (_handlerGate)
        {
            if (!_handlers.TryGetValue(request.Command, out entry))
            {
                var unknown = new CommandResult(
                    commandId,
                    false,
                    Error: new CommandError("unknown-command", $"Unknown command: {request.Command}"));
                RecordCommandResult(request, null, commandId, null, unknown);
                return unknown;
            }
        }

        var requestEntryId = RecordCommandRequest(request, entry.Descriptor, commandId);

        try
        {
            var resultJson = await _mainThread.InvokeAsync(
                () => entry.Handler(request.PayloadJson, cancellationToken), cancellationToken);

            var result = new CommandResult(commandId, true, ResultJson: resultJson);
            RecordCommandResult(request, entry.Descriptor, commandId, requestEntryId, result);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Command {Command} failed: {Message}", request.Command, ex.Message);
            var result = new CommandResult(commandId, false, Error: new CommandError(ex.GetType().Name, ex.Message));
            RecordCommandResult(request, entry.Descriptor, commandId, requestEntryId, result);
            return result;
        }
    }

    internal IWorldOrchestration Orchestration => _orchestration;

    private void RegisterBuiltIns()
    {
        Register(
            new CommandDescriptor(
                Id: OrchestrateCommandId,
                Title: "Orchestrate world",
                Description: "Delegates a command request to the active IWorldOrchestration (LocalOrchestrator by default).",
                Category: "orchestration"),
            async (payloadJson, ct) =>
            {
                var inner = ParseInnerRequest(payloadJson) ?? new CommandRequest(Command: "world.refresh");
                var result = await _orchestration.TriggerAsync(inner, ct);
                return System.Text.Json.JsonSerializer.Serialize(result);
            });
    }

    private static CommandRequest? ParseInnerRequest(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return null;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<CommandRequest>(payloadJson);
        }
        catch
        {
            return null;
        }
    }

    private string? RecordCommandRequest(CommandRequest request, CommandDescriptor descriptor, string correlationId)
    {
        var entryId = Guid.NewGuid().ToString("N");
        AppendActivity(new ActivityEntry(
            EntryId: entryId,
            Kind: ActivityEntryKind.DomainCommand,
            Timestamp: DateTimeOffset.UtcNow,
            Actor: new ActivityActor(
                string.IsNullOrWhiteSpace(request.ActorKind) ? "system" : request.ActorKind!,
                request.ActorId),
            Name: request.Command,
            Category: descriptor.Category,
            PayloadJson: request.PayloadJson,
            CorrelationId: correlationId,
            Outcome: "requested"));
        return entryId;
    }

    private void RecordCommandResult(
        CommandRequest request,
        CommandDescriptor? descriptor,
        string correlationId,
        string? causationId,
        CommandResult result)
    {
        AppendActivity(new ActivityEntry(
            EntryId: Guid.NewGuid().ToString("N"),
            Kind: ActivityEntryKind.CommandResult,
            Timestamp: DateTimeOffset.UtcNow,
            Actor: new ActivityActor("system", "command"),
            Name: request.Command + ".result",
            Category: descriptor?.Category ?? "command",
            PayloadJson: result.ResultJson,
            CausationId: causationId,
            CorrelationId: correlationId,
            Outcome: result.Ok ? "ok" : "failed",
            Error: result.Error?.Message));
    }

    private void AppendActivity(ActivityEntry entry)
    {
        try
        {
            _registry.TryGet<FantaSim.App.Activity.IService>()?.Append(entry);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Activity ledger unavailable while recording command activity.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_orchestration is IDisposable d) d.Dispose();
        _disposed = true;
    }
}
