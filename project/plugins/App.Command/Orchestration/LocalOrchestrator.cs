using System.Globalization;
using System.Text.Json;
using FantaSim.App.World.Dto;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceArchi.Contracts;
using WorldService = FantaSim.App.World.IService;
using EcsService = FantaSim.App.Ecs.IService;

namespace FantaSim.App.Command.Orchestration;

/// <summary>
/// Local in-process <see cref="IWorldOrchestration"/> implementation. iii sits above Akka/ECS
/// here: it consumes the app-side <see cref="WorldService"/> and <see cref="EcsService"/>
/// contracts resolved from the <see cref="IRegistry"/> and triggers world generation followed
/// by an ECS update fan-out. No Hermes/Python and no native iii bridge is involved.
/// </summary>
/// <remarks>
/// The orchestrator is deliberately deterministic and single-threaded. It does not spawn
/// processes or rely on external clocks. <see cref="TriggerAsync"/> recognizes a trimmed set
/// of command ids (see <see cref="WellKnownCommands"/>); unknown commands return a typed
/// <see cref="CommandError"/> rather than throwing.
/// </remarks>
public sealed class LocalOrchestrator : IWorldOrchestration
{
    /// <summary>Trimmed command surface this orchestrator recognizes.</summary>
    public static class WellKnownCommands
    {
        public const string Generate = "world.generate";
        public const string Tick = "world.tick";
        public const string Refresh = "world.refresh";
    }

    private readonly IRegistry _registry;
    private readonly ILogger _logger;

