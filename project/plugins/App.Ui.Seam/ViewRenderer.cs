using BoomHud.Abstractions.Runtime;
using BoomHud.Godot.Runtime;
using Godot;
using Microsoft.Extensions.Logging;
using System.Text;

namespace FantaSim.App.Ui.Seam;

public sealed class ViewRenderer : IDisposable
{
    private readonly Control _parent;
    private readonly Func<IViewSource?> _resolve;
    private readonly Func<string, string?> _resolveShellScenePath;
    private readonly ILogger _logger;
    private readonly RuntimeSurfaceRenderer _renderer;
    private readonly PresentationShellBinder _shellBinder;

    private IViewSource? _source;
    private Action? _onChanged;
    private BoomHudGraphEditBinder? _graphBinder;

    public ViewRenderer(Control parent, Func<IViewSource?> resolve, Func<string, string?> resolveShellScenePath, ILogger logger)
    {
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _resolveShellScenePath = resolveShellScenePath ?? throw new ArgumentNullException(nameof(resolveShellScenePath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _renderer = new RuntimeSurfaceRenderer(new RuntimeSurfaceRendererOptions
        {
            Catalog = RuntimeSurfaceCatalog.Basic,
            ActionHandler = new RuntimeSurfaceActionHandler(OnAction),
            Theme = BuildTheme(),
            // Activity renders up to 60 cards; a card can expand to ~20+ inline detail rows. Worst case
            // (all expanded, dense payloads) approaches ~2100 nodes, so keep comfortable headroom above
            // the 512 default — otherwise a heavy render trips the validator and blanks the whole surface.
            ValidatorOptions = new RuntimeSurfaceValidatorOptions { MaxNodeCount = 4096 },
        });
        _shellBinder = new PresentationShellBinder(logger);
    }

    public void Bind()
    {
        _source = _resolve();
        if (_source is null)
        {
            _logger.LogWarning("No IViewSource is registered for this view.");
            return;
        }

        _onChanged = () => Callable.From(Render).CallDeferred();
        _source.Changed += _onChanged;
        Render();
        _logger.LogInformation("View renderer bound to {ViewId}.", _source.ViewId);
    }

    public void Rebind()
    {
        Unbind();
        Bind();
    }

    public void ReleaseSourceReference()
    {
        if (_source is not null && _onChanged is not null)
            _source.Changed -= _onChanged;

        _onChanged = null;
        _source = null;
    }

    private void Unbind()
    {
        _graphBinder?.Dispose();
        _graphBinder = null;

        if (_source is not null && _onChanged is not null)
            _source.Changed -= _onChanged;

        _onChanged = null;
        _source = null;
        foreach (var child in _parent.GetChildren())
            child.QueueFree();
    }

    private void Render()
    {
        var source = _source;
        if (source is null)
            return;

        try
        {
            // Drop the previous graph binder first: Mount frees the old tree (incl. any GraphEdit).
            _graphBinder?.Dispose();
            _graphBinder = null;

            var document = source.BuildDocument();
            var mounted = TryMountShell(source, document) ?? _renderer.Mount(_parent, document, clearExistingChildren: true);
            if (mounted is Control control)
            {
                control.SetAnchorsPreset(Control.LayoutPreset.FullRect);
                control.SetOffsetsPreset(Control.LayoutPreset.FullRect);
                control.Position = Vector2.Zero;
                control.Size = _parent.Size;
                control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                control.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
                control.SetDeferred(Control.PropertyName.Size, _parent.Size);
            }

            BindGraphIfPresent(mounted, source);
            NormalizeLabels(mounted);
            SaveGeneratedScene(source.ViewId, mounted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "View render failed.");
        }
    }

    // nodeGraph seam hook: the BoomHud renderer mounts an empty GraphEdit for a `nodeGraph` component;
    // the resident BoomHudGraphEditBinder populates it by reflecting over the source's Nodes/Wires
    // (TryBind returns null for sources without them, so non-graph views no-op). MSAGL lays it out.
    private void BindGraphIfPresent(Node? mounted, IViewSource source)
    {
        var graphEdit = FindGraphEdit(mounted);
        if (graphEdit is null)
            return;

        graphEdit.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _graphBinder = BoomHudGraphEditBinder.TryBind(graphEdit, source);
        if (_graphBinder is null)
            return;

        _logger.LogInformation("ViewRenderer: graph binder bound for view '{ViewId}'.", source.ViewId);

        graphEdit.NodeSelected += selectedNode =>
            source.Dispatch($"select-node:{selectedNode.Name}", null);

        Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(graphEdit))
            {
                GraphNodeVisualEnhancer.TryApply(graphEdit, source, _logger);
                MsaglGraphLayoutApplicator.TryApply(graphEdit, source, _logger);
                GraphAnnotationFrameEnhancer.TryApply(graphEdit, source, _logger);
            }
        }).CallDeferred();
    }

    private static GraphEdit? FindGraphEdit(Node? node)
    {
        if (node is null)
            return null;
        if (node is GraphEdit graphEdit)
            return graphEdit;
        foreach (var child in node.GetChildren())
            if (FindGraphEdit(child) is { } found)
                return found;
        return null;
    }

    private Node? TryMountShell(IViewSource source, RuntimeSurfaceDocument document)
    {
        try
        {
            var scenePath = _resolveShellScenePath(source.ViewId);
            if (string.IsNullOrWhiteSpace(scenePath))
                return null;

            var packed = ResourceLoader.Load<PackedScene>(scenePath, null, ResourceLoader.CacheMode.ReplaceDeep);
            if (packed is null)
            {
                _logger.LogWarning("UI shell scene not found for {ViewId}: {Path}", source.ViewId, scenePath);
                return null;
            }

            var instance = packed.Instantiate();
            if (instance is not Control shell)
            {
                instance.QueueFree();
                _logger.LogWarning("UI shell scene root is not a Control for {ViewId}: {Path}", source.ViewId, scenePath);
                return null;
            }

            ClearChildren(_parent);
            shell.Name = $"Shell_{source.ViewId}";
            shell.Position = Vector2.Zero;
            shell.Size = _parent.Size;
            shell.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            shell.SetOffsetsPreset(Control.LayoutPreset.FullRect);
            shell.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            shell.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            _parent.AddChild(shell);
            shell.SetDeferred(Control.PropertyName.Size, _parent.Size);

            BindShell(shell, source, document);
            return shell;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "UI shell mount failed for {ViewId}; falling back to BoomHud renderer.", source.ViewId);
            return null;
        }
    }

    private static void ClearChildren(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void BindShell(Control shell, IViewSource source, RuntimeSurfaceDocument document)
        => _shellBinder.Bind(shell, document, DispatchCurrent);

    private void DispatchCurrent(string action, string? componentId)
    {
        var source = _source;
        if (source is null)
        {
            _logger.LogDebug("View action ignored because no source is bound: {Action}", action);
            return;
        }

        source.Dispatch(action, componentId);
    }

    private static void NormalizeLabels(Node? node)
    {
        if (node is null)
            return;

        if (node is Label label)
        {
            label.AutowrapMode = TextServer.AutowrapMode.Off;
            label.ClipText = false;
            label.AddThemeColorOverride("font_color", new Color(0.90f, 0.92f, 0.95f));
        }

        foreach (var child in node.GetChildren())
            NormalizeLabels(child);
    }

    private void SaveGeneratedScene(string viewId, Node? mounted)
    {
        if (mounted is null)
            return;

        try
        {
            AssignSceneOwner(mounted, mounted);

            const string outputDirectory = "user://boomhud-surfaces";
            Directory.CreateDirectory(ProjectSettings.GlobalizePath(outputDirectory));

            var outputPath = $"{outputDirectory}/{SafeFileName(viewId)}.tscn";
            var scene = new PackedScene();
            var packError = scene.Pack(mounted);
            if (packError != Error.Ok)
            {
                _logger.LogWarning("BoomHud scene pack failed for {ViewId}: {Error}", viewId, packError);
                return;
            }

            var saveError = ResourceSaver.Save(scene, outputPath);
            if (saveError == Error.Ok)
                _logger.LogInformation("BoomHud scene generated for {ViewId}: {Path}", viewId, outputPath);
            else
                _logger.LogWarning("BoomHud scene save failed for {ViewId}: {Error}", viewId, saveError);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "BoomHud scene generation failed for {ViewId}.", viewId);
        }
    }

    private static void AssignSceneOwner(Node node, Node sceneRoot)
    {
        foreach (var child in node.GetChildren())
        {
            child.Owner = sceneRoot;
            AssignSceneOwner(child, sceneRoot);
        }
    }

    private static string SafeFileName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '_');
        }

        return builder.Length == 0 ? "surface" : builder.ToString();
    }

    private void OnAction(RuntimeSurfaceActionInvocation invocation)
        => _source?.Dispatch(invocation.Action.Command, invocation.ComponentId);

    private static RuntimeSurfaceTheme BuildTheme()
        => new()
        {
            Variants = new Dictionary<string, RuntimeComponentStyle>(StringComparer.OrdinalIgnoreCase)
            {
                ["surface"] = new()
                {
                    Fill = "#101418F2",
                    BorderColor = "#2C3540",
                    BorderWidth = 1,
                    CornerRadius = 4,
                    Padding = new[] { 12, 14, 12, 14 },
                },
                ["section"] = new()
                {
                    Fill = "#151A20E6",
                    BorderColor = "#29323C",
                    BorderWidth = 1,
                    CornerRadius = 4,
                    Padding = new[] { 10, 12, 10, 12 },
                },
                ["item"] = new()
                {
                    Fill = "#0E1216CC",
                    BorderColor = "#222A33",
                    BorderWidth = 1,
                    CornerRadius = 3,
                    Padding = new[] { 8, 10, 8, 10 },
                },
                ["title"] = new()
                {
                    FontColor = "#F3F6FA",
                    FontSize = 20,
                },
                ["sectionTitle"] = new()
                {
                    FontColor = "#B9C4D1",
                    FontSize = 14,
                },
                ["muted"] = new()
                {
                    FontColor = "#A7B0BB",
                    FontSize = 13,
                },
                ["success"] = Badge("#153A25", "#42D77D"),
                ["danger"] = Badge("#3B171A", "#FF6B73"),
                ["warning"] = Badge("#3A2C12", "#E7BB52"),
                ["info"] = Badge("#132D43", "#66C2FF"),
                ["neutral"] = Badge("#222932", "#D8DEE8"),
            },
        };

    private static RuntimeComponentStyle Badge(string fill, string fontColor)
        => new()
        {
            Fill = fill,
            BorderColor = fontColor,
            BorderWidth = 1,
            CornerRadius = 4,
            Padding = new[] { 2, 8, 2, 8 },
            FontColor = fontColor,
            FontSize = 12,
        };

    public void Dispose() => Unbind();
}
