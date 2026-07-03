using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.Command;
using FantaSim.App.Command.Orchestration;
using FantaSim.App.NodeGraph;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FantaSim.App.Iii;

/// <summary>
/// The iii-axis <see cref="IIiiOrchestration"/> implementation. Owns a <see cref="GraphExecutor"/>
/// over the registered node-function providers and recognizes the iii command family
/// (pipeline.*, iii.*). App.Command.IService routes those verbs here; World verbs stay with
/// <c>LocalOrchestrator</c>.
/// </summary>
public sealed class IiiOrchestrator : IIiiOrchestration
{
    public static class WellKnownCommands
    {
        public const string RunTextTo3d = "pipeline.run_text_to_3d";
    }

    private readonly GraphExecutor _executor;
    private readonly ILogger _logger;

    public IiiOrchestrator(
        IEnumerable<INodeFunctionProvider> providers,
        ILoggerFactory? loggerFactory = null)
    {
        _executor = new GraphExecutor(providers);
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<IiiOrchestrator>();
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
            WellKnownCommands.RunTextTo3d => RunTextTo3dAsync(request, commandId, cancellationToken),
            _ => Task.FromResult(UnknownCommand(request, commandId)),
        };
    }

    public Task<CommandHealth> HealthAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new CommandHealth(true, 1));
    }

    private async Task<CommandResult> RunTextTo3dAsync(CommandRequest request, string commandId, CancellationToken ct)
    {
        try
        {
            var prompt = ExtractPrompt(request);
            var graph = Recipes.TextTo3dGraph.Build(prompt);
            var jobId = Guid.NewGuid().ToString("N")[..8];
            var shared = new JsonObject { ["job_id"] = jobId };
            var result = await _executor.ExecuteAsync(graph, shared, ct).ConfigureAwait(false);
            var glb = result["glb_path"]?.ToString() ?? "(none)";
            // Serialize, never interpolate: a provider path containing quotes/backslashes/newlines
            // must still produce valid JSON (2026-07-03 review fix).
            return new CommandResult(
                commandId,
                true,
                ResultJson: System.Text.Json.JsonSerializer.Serialize(new { glb_path = glb }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "pipeline.run_text_to_3d failed");
            return new CommandResult(commandId, false, Error: new CommandError(ex.GetType().Name, ex.Message));
        }
    }

    private static string ExtractPrompt(CommandRequest request)
        => string.IsNullOrWhiteSpace(request.PayloadJson)
            ? "a small red toy cube"
            : (JsonNode.Parse(request.PayloadJson)?["prompt"]?.ToString() ?? "a small red toy cube");

    private static CommandResult UnknownCommand(CommandRequest request, string commandId)
        => new(commandId, false, Error: new CommandError("unknown-command", $"Unknown iii command: {request.Command}"));
}
