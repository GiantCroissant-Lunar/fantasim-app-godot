using Godot;
using Microsoft.Extensions.Logging;
using ViewService = FantaSim.App.Ui.IService;

namespace FantaSim.App.Ui.Seam;

public sealed partial class ViewToggleBar : PanelContainer
{
    public sealed record ViewEntry(string ViewId, string Label, string Key);

    private static readonly Color ActiveTint = new(0.55f, 1.00f, 0.65f);
    private static readonly Color InactiveTint = new(0.82f, 0.82f, 0.86f);

    private readonly ViewService _viewService;
    private readonly IReadOnlyList<ViewEntry> _entries;
    private readonly ILogger _logger;
    private readonly Dictionary<string, Button> _buttons = new(StringComparer.Ordinal);
    private readonly Dictionary<Key, string> _shortcuts = new();
    private Action? _onViewsChanged;

    public ViewToggleBar(ViewService viewService, IReadOnlyList<ViewEntry> entries, ILogger logger)
    {
        _viewService = viewService ?? throw new ArgumentNullException(nameof(viewService));
        _entries = entries ?? throw new ArgumentNullException(nameof(entries));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Name = "ViewToggleBar";
    }

    public override void _Ready()
    {
        SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        Position = new Vector2(8, 8);

        var row = new HBoxContainer { Name = "Row" };
        AddChild(row);

        var title = new Label { Text = "VIEWS" };
        title.AddThemeColorOverride("font_color", new Color(0.70f, 0.74f, 0.80f));
        row.AddChild(title);

        foreach (var entry in _entries)
        {
            var button = new Button
            {
                Name = $"Toggle_{entry.ViewId}",
                Text = $"{entry.Key}:{entry.Label}",
                FocusMode = Control.FocusModeEnum.None,
                TooltipText = $"Toggle {entry.ViewId}",
            };
            var viewId = entry.ViewId;
            button.Pressed += () => Toggle(viewId);
            row.AddChild(button);
            _buttons[viewId] = button;

            if (Enum.TryParse<Key>($"Key{entry.Key}", out var key))
                _shortcuts[key] = viewId;
        }

        _onViewsChanged = () => Callable.From(SyncButtons).CallDeferred();
        _viewService.ViewsChanged += _onViewsChanged;
        SyncButtons();

        _logger.LogInformation("View toggle bar ready with {Count} view(s).", _entries.Count);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } key
            && key.GetModifiersMask() == 0
            && _shortcuts.TryGetValue(key.Keycode, out var viewId))
        {
            Toggle(viewId);
            GetViewport().SetInputAsHandled();
        }
    }

    private void Toggle(string viewId)
    {
        if (_viewService.ActiveViews.Contains(viewId))
            _ = _viewService.HideAsync(viewId);
        else
            _ = _viewService.ShowAsync(viewId);
    }

    private void SyncButtons()
    {
        var active = _viewService.ActiveViews;
        foreach (var (viewId, button) in _buttons)
        {
            if (GodotObject.IsInstanceValid(button))
                button.Modulate = active.Contains(viewId) ? ActiveTint : InactiveTint;
        }
    }

    public override void _ExitTree()
    {
        if (_onViewsChanged is not null)
            _viewService.ViewsChanged -= _onViewsChanged;
        _onViewsChanged = null;
    }
}
