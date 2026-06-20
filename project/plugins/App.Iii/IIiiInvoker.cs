using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace FantaSim.App.Iii;

/// <summary>
/// Async invoker over the iii engine. Implemented by the gdext bridge (<c>IiiBridge</c> in
/// App.Iii.Seam); kept as an interface so the pure T3 orchestration (IiiFunctionProvider,
/// GraphExecutor) can await iii calls without referencing Godot. Collectible bundles reach iii
/// only through registered <c>INodeFunctionProvider</c>(s) that wrap this invoker -- never the Node.
/// </summary>
public interface IIiiInvoker
{
    Task<JsonObject> RequestAsync(string functionId, JsonObject payload, CancellationToken cancellationToken = default);
}
