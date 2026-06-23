using System.Text.Json.Nodes;
using BoomHud.Abstractions.Runtime;
using FantaSim.App.Command.Orchestration;
using FantaSim.App.Ui;
using Microsoft.Extensions.Logging;

namespace FantaSim.App.Ui;

public sealed class RuntimeStatusViewSource : IViewSource
{
    private readonly IWorldOrchestration _orchestration;
    private readonly ILogger _logger;

    public RuntimeStatusViewSource(IWorldOrchestration orchestration, ILogger logger)
    {
        _orchestration = orchestration ?? throw new ArgumentNullException(nameof(orchestration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ViewId => "runtime-status";

    public event Action? Changed;

    public void Refresh() => Changed?.Invoke();

    public RuntimeSurfaceDocument BuildDocument()
    {
        var runtimeText = GetRuntimeText();

        var model = new JsonObject
        {
            ["agentRuntime"] = new JsonObject
            {
                ["runtime"] = runtimeText,
                ["profile"] = "fantasim-godot",
                ["modelLabel"] = "glm-5.2 via Ollama Cloud / Zai",
            },
            ["summary"] = new JsonObject(),
            ["signals"] = new JsonObject(),
            ["projection"] = new JsonObject { ["enabled"] = false },
            ["projectedArchive"] = new JsonArray(),
            ["agentRuns"] = new JsonArray(),
            ["appEvents"] = new JsonArray(),
        };

        return new RuntimeSurfaceDocument
        {
            SurfaceId = ViewId,
            CatalogId = "basic",
            Root = new RuntimeComponentNode
            {
                Id = "root",
                Type = "surface",
                Children = Array.Empty<RuntimeComponentNode>(),
            },
            DataModel = model,
        };
    }

    public void Dispatch(string action, string? componentId)
    {
        _logger.LogInformation("Runtime status view action dispatched: {Action} ({ComponentId})", action, componentId);
    }

    private string GetRuntimeText()
    {
        try
        {
            var health = _orchestration.HealthAsync().GetAwaiter().GetResult();
            return health.Ok
                ? "local in-process orchestration (healthy)"
                : "local in-process orchestration (degraded)";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read orchestration health for runtime status.");
            return "local in-process orchestration (unknown)";
        }
    }
}