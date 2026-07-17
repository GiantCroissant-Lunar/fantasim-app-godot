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
    public const string ExplodedCommandId = "render.exploded";
    public const string MantleCommandId = "render.mantle";
    public const string LodDebugCommandId = "render.lod";

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
            return new RenderCompositionHandle(captureNode, registered: false, new CutawayTargetHolder(), new ExplodedTargetHolder(), new MantleTargetHolder(), new LodDebugTargetHolder());
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

        // M-B: render.exploded — the exploded solid-crust view. Mirrors render.cutaway: the binder
        // is mounted later (world bundle load) so the handler closes over a mutable target that
        // Host.cs wires via SetExplodedTarget. Null target = binder not yet mounted; the command
        // reports that. ExplodedRequestParser is the Godot-free parser (unit-tested).
        var explodedTarget = new ExplodedTargetHolder();
        commandService.Register(
            new FantaSim.App.Command.CommandDescriptor(
                Id: ExplodedCommandId,
                Title: "Exploded solid crust",
                Description: "Activates the exploded solid-crust view. Payload {\"factor\":N} keeps the whole-globe radial explosion. Payload {\"factor\":N,\"focusConvergent\":true} isolates the proven convergent pair; factor 0 is exact and factor > 0 lifts only the overriding complete plate. N is clamped to [0,1].",
                Category: "render"),
            (payloadJson, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                ExplodedRequest req;
                try
                {
                    req = ExplodedRequestParser.Parse(payloadJson);
                }
                catch (ArgumentException ex)
                {
                    return Task.FromResult<string?>(JsonSerializer.Serialize(new { ok = false, error = ex.Message }));
                }

                var target = explodedTarget.Target;
                if (target is null)
                {
                    return Task.FromResult<string?>(JsonSerializer.Serialize(new
                    {
                        ok = false,
                        error = "planet presentation binder not mounted (world bundle not loaded)",
                    }));
                }

                target(req.Factor, req.FocusConvergent);
                log.LogInformation(
                    "render.exploded: factor={Factor} focusConvergent={FocusConvergent}",
                    req.Factor,
                    req.FocusConvergent);
                return Task.FromResult<string?>(JsonSerializer.Serialize(new
                {
                    ok = true,
                    factor = req.Factor,
                    assembled = req.IsAssembled,
                    focusConvergent = req.FocusConvergent,
                }));
            });

        // DEPRECATED render.mantle alias (directive 2, 2026-07-16): mantle convection is a LAYER, not
        // an x-ray mode. The command survives only as a loud deprecated alias over the geosphere.mantle
        // layer selection (the same path as timeline.select_layer). Mirrors render.cutaway: the binder
        // mounts later (world bundle load), so the handler closes over a mutable target Host.cs wires
        // via SetMantleTarget. The target returns null on success or a rejection message; MantleAlias
        // builds the deprecation-noted result JSON (ok:false + message on rejection, never a no-op).
        var mantleTarget = new MantleTargetHolder();
        commandService.Register(
            new FantaSim.App.Command.CommandDescriptor(
                Id: MantleCommandId,
                Title: "Mantle layer (deprecated alias)",
                Description: "DEPRECATED alias for timeline.select_layer geosphere/geosphere.mantle. Routes to the mantle LAYER selection (composed interior + separated crust slabs; no x-ray ghost shell). Payload: {\"enabled\":true|false}. Default: {\"enabled\":true}. The result carries a deprecation note.",
                Category: "render"),
            (payloadJson, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                MantleRequest req;
                try
                {
                    req = MantleRequestParser.Parse(payloadJson);
                }
                catch (ArgumentException ex)
                {
                    return Task.FromResult<string?>(JsonSerializer.Serialize(new { ok = false, error = ex.Message }));
                }

                var target = mantleTarget.Target;
                if (target is null)
                {
                    return Task.FromResult<string?>(JsonSerializer.Serialize(new
                    {
                        ok = false,
                        error = "planet presentation binder not mounted (world bundle not loaded)",
                    }));
                }

                string? rejection = target(req.Enabled);
                bool ok = rejection is null;
                log.LogInformation("render.mantle (deprecated alias): enabled={Enabled} ok={Ok}", req.Enabled, ok);
                return Task.FromResult<string?>(MantleAlias.BuildResultJson(ok, req.Enabled, rejection));
            });

        var lodDebugTarget = new LodDebugTargetHolder();
        commandService.Register(
            new FantaSim.App.Command.CommandDescriptor(
                Id: LodDebugCommandId,
                Title: "LOD debug overlay",
                Description: "Toggles the adaptive LOD debug overlay. Payload: {\"mode\":\"off\"|\"wireframe\"|\"density\"}. wireframe renders plate-cap mesh edges as wireframe; density color-codes triangles by graph-distance-to-boundary. Default: {\"mode\":\"off\"} (clear).",
                Category: "render"),
            (payloadJson, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                LodDebugRequest req;
                try
                {
                    req = LodDebugRequestParser.Parse(payloadJson);
                }
                catch (ArgumentException ex)
                {
                    return Task.FromResult<string?>(JsonSerializer.Serialize(new { ok = false, error = ex.Message }));
                }

                var target = lodDebugTarget.Target;
                if (target is null)
                {
                    // Slice-1 fallback: no binder-side overlay is wired yet. Wireframe is served
                    // product-wide via the viewport's debug draw (sufficient for the falsifiable
                    // nonuniform-tessellation gate); density needs the per-triangle overlay and
                    // reports not-implemented honestly instead of no-op'ing.
                    if (req.Mode == LodDebugMode.Density)
                    {
                        return Task.FromResult<string?>(JsonSerializer.Serialize(new
                        {
                            ok = false,
                            error = "density overlay not implemented in slice 1 (wireframe is)",
                        }));
                    }

                    captureNode.GetViewport().DebugDraw = req.Mode == LodDebugMode.Wireframe
                        ? Godot.Viewport.DebugDrawEnum.Wireframe
                        : Godot.Viewport.DebugDrawEnum.Disabled;
                    log.LogInformation("render.lod: viewport debug draw set ({Mode}).", req.Mode);
                    return Task.FromResult<string?>(JsonSerializer.Serialize(new
                    {
                        ok = true,
                        mode = req.Mode.ToString().ToLowerInvariant(),
                        inactive = req.IsInactive,
                        surface = "viewport-debug-draw",
                    }));
                }

                target(req.Mode);
                log.LogInformation("render.lod: mode={Mode}", req.Mode);
                return Task.FromResult<string?>(JsonSerializer.Serialize(new
                {
                    ok = true,
                    mode = req.Mode.ToString().ToLowerInvariant(),
                    inactive = req.IsInactive,
                }));
            });

        log.LogInformation("registered: render.screenshot (viewport capture), render.cutaway (wedge), render.exploded (solid crust), render.mantle (deprecated mantle-layer alias), render.lod (wireframe/density debug toggle).");
        return new RenderCompositionHandle(captureNode, registered: true, cutawayTarget, explodedTarget, mantleTarget, lodDebugTarget);
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

    void SetExplodedTarget(Action<double, bool>? target);
    void SetMantleTarget(Func<bool, string?>? target);

    void SetLodDebugTarget(Action<LodDebugMode>? target);
}

