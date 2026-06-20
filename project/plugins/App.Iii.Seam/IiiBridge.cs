using System;
using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.Iii;
using Godot;

namespace FantaSim.App.Iii.Seam;

/// <summary>
/// Godot-side async wrapper over the gdext <c>IiiClient</c> node. Implements <see cref="IIiiInvoker"/>
/// so the pure App.Iii T3 orchestration (IiiFunctionProvider, GraphExecutor) can <c>await</c> iii
/// calls. Correlates fire-and-forget <c>request</c> calls with the <c>response</c> signal via
/// opaque ids and TaskCompletionSources.
/// </summary>
/// <remarks>
/// Node-backed T4 seam exception: this is the one place a seam is a <see cref="Node"/> rather than
/// a plain class, because the gdext <c>IiiClient</c> child runs a tokio runtime off-thread and
/// pushes results through an mpsc channel drained in its <c>_Process</c> on the main thread. A
/// plain class has no <c>_Process</c>. Exposes ONLY <see cref="IIiiInvoker"/> upward -- no Godot
/// type crosses to T1/T3, and collectible bundles never reference this Node.
/// </remarks>
public sealed partial class IiiBridge : Node, IIiiInvoker
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonObject>> _pending = new();
    private readonly string _url;
    private Node? _client;

    public IiiBridge(string url = "ws://127.0.0.1:49134") => _url = url;

    public override void _Ready()
    {
        if (!ClassDB.ClassExists("IiiClient"))
        {
            GD.PushError("[iii] IiiClient gdextension class not registered (build the bridge + godot --import).");
            return;
        }
        _client = ClassDB.Instantiate("IiiClient").As<Node>();
        _client.Name = "IiiClient";
        AddChild(_client);                                  // child of this Node -> its process() drains responses
        _client.Call("set_url", _url);
        _client.Connect("response", Callable.From<string, string>(OnResponse));
        GD.Print($"[iii] IiiBridge ready @ {_url}");
    }

    public Task<JsonObject> RequestAsync(string functionId, JsonObject payload, CancellationToken cancellationToken = default)
    {
        if (_client is null)
            return Task.FromException<JsonObject>(new InvalidOperationException("IiiBridge not ready."));

        var id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        if (cancellationToken.CanBeCanceled)
            cancellationToken.Register(() => { if (_pending.TryRemove(id, out var t)) t.TrySetCanceled(); });

        // CallDeferred marshals to the main thread — safe to call from the executor's threadpool continuations.
        _client.CallDeferred("request", id, functionId, payload.ToJsonString());
        return tcs.Task;
    }

    private void OnResponse(string id, string payload)
    {
        if (!_pending.TryRemove(id, out var tcs)) return;
        try
        {
            tcs.TrySetResult(JsonNode.Parse(payload)?.AsObject() ?? new JsonObject());
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }
    }
}
