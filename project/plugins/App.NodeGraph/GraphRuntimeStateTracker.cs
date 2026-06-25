using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace FantaSim.App.NodeGraph;

/// <summary>
/// In-memory provider-neutral runtime state for one graph view. It adapts the executor's
/// <see cref="RunContext"/> callbacks into snapshots that UI surfaces can bind without knowing
/// which provider, iii worker, or domain produced the values.
/// </summary>
public sealed class GraphRuntimeStateTracker : IGraphRuntimeStateSource
{
    private const int MaxJsonLength = 900;

    private readonly object _gate = new();
    private readonly Dictionary<string, GraphNodeRuntimeState> _nodeStates = new(StringComparer.Ordinal);
    private GraphRunState _runState = new("none", GraphRunStatus.Pending);

    public GraphRunState RunState
    {
        get { lock (_gate) return _runState; }
    }

    public IReadOnlyDictionary<string, GraphNodeRuntimeState> NodeStates
    {
        get
        {
            lock (_gate)
                return new Dictionary<string, GraphNodeRuntimeState>(_nodeStates, StringComparer.Ordinal);
        }
    }

    public event Action? RuntimeStateChanged;

    public RunContext CreateRunContext(string? runId = null)
    {
        var actualRunId = string.IsNullOrWhiteSpace(runId)
            ? Guid.NewGuid().ToString("N")
            : runId;

        return new RunContext
        {
            BeforeRun = (graph, ct) =>
            {
                StartRun(graph, actualRunId);
                return Task.CompletedTask;
            },
            BeforeNode = (node, payload, ct) =>
            {
                StartNode(node, payload);
                return Task.CompletedTask;
            },
            AfterNode = (node, payload, result, ct) =>
            {
                CompleteNode(node, result);
                return Task.CompletedTask;
            },
            AfterRun = (graph, ct) =>
            {
                CompleteRun();
                return Task.CompletedTask;
            },
            OnNodeFailed = (node, ex, ct) =>
            {
                FailNode(node, ex);
                return Task.CompletedTask;
            },
            OnRunFailed = (graph, ex, ct) =>
            {
                FailRun(ex);
                return Task.CompletedTask;
            },
        };
    }

    private void StartRun(GraphDocument graph, string runId)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            _nodeStates.Clear();
            foreach (var node in graph.Nodes)
            {
                _nodeStates[node.Id] = new GraphNodeRuntimeState(
                    node.Id,
                    GraphNodeRuntimeStatus.Pending,
                    Progress: 0);
            }

            _runState = new GraphRunState(runId, GraphRunStatus.Running, StartedAt: now);
        }

        Publish();
    }

    private void StartNode(GraphNode node, JsonObject payload)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            _nodeStates.TryGetValue(node.Id, out var previous);
            _nodeStates[node.Id] = new GraphNodeRuntimeState(
                node.Id,
                GraphNodeRuntimeStatus.Running,
                StartedAt: previous?.StartedAt ?? now,
                InputsJson: CompactJson(payload),
                Progress: 0);
        }

        Publish();
    }

    private void CompleteNode(GraphNode node, JsonObject result)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            _nodeStates.TryGetValue(node.Id, out var previous);
            _nodeStates[node.Id] = new GraphNodeRuntimeState(
                node.Id,
                GraphNodeRuntimeStatus.Completed,
                StartedAt: previous?.StartedAt,
                EndedAt: now,
                InputsJson: previous?.InputsJson,
                OutputsJson: CompactJson(result),
                ArtifactsJson: ExtractArtifactsJson(result),
                Progress: 1);
        }

        Publish();
    }

    private void FailNode(GraphNode node, Exception ex)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            _nodeStates.TryGetValue(node.Id, out var previous);
            _nodeStates[node.Id] = new GraphNodeRuntimeState(
                node.Id,
                GraphNodeRuntimeStatus.Failed,
                StartedAt: previous?.StartedAt,
                EndedAt: now,
                InputsJson: previous?.InputsJson,
                LogsJson: ex.Message,
                Progress: previous?.Progress ?? 0);
        }

        Publish();
    }

    private void CompleteRun()
    {
        lock (_gate)
        {
            _runState = _runState with
            {
                Status = GraphRunStatus.Completed,
                EndedAt = DateTimeOffset.UtcNow,
            };
        }

        Publish();
    }

    private void FailRun(Exception ex)
    {
        lock (_gate)
        {
            _runState = _runState with
            {
                Status = ex is OperationCanceledException ? GraphRunStatus.Cancelled : GraphRunStatus.Failed,
                EndedAt = DateTimeOffset.UtcNow,
                ErrorMessage = ex.Message,
            };
        }

        Publish();
    }

    private static string CompactJson(JsonObject json)
    {
        var text = json.ToJsonString();
        return text.Length <= MaxJsonLength ? text : text[..MaxJsonLength] + "...";
    }

    private static string? ExtractArtifactsJson(JsonObject result)
    {
        var artifacts = new JsonObject();
        CopyIfPresent(result, artifacts, "path");
        CopyIfPresent(result, artifacts, "glb_path");
        CopyIfPresent(result, artifacts, "usd_path");
        CopyIfPresent(result, artifacts, "productAddress");
        CopyIfPresent(result, artifacts, "products");
        return artifacts.Count == 0 ? null : artifacts.ToJsonString();
    }

    private static void CopyIfPresent(JsonObject source, JsonObject target, string key)
    {
        if (source.TryGetPropertyValue(key, out var value) && value is not null)
            target[key] = value.DeepClone();
    }

    private void Publish() => RuntimeStateChanged?.Invoke();
}
