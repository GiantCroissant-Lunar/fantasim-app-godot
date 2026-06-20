using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace FantaSim.App.NodeGraph;

/// <summary>A domain capability provider. Domains (World, iii, ...) register handlers under their
/// function-id prefix; the <see cref="GraphExecutor"/> resolves each node's function to the provider
/// that <see cref="Supports"/> it. This keeps the graph domain-agnostic while its nodes carry
/// domain semantics -- the same separation that makes App.Timeline domain-agnostic via
/// ITimelineSource.</summary>
public interface INodeFunctionProvider
{
    /// <summary>True if this provider can invoke <paramref name="functionId"/>.</summary>
    bool Supports(string functionId);

    /// <summary>Invoke the function, returning its output fields as a JSON object.</summary>
    Task<JsonObject> InvokeAsync(string functionId, JsonObject payload, CancellationToken cancellationToken = default);
}
