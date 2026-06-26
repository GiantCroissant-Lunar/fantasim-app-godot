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
/// (comfy.*, blender.*, asset.*, pipeline.*, vplanet.*) and invokes them through <see cref="IIiiInvoker"/>.
/// The supported families are read from the supplied <see cref="IIiiWorkerCatalog"/> so they can be
/// reloaded from the iii worker metadata bundle without changing the resident provider.
/// This is how iii plugs into the general node-graph paradigm -- it provides node functions, not
/// a graph engine. The <see cref="GraphExecutor"/> resolves these by function id.
/// </summary>
public sealed class IiiFunctionProvider : INodeFunctionProvider
{
    private readonly IIiiInvoker _invoker;
    private readonly IIiiWorkerCatalog _catalog;
    private readonly ILogger _logger;

    public IiiFunctionProvider(IIiiInvoker invoker, IIiiWorkerCatalog? catalog = null, ILoggerFactory? loggerFactory = null)
    {
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        _catalog = catalog ?? new IiiWorkerCatalog(IiiWorkerManifest.Default);
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<IiiFunctionProvider>();
    }

    public bool Supports(string functionId)
    {
        foreach (var worker in _catalog.Workers)
        {
            foreach (var family in worker.FunctionFamilies)
            {
                if (functionId.StartsWith(family, StringComparison.Ordinal))
                    return true;
            }

            foreach (var function in worker.Functions)
            {
                if (functionId == function)
                    return true;
            }
        }

        return false;
    }

    public async Task<JsonObject> InvokeAsync(string functionId, JsonObject payload, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("iii invoke {FunctionId}", functionId);
        return await _invoker.RequestAsync(functionId, payload, cancellationToken).ConfigureAwait(false);
    }
}
