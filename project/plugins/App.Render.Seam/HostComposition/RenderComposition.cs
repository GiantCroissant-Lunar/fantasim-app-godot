using System.Globalization;
using System.Text.Json;
using FantaSim.App.Common;
using FantaSim.App.Render;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;

namespace FantaSim.App.Render.Seam;

/// <summary>
/// T4 resident render seam. Registers the <c>render.screenshot</c> command against the
/// resident <see cref="FantaSim.App.Command.IService"/> and supplies the Godot
/// <see cref="IViewportCapture"/>. Mirrors the sibling HostComposition seams
/// (TimelineComposition, RemoteIngressComposition): read what the context needs, register
/// services, return an <see cref="IRenderCompositionHandle"/> the host uses to unregister on
/// shutdown so the handler does not pin a collectible ALC.
/// </summary>
public static class RenderComposition
{
    public const string ScreenshotCommandId = "render.screenshot";
    public const string CutawayCommandId = "render.cutaway";

    public static IRenderCompositionHandle ComposeRender(HostCompositionContext ctx, Godot.Node hostNode)
    {
        var log = ctx.LoggerFactory.CreateLogger("HostComposition.Render");
        var registry = ctx.Registry;

        var captureNode = new GodotViewportCapture();
        hostNode.AddChild(captureNode);

        // user:// globalizer bound to ProjectSettings.GlobalizePath (the only Godot call here;
        // ScreenshotRequest.ResolveAbsolutePath is Godot-free and unit-tested).
        Func<string, string> globalizeUserPath = p => Godot.ProjectSettings.GlobalizePath(p);

        var commandService = registry.TryGet<FantaSim.App.Command.IService>();
        if (commandService is null)
        {
            log.LogWarning("Render: no command IService registered; render.screenshot will be inert.");
            return new RenderCompositionHandle(captureNode, registered: false, new CutawayTargetHolder());
        }

        commandService.Register(
            new FantaSim.App.Command.CommandDescriptor(
                Id: ScreenshotCommandId,
                Title: "Capture screenshot",
                Description: "Captures the main viewport to a PNG. Payload: {\"path\":\"<absolute or user:// path>\"}. Default: user://screenshots/<UTC yyyyMMdd-HHmmss>.png.",
                Category: "render"),
            (payloadJson, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                string requestedPath;
                try
                {
                    var parsed = ScreenshotRequest.ParsePath(payloadJson);
                    requestedPath = parsed ?? ScreenshotRequest.BuildDefaultPath(DateTimeOffset.UtcNow);
                }
                catch (ArgumentException ex)
                {
                    return Task.FromResult<string?>(JsonSerializer.Serialize(new { ok = false, error = ex.Message }));
                }

                string absolutePath;
                try
                {
                    absolutePath = ScreenshotRequest.ResolveAbsolutePath(requestedPath, globalizeUserPath);
                    ScreenshotRequest.EnsureDirectoryExists(absolutePath);
                }
                catch (InvalidOperationException ex)
                {
                    return Task.FromResult<string?>(JsonSerializer.Serialize(new { ok = false, error = ex.Message }));
                }

                // Capture runs on the Godot main thread (the command service's IMainThreadDispatcher
                // marshals handler invocations onto the main loop). The capture node is a resident
                // child of the host, so GetViewport() resolves the main window viewport.
                var captured = captureNode.CaptureAndSavePng(absolutePath);
                if (captured is null)
                {
                    return Task.FromResult<string?>(JsonSerializer.Serialize(new
                    {
                        ok = false,
                        error = "no viewport texture available (headless or unrendered viewport)",
                    }));
                }

                var (width, height) = captured.Value;
                log.LogInformation("render.screenshot: captured {Width}x{Height} -> {Path}", width, height, absolutePath);
                return Task.FromResult<string?>(JsonSerializer.Serialize(new
                {
                    ok = true,
                    path = absolutePath,
                    width = width,
                    height = height,
                }));
            });

        // W3a: render.cutaway — the binder is created later (when the world bundle loads), so the
        // handler closes over a mutable target that Host.cs wires via SetCutawayTarget. Null target
        // = binder not yet mounted; the command reports that. Mirrors render.screenshot's shape.
        var cutawayTarget = new CutawayTargetHolder();
        commandService.Register(
            new FantaSim.App.Command.CommandDescriptor(
                Id: CutawayCommandId,
                Title: "Cutaway wedge",
                Description: "Activates the planet cutaway wedge. Payload: {\"azimuthDeg\":N,\"widthDeg\":N}. Width 0 clears the cutaway. Default: {\"azimuthDeg\":0,\"widthDeg\":0} (clear).",
                Category: "render"),
            (payloadJson, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                CutawayRequest req;
                try
                {
                    req = CutawayRequestParser.Parse(payloadJson);
                }
                catch (ArgumentException ex)
                {
                    return Task.FromResult<string?>(JsonSerializer.Serialize(new { ok = false, error = ex.Message }));
                }

                var target = cutawayTarget.Target;
                if (target is null)
                {
                    return Task.FromResult<string?>(JsonSerializer.Serialize(new
                    {
                        ok = false,
                        error = "planet presentation binder not mounted (world bundle not loaded)",
                    }));
                }

                target(req.AzimuthDeg, req.WidthDeg);
                log.LogInformation("render.cutaway: azimuth={Azimuth} width={Width}", req.AzimuthDeg, req.WidthDeg);
                return Task.FromResult<string?>(JsonSerializer.Serialize(new
                {
                    ok = true,
                    azimuthDeg = req.AzimuthDeg,
                    widthDeg = req.WidthDeg,
                    inactive = req.IsInactive,
                }));
            });

        log.LogInformation("registered: render.screenshot (viewport capture), render.cutaway (wedge).");
        return new RenderCompositionHandle(captureNode, registered: true, cutawayTarget);
    }
}

/// <summary>
/// Handle returned by <see cref="RenderComposition.ComposeRender"/>. The host calls
/// <see cref="Unregister"/> on shutdown so the handler delegate (which closes over the
/// resident capture node) does not pin a collectible ALC. Mirrors the unregister discipline
/// in WorldPlugin.ShutdownAsync.
/// </summary>
public interface IRenderCompositionHandle : IDisposable
{
    bool Registered { get; }

    void Unregister(IRegistry registry);

    void SetCutawayTarget(Action<double, double>? target);
}

internal sealed class RenderCompositionHandle : IRenderCompositionHandle
{
    private readonly GodotViewportCapture _captureNode;
    private readonly bool _registered;
    private readonly CutawayTargetHolder _cutawayTarget;
    private bool _disposed;

    public RenderCompositionHandle(GodotViewportCapture captureNode, bool registered, CutawayTargetHolder cutawayTarget)
    {
        _captureNode = captureNode;
        _registered = registered;
        _cutawayTarget = cutawayTarget;
    }

    public bool Registered => _registered;

    public void Unregister(IRegistry registry)
    {
        if (!_registered)
            return;

        registry?.TryGet<FantaSim.App.Command.IService>()?.Unregister(RenderComposition.ScreenshotCommandId);
        registry?.TryGet<FantaSim.App.Command.IService>()?.Unregister(RenderComposition.CutawayCommandId);
    }

    public void SetCutawayTarget(Action<double, double>? target)
        => _cutawayTarget.Target = target;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            if (_captureNode.IsInsideTree())
                _captureNode.QueueFree();
        }
        catch
        {
        }
    }
}

internal sealed class CutawayTargetHolder
{
    public Action<double, double>? Target { get; set; }
}