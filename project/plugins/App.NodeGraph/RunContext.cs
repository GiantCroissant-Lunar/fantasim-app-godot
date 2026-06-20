using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace FantaSim.App.NodeGraph;

/// <summary>Optional run-lifecycle hooks domains install so the general executor can preserve
/// cross-node invariants without owning the executor. Example: World registers BeforeRun to
/// detect source-param changes (cache invalidation), AfterNode to commit truth-stream drafts in
/// visit order, and AfterRun to raise GenerationChanged. The executor still owns the topological
/// walk and invokes hooks in deterministic order.</summary>
public sealed class RunContext
{
    /// <summary>Called once before any node runs.</summary>
    public Func<GraphDocument, CancellationToken, Task>? BeforeRun { get; init; }

    /// <summary>Called before each node is invoked. Receives the assembled payload.</summary>
    public Func<GraphNode, JsonObject, CancellationToken, Task>? BeforeNode { get; init; }

    /// <summary>Called after each node resolves. Receives the input payload and the node result.</summary>
    public Func<GraphNode, JsonObject, JsonObject, CancellationToken, Task>? AfterNode { get; init; }

    /// <summary>Called once after the sink resolves (only on a successful run).</summary>
    public Func<GraphDocument, CancellationToken, Task>? AfterRun { get; init; }
}