    public LocalOrchestrator(IRegistry registry, ILoggerFactory loggerFactory)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<LocalOrchestrator>();
    }

    public Task<CommandResult> TriggerAsync(CommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Command);
        cancellationToken.ThrowIfCancellationRequested();

        var commandId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : request.CorrelationId;

        return request.Command switch
        {
            WellKnownCommands.Generate => TriggerGenerateAsync(request, commandId, cancellationToken),
            WellKnownCommands.Tick => TriggerTickAsync(request, commandId, cancellationToken),
            WellKnownCommands.Refresh => TriggerRefreshAsync(request, commandId, cancellationToken),
            _ => Task.FromResult(UnknownCommand(request, commandId)),
        };
    }

    public Task<CommandHealth> HealthAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var world = _registry.TryGet<WorldService>();
        var ecs = _registry.TryGet<EcsService>();
        var ok = world is not null && ecs is not null;
        var commands = 3; // generate + tick + refresh
        return Task.FromResult(new CommandHealth(ok, commands));
    }

    private Task<CommandResult> TriggerGenerateAsync(
        CommandRequest request, string commandId, CancellationToken cancellationToken)
    {
        var world = _registry.TryGet<WorldService>();
        if (world is null)
            return Task.FromResult(MissingService(request, commandId, "FantaSim.App.World.IService"));

        try
        {
            var parameters = ParseParameters(request);
            var genRequest = new WorldGenerationRequest(
                WorldId: ExtractWorldId(request) ?? "default",
                GenerationSpec: request.Command,
                Parameters: parameters);
            var result = world.RunGenerationAsync(genRequest);

            // After a successful generation, fan out an ECS update so the field/reducer
            // systems pick up the new generation. A zero-delta tick is intentional: iii
            // triggers worlds/recipes, it does not drive per-tick simulation math.
            if (result.Success)
            {
                var ecs = _registry.TryGet<EcsService>();
                ecs?.UpdateAll(0f);
            }

            return Task.FromResult(new CommandResult(
                Id: commandId,
                Ok: result.Success,
                ResultJson: SerializeGeneration(result)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "world.generate failed for command {CommandId}.", commandId);
            return Task.FromResult(new CommandResult(commandId, false, Error: ToError(ex)));
        }
    }

    private Task<CommandResult> TriggerTickAsync(
        CommandRequest request, string commandId, CancellationToken cancellationToken)
    {
        var ecs = _registry.TryGet<EcsService>();
        if (ecs is null)
            return Task.FromResult(MissingService(request, commandId, "FantaSim.App.Ecs.IService"));

        try
        {
            var tick = ExtractTickRequest(request);
            ecs.UpdateAll(tick.Delta);
            return Task.FromResult(new CommandResult(commandId, true, ResultJson: SerializeTick(tick)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "world.tick failed for command {CommandId}.", commandId);
            return Task.FromResult(new CommandResult(commandId, false, Error: ToError(ex)));
        }
    }

    private Task<CommandResult> TriggerRefreshAsync(
        CommandRequest request, string commandId, CancellationToken cancellationToken)
    {
        var world = _registry.TryGet<WorldService>();
        if (world is null)
            return Task.FromResult(MissingService(request, commandId, "FantaSim.App.World.IService"));

        try
        {
            var overview = world.GetOverviewAsync();
            return Task.FromResult(new CommandResult(
                commandId,
                true,
                ResultJson: SerializeOverview(overview)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "world.refresh failed for command {CommandId}.", commandId);
            return Task.FromResult(new CommandResult(commandId, false, Error: ToError(ex)));
        }
    }

    private static CommandResult UnknownCommand(CommandRequest request, string commandId)
        => new(commandId, false, Error: new CommandError("unknown-command", $"Unknown command: {request.Command}"));

    private static CommandResult MissingService(CommandRequest request, string commandId, string serviceName)
        => new(commandId, false, Error: new CommandError(
            "missing-service",
            $"Service '{serviceName}' is not registered.",
            Detail: $"Command '{request.Command}' requires {serviceName}."));

    private static CommandError ToError(Exception ex)
        => new(ex.GetType().Name, ex.Message);

    private static Dictionary<string, object> ParseParameters(CommandRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PayloadJson))
            return new Dictionary<string, object>(StringComparer.Ordinal);
        try
        {
            var doc = JsonDocument.Parse(request.PayloadJson);
            var dict = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var prop in doc.RootElement.EnumerateObject())
                dict[prop.Name] = prop.Value.ToString();
            return dict;
        }
        catch
        {
            return new Dictionary<string, object>(StringComparer.Ordinal);
        }
    }

    private static string? ExtractWorldId(CommandRequest request)
        => string.IsNullOrWhiteSpace(request.ActorId) ? null : request.ActorId;

    private static TickRequest ExtractTickRequest(CommandRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PayloadJson))
            return new TickRequest(0f, null);

        if (float.TryParse(
                request.PayloadJson,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var legacyDelta))
        {
            return new TickRequest(legacyDelta, null);
        }

        using var doc = JsonDocument.Parse(request.PayloadJson);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Payload must be a numeric delta or a JSON object with 'tick', 'canonicalTick', or 'delta'.");

        if (root.TryGetProperty("tick", out var tick))
            return new TickRequest(0f, ReadCanonicalTick(tick, "tick"));
        if (root.TryGetProperty("canonicalTick", out var canonicalTick))
            return new TickRequest(0f, ReadCanonicalTick(canonicalTick, "canonicalTick"));
        if (root.TryGetProperty("delta", out var delta))
            return new TickRequest(ReadDelta(delta, "delta"), null);

        throw new ArgumentException("Missing 'tick', 'canonicalTick', or 'delta'.");
    }

    private static long ReadCanonicalTick(JsonElement value, string fieldName)
    {
        long tick;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out tick))
            return NonNegativeTick(tick, fieldName);
        if (value.ValueKind == JsonValueKind.String
            && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out tick))
            return NonNegativeTick(tick, fieldName);

        throw new ArgumentException($"Invalid '{fieldName}' value; expected an integer canonical tick.");
    }

    private static long NonNegativeTick(long tick, string fieldName)
    {
        if (tick < 0)
            throw new ArgumentOutOfRangeException(fieldName, "Canonical ticks must be non-negative.");
        return tick;
    }

    private static float ReadDelta(JsonElement value, string fieldName)
    {
        if (value.ValueKind == JsonValueKind.Number)
            return value.GetSingle();
        if (value.ValueKind == JsonValueKind.String
            && float.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return parsed;

        throw new ArgumentException($"Invalid '{fieldName}' value; expected a numeric delta.");
    }

    private static string SerializeGeneration(WorldGenerationResult result)
        => $"{{\"success\":{(result.Success ? "true" : "false")},\"message\":\"{Escape(result.Message)}\",\"worldId\":\"{Escape(result.ResultWorldId)}\"}}";

    private static string SerializeOverview(WorldOverview overview)
        => $"{{\"worldId\":\"{Escape(overview.WorldId)}\",\"name\":\"{Escape(overview.Name)}\",\"entityCount\":{overview.EntityCount},\"fieldCount\":{overview.FieldCount},\"isDirty\":{(overview.IsDirty ? "true" : "false")}}}";

    private static string SerializeTick(TickRequest request)
    {
        var delta = request.Delta.ToString("R", CultureInfo.InvariantCulture);
        return request.CanonicalTick is { } tick
            ? $"{{\"tick\":{tick},\"canonicalTick\":{tick},\"delta\":{delta}}}"
            : $"{{\"delta\":{delta}}}";
    }

    private static string Escape(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private readonly record struct TickRequest(float Delta, long? CanonicalTick);
}
