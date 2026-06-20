using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.NodeGraph;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FantaSim.App.Iii;

/// <summary>
/// The iii axis as a node-function provider: claims the iii capability function families
/// (comfy.*, blender.*, asset.*, test.echo) and invokes them through <see cref="IIiiInvoker"/>.
/// This is how iii plugs into the general node-graph paradigm -- it provides node functions, not
/// a graph engine. The <see cref="GraphExecutor"/> resolves these by function id.
/// </summary>
public sealed class IiiFunctionProvider : INodeFunctionProvider
{
    private readonly IIiiInvoker _invoker;
    private readonly ILogger _logger;

    public IiiFunctionProvider(IIiiInvoker invoker, ILoggerFactory? loggerFactory = null)
    {
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<IiiFunctionProvider>();
    }

    public bool Supports(string functionId)
        => functionId.StartsWith("comfy.", StringComparison.Ordinal)
           || functionId.StartsWith("blender.", StringComparison.Ordinal)
           || functionId.StartsWith("asset.", StringComparison.Ordinal)
           || functionId == "test.echo"
           || functionId == "ping";

    public async Task<JsonObject> InvokeAsync(string functionId, JsonObject payload, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("iii invoke {FunctionId}", functionId);
        return await _invoker.RequestAsync(functionId, payload, cancellationToken).ConfigureAwait(false);
    }
}
