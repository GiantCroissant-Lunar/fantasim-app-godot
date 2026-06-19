using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace FantaSim.App.Iii;

/// <summary>
/// One node in an executable iii graph: a single iii function call.
/// <paramref name="FunctionId"/> is the iii function (e.g. "comfy.generate"); <paramref name="Params"/>
/// are static payload fields; wired inputs are merged in by the executor at run time.
/// </summary>
public sealed record GraphNode(string Id, string FunctionId, JsonObject Params);

/// <summary>A data-flow edge: the <c>FromPort</c> field of <c>FromNode</c>'s result becomes the
/// <c>ToPort</c> payload key of <c>ToNode</c>.</summary>
public sealed record GraphWire(string FromNode, string FromPort, string ToNode, string ToPort);

/// <summary>An executable graph: nodes (iii calls) + wires (data flow) + the terminal node whose
/// result is returned. This is the data-driven replacement for the hard-coded pipeline-worker DAG.</summary>
public sealed record GraphDocument(
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphWire> Wires,
    string SinkNodeId);

/// <summary>Async invoker over the iii engine. Implemented by the gdext bridge (IiiBridge); kept as an
/// interface so the executor stays pure and testable.</summary>
public interface IIiiInvoker
{
    Task<JsonObject> RequestAsync(string functionId, JsonObject payload, CancellationToken cancellationToken = default);
}