internal sealed class RenderCompositionHandle : IRenderCompositionHandle
{
    private readonly GodotViewportCapture _captureNode;
    private readonly bool _registered;
    private readonly CutawayTargetHolder _cutawayTarget;
    private readonly ExplodedTargetHolder _explodedTarget;
    private readonly MantleTargetHolder _mantleTarget;
    private readonly LodDebugTargetHolder _lodDebugTarget;
    private bool _disposed;

    public RenderCompositionHandle(
        GodotViewportCapture captureNode,
        bool registered,
        CutawayTargetHolder cutawayTarget,
        ExplodedTargetHolder explodedTarget,
        MantleTargetHolder mantleTarget,
        LodDebugTargetHolder lodDebugTarget)
    {
        _captureNode = captureNode;
        _registered = registered;
        _cutawayTarget = cutawayTarget;
        _explodedTarget = explodedTarget;
        _mantleTarget = mantleTarget;
        _lodDebugTarget = lodDebugTarget;
    }

    public bool Registered => _registered;

    public void Unregister(IRegistry registry)
    {
        if (!_registered)
            return;

        registry?.TryGet<FantaSim.App.Command.IService>()?.Unregister(RenderComposition.ScreenshotCommandId);
        registry?.TryGet<FantaSim.App.Command.IService>()?.Unregister(RenderComposition.CutawayCommandId);
        registry?.TryGet<FantaSim.App.Command.IService>()?.Unregister(RenderComposition.ExplodedCommandId);
        registry?.TryGet<FantaSim.App.Command.IService>()?.Unregister(RenderComposition.MantleCommandId);
        registry?.TryGet<FantaSim.App.Command.IService>()?.Unregister(RenderComposition.LodDebugCommandId);
    }

    public void SetCutawayTarget(Action<double, double>? target)
        => _cutawayTarget.Target = target;

    public void SetExplodedTarget(Action<double, bool>? target)
        => _explodedTarget.Target = target;
    public void SetMantleTarget(Func<bool, string?>? target)
        => _mantleTarget.Target = target;

    public void SetLodDebugTarget(Action<LodDebugMode>? target)
        => _lodDebugTarget.Target = target;

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

internal sealed class ExplodedTargetHolder
{
    public Action<double, bool>? Target { get; set; }
}

internal sealed class MantleTargetHolder
{
    public Func<bool, string?>? Target { get; set; }
}

internal sealed class LodDebugTargetHolder
{
    public Action<LodDebugMode>? Target { get; set; }
}
