using BoomHud.Abstractions.Runtime;
using BoomHud.Godot.Runtime;
using Godot;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json.Nodes;

namespace FantaSim.App.Ui.Seam;

public sealed class ViewRenderer : IDisposable
{
    private readonly Control _parent;
    private readonly Func<IViewSource?> _resolve;
    private readonly Func<string, string?> _resolveShellScenePath;
    private readonly ILogger _logger;
    private readonly RuntimeSurfaceRenderer _renderer;

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
        });
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
                control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                control.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
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
    {
        var model = document.DataModel;
        var summary = Select(model, "summary");
        var signals = Select(model, "signals");
        var agentRuntime = Select(model, "agentRuntime");
        var projection = Select(model, "projection");

        SetLabel(shell, "Subtitle", $"Hermes, app commands, bundles, and verification events - {Int(summary, "entriesShown")} newest entries");
        SetLabel(shell, "AgentCount", $"{Int(summary, "agentGroupCount")} agent runs");
        SetLabel(shell, "CommandCount", $"{Int(summary, "commandCount")} command events");
        SetLabel(shell, "BundleCount", $"{Int(summary, "bundleCount")} bundle events");
        SetLabel(shell, "FailureCount", $"{Int(summary, "failureCount")} failures");

        BindPipeline(shell, model, summary, signals);
        BindAgentRuntime(shell, agentRuntime, summary);
        BindProjection(shell, projection);
        BindProjectedArchive(shell, Array(model, "projectedArchive"), Text(model, "projectionQueryError"));
        BindSignals(shell, signals);
        BindAgentRuns(shell, Array(model, "agentRuns"));
        BindAppEvents(shell, Array(model, "appEvents"));

        BindAction(shell, "RefreshButton", "refresh", source);
        BindAction(shell, "HideButton", "hide", source);
    }

    private static void BindPipeline(Node shell, JsonNode? model, JsonNode? summary, JsonNode? signals)
    {
        var agentRuns = Array(model, "agentRuns");
        var activeAgentGroups = Int(summary, "activeAgentGroups");
        var agentState = agentRuns.Count == 0
            ? "no recent run"
            : activeAgentGroups > 0
                ? $"{activeAgentGroups} active run"
                : $"{Text(agentRuns[0], "status")} - {Text(agentRuns[0], "shortKey")}";

        SetLabel(shell, "AgentRunValue", agentState);
        SetLabel(shell, "VerificationValue", EntryText(Object(signals, "lastVerification"), "none in current window"));
        SetLabel(shell, "BundleReloadValue", EntryText(Object(signals, "lastBundleReload"), "none in current window"));

        var latestIssue = Object(signals, "latestError");
        SetLabel(shell, "LatestIssueValue", EntryText(latestIssue, "clear"));
        SetLabel(shell, "LatestIssueState", latestIssue is null ? "clear" : "issue");
    }

    private static void BindAgentRuntime(Node shell, JsonNode? agentRuntime, JsonNode? summary)
    {
        var profile = Text(agentRuntime, "profile", "fantasim-godot");
        var home = Text(agentRuntime, "homePathLabel", "not loaded");
        var error = Text(agentRuntime, "error");

        SetLabel(shell, "RuntimeModeValue", Text(agentRuntime, "runtime", "local in-process orchestration"));
        SetLabel(shell, "RuntimeProfileValue", string.IsNullOrWhiteSpace(home) ? profile : $"{profile} - {home}");
        SetLabel(shell, "RuntimeModelValue", Text(agentRuntime, "modelLabel", "glm-5.2 via Ollama Cloud / Zai"));
        SetLabel(shell, "RuntimeWorkerValue", Text(agentRuntime, "workerState", "not loaded"));
        SetLabel(shell, "RuntimeGatewayValue", Text(agentRuntime, "gatewayState", "not loaded"));
        SetLabel(shell, "RuntimePluginsValue", Text(agentRuntime, "pluginsLabel", "none enabled"));
        SetLabel(shell, "RuntimeSessionsValue", Text(agentRuntime, "sessionsLabel", "not loaded"));
        SetLabel(shell, "RuntimeSignalValue", Text(summary, "lastAgentSignal", "no agent signal in current window"));
        SetLabel(shell, "RuntimeErrorValue", string.IsNullOrWhiteSpace(error) ? "none" : error);
    }

    private static void BindProjection(Node shell, JsonNode? projection)
    {
        var enabled = Bool(projection, "enabled");
        var target = enabled
            ? $"{Text(projection, "provider", "none")} {Text(projection, "namespace", "-")}/{Text(projection, "database", "-")}/{Text(projection, "table", "-")}"
            : "local ledger only";
        var failed = Int(projection, "failed");
        var dropped = Int(projection, "dropped");
        var health = !enabled
            ? "disabled"
            : failed == 0 && dropped == 0
                ? "healthy"
                : $"{failed} failed / {dropped} dropped";
        var counts = $"{Text(projection, "projected", "0")} projected / {Text(projection, "queued", "0")} queued";
        var error = Text(projection, "lastError", "none");
        if (string.IsNullOrWhiteSpace(error))
            error = "none";

        SetLabel(shell, "ProjectionBackendValue", target);
        SetLabel(shell, "ProjectionHealthValue", health);
        SetLabel(shell, "ProjectionCountsValue", counts);
        SetLabel(shell, "ProjectionErrorValue", error);
    }

    private static void BindProjectedArchive(Node shell, JsonArray entries, string queryError)
    {
        if (Find<VBoxContainer>(shell, "ProjectedArchiveList") is not { } list)
            return;

        ClearChildren(list);
        if (!string.IsNullOrWhiteSpace(queryError))
            list.AddChild(MakeLabel($"query error: {queryError}", muted: true));

        if (entries.Count == 0)
        {
            list.AddChild(MakeLabel("No projected Activity rows read from SurrealDB yet."));
            return;
        }

        foreach (var entry in entries)
        {
            var panel = MakeItemPanel();
            var body = (VBoxContainer)panel.GetChild(0);
            body.AddChild(MakePairRow(Text(entry, "kind", "?"), Text(entry, "text")));

            var error = Text(entry, "error");
            var outcome = Text(entry, "outcome");
            if (!string.IsNullOrWhiteSpace(error))
                body.AddChild(MakeLabel($"error: {error}", muted: true));
            else if (!string.IsNullOrWhiteSpace(outcome))
                body.AddChild(MakeLabel($"outcome: {outcome}", muted: true));

            list.AddChild(panel);
        }
    }

    private static void BindSignals(Node shell, JsonNode? signals)
    {
        SetLabel(shell, "SignalVerificationValue", EntryText(Object(signals, "lastVerification"), "none in current window"));
        SetLabel(shell, "SignalBundleValue", EntryText(Object(signals, "lastBundleReload"), "none in current window"));
        SetLabel(shell, "SignalErrorValue", EntryText(Object(signals, "latestError"), "none"));
    }

    private static void BindAgentRuns(Node shell, JsonArray agentRuns)
    {
        if (Find<VBoxContainer>(shell, "AgentRunList") is not { } list)
            return;

        ClearChildren(list);
        if (agentRuns.Count == 0)
        {
            list.AddChild(MakeLabel("No agent activity recorded yet. Start agent.developBundleAsync or agent.run to populate this timeline."));
            return;
        }

        foreach (var run in agentRuns)
        {
            var panel = MakeItemPanel();
            var body = (VBoxContainer)panel.GetChild(0);
            body.AddChild(MakePairRow(Text(run, "shortKey", "-"), Text(run, "status", "active")));
            body.AddChild(MakeLabel(Text(run, "latestText", "no recent signal")));

            foreach (var entry in Array(run, "entries").Take(4))
            {
                var text = Text(entry, "text");
                if (!string.IsNullOrWhiteSpace(text))
                    body.AddChild(MakeLabel(text, muted: true));
            }

            list.AddChild(panel);
        }
    }

    private static void BindAppEvents(Node shell, JsonArray events)
    {
        if (Find<VBoxContainer>(shell, "AppEventList") is not { } list)
            return;

        ClearChildren(list);
        if (events.Count == 0)
        {
            list.AddChild(MakeLabel("No app activity outside agent work in the current window."));
            return;
        }

        foreach (var entry in events)
        {
            var panel = MakeItemPanel();
            var body = (VBoxContainer)panel.GetChild(0);
            body.AddChild(MakePairRow(Text(entry, "kind", "?"), Text(entry, "text")));

            var error = Text(entry, "error");
            var outcome = Text(entry, "outcome");
            if (!string.IsNullOrWhiteSpace(error))
                body.AddChild(MakeLabel($"error: {error}", muted: true));
            else if (!string.IsNullOrWhiteSpace(outcome))
                body.AddChild(MakeLabel($"outcome: {outcome}", muted: true));

            list.AddChild(panel);
        }
    }

    private static PanelContainer MakeItemPanel()
    {
        var panel = new PanelContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        panel.AddThemeStyleboxOverride("panel", ItemBackground());

        var body = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        body.AddThemeConstantOverride("separation", 4);
        panel.AddChild(body);
        return panel;
    }

    private static HBoxContainer MakePairRow(string left, string right)
    {
        var row = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(MakeLabel(left, minWidth: 96));

        var rightLabel = MakeLabel(right);
        rightLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(rightLabel);
        return row;
    }

    private static Label MakeLabel(string text, bool muted = false, int? minWidth = null)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            ClipText = false,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        if (minWidth is not null)
            label.CustomMinimumSize = new Vector2(minWidth.Value, 0);
        label.AddThemeColorOverride("font_color", muted ? new Color(0.66f, 0.70f, 0.74f) : new Color(0.90f, 0.92f, 0.95f));
        return label;
    }

    private static StyleBoxFlat ItemBackground()
    {
        var box = new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.08f, 0.10f, 0.78f),
            BorderColor = new Color(0.16f, 0.19f, 0.23f, 1.0f),
        };
        box.SetBorderWidthAll(1);
        box.SetCornerRadiusAll(3);
        box.ContentMarginTop = 6;
        box.ContentMarginRight = 8;
        box.ContentMarginBottom = 6;
        box.ContentMarginLeft = 8;
        return box;
    }

    private static void BindAction(Node shell, string nodeName, string command, IViewSource source)
    {
        if (Find<Button>(shell, nodeName) is not { } button)
            return;

        button.Pressed += () => source.Dispatch(command, button.Name.ToString());
    }

    private static void SetLabel(Node root, string nodeName, string text)
    {
        if (Find<Label>(root, nodeName) is { } label)
            label.Text = text;
    }

    private static T? Find<T>(Node root, string nodeName) where T : Node
        => root.FindChild(nodeName, recursive: true, owned: false) as T;

    private static string EntryText(JsonObject? entry, string fallback)
        => entry is null ? fallback : Text(entry, "text", fallback);

    private static JsonNode? Select(JsonNode? node, string path)
    {
        var current = node;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current is not JsonObject obj || !obj.TryGetPropertyValue(segment, out current))
                return null;
        }

        return current;
    }

    private static JsonObject? Object(JsonNode? node, string path)
        => Select(node, path) as JsonObject;

    private static JsonArray Array(JsonNode? node, string path)
        => Select(node, path) as JsonArray ?? new JsonArray();

    private static string Text(JsonNode? node, string path, string fallback = "")
    {
        var value = string.IsNullOrWhiteSpace(path) ? node : Select(node, path);
        if (value is null)
            return fallback;

        if (value.GetValueKind() == System.Text.Json.JsonValueKind.Null)
            return fallback;

        if (value is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<string>(out var text))
                return text ?? fallback;
            if (jsonValue.TryGetValue<int>(out var intValue))
                return intValue.ToString();
            if (jsonValue.TryGetValue<long>(out var longValue))
                return longValue.ToString();
            if (jsonValue.TryGetValue<double>(out var doubleValue))
                return doubleValue.ToString("0.##");
        }

        return value.ToJsonString();
    }

    private static int Int(JsonNode? node, string path)
    {
        var value = Select(node, path);
        if (value is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<int>(out var intValue))
                return intValue;
            if (jsonValue.TryGetValue<long>(out var longValue))
                return (int)longValue;
            if (jsonValue.TryGetValue<string>(out var text) && int.TryParse(text, out var parsed))
                return parsed;
        }

        return 0;
    }

    private static bool Bool(JsonNode? node, string path)
    {
        var value = Select(node, path);
        if (value is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<bool>(out var boolValue))
                return boolValue;
            if (jsonValue.TryGetValue<string>(out var text) && bool.TryParse(text, out var parsed))
                return parsed;
        }

        return false;
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
