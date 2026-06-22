using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using BoomHud.Abstractions.Runtime;
using FantaSim.App.Ui;
using FantaSim.App.World.Composition;

namespace FantaSim.App.Timeline;

public sealed class TimelineViewSource : IViewSource, IDisposable
{
    private readonly ITimelineController _ctl;
    private readonly Action<long> _onTick;   // keep delegate identity for unsubscribe
    private string? _lastGeoRegimeId;
    private const double TrackWidth = 760.0;  // px the band row spans
    private const double MinBand = 6.0;        // floor so a tiny regime (magma ~0.8%) stays visible

    public TimelineViewSource(ITimelineController controller)
    {
        _ctl = controller ?? throw new ArgumentNullException(nameof(controller));
        _onTick = OnTick;
        _ctl.TickChanged += _onTick;
    }

    private void OnTick(long tick)
    {
        var rid = _ctl.GeosphereSchedule.RegimeAt(tick)?.RegimeId;
        if (rid != _lastGeoRegimeId)
        {
            _lastGeoRegimeId = rid;
            Changed?.Invoke();
        }
    }

    public void Dispose() => _ctl.TickChanged -= _onTick;

    public string ViewId => "timeline";
    public event Action? Changed;

    public RuntimeSurfaceDocument BuildDocument()
    {
        long tick = _ctl.Tick;
        var children = new List<RuntimeComponentNode> { Header(tick) };
        children.Add(SphereSection("geosphere", _ctl.GeosphereSchedule, tick));
        children.Add(SphereSection("atmosphere", _ctl.AtmosphereSchedule, tick));

        return new RuntimeSurfaceDocument
        {
            SurfaceId = "timeline",
            CatalogId = "boomhud.runtime.basic.v1",
            Revision = 1,
            Root = new RuntimeComponentNode
            {
                Id = "root", Type = "container",
                Layout = new RuntimeLayoutSpec { Type = "vertical", Gap = 6, Padding = 8 },
                Children = children,
            },
        };
    }

    private RuntimeComponentNode Header(long tick)
    {
        var geo = _ctl.GeosphereSchedule.RegimeAt(tick)?.RegimeId ?? "—";
        return new RuntimeComponentNode
        {
            Id = "header", Type = "container",
            Layout = new RuntimeLayoutSpec { Type = "horizontal", Gap = 8 },
            Children = new[]
            {
                Button("btn-playpause", _ctl.IsPlaying ? "⏸ Pause" : "▶ Play", _ctl.IsPlaying ? "timeline.pause" : "timeline.play"),
                Label("lbl-tick", $"{(_ctl.IsPlaying ? "▶ playing" : "Ⅱ paused")} · {geo}"),
            },
        };
    }

    private RuntimeComponentNode SphereSection(string sphere, SphereRegimeSchedule schedule, long tick)
    {
        var bands = TimelineModel.Bands(schedule, _ctl.MaxTick, tick);
        var tracks = TimelineModel.Tracks(schedule, tick);

        var bandRow = new RuntimeComponentNode
        {
            Id = $"bands-{sphere}", Type = "container",
            Layout = new RuntimeLayoutSpec { Type = "horizontal", Gap = 2 },
            Children = bands.Select(b => new RuntimeComponentNode
            {
                Id = $"band-{sphere}-{b.RegimeId}", Type = "button",   // button so region-jump works
                Layout = new RuntimeLayoutSpec { Width = Math.Max(MinBand, b.WidthFraction * TrackWidth) },
                Properties = new Dictionary<string, RuntimeValue>
                {
                    ["text"] = Lit(b.IsActive ? $"▮ {b.RegimeId}" : b.RegimeId),
                    ["variant"] = Lit(b.IsActive ? b.Variant : b.Variant + "-dim"),
                },
                Actions = new[] { new RuntimeActionDescriptor { Event = "pressed", Command = $"timeline.seek:{SeekTickFor(schedule, b.RegimeId)}" } },
            }).ToArray(),
        };

        var trackRows = tracks.Select(t => new RuntimeComponentNode
        {
            Id = $"track-{sphere}-{t.LayerId}", Type = "badge",
            Properties = new Dictionary<string, RuntimeValue>
            {
                ["text"] = Lit(t.LayerId),
                ["variant"] = Lit(t.IsActive ? "success" : "muted"),
            },
        }).ToArray();

        return new RuntimeComponentNode
        {
            Id = $"sphere-{sphere}", Type = "panel",
            Properties = new Dictionary<string, RuntimeValue> { ["title"] = Lit(sphere) },
            Layout = new RuntimeLayoutSpec { Type = "vertical", Gap = 4, Padding = 6 },
            Children = new[] { bandRow }.Concat(trackRows).ToArray(),
        };
    }

    private static long SeekTickFor(SphereRegimeSchedule s, string regimeId) =>
        s.Regimes.FirstOrDefault(r => r.RegimeId == regimeId)?.StartTick ?? 0;

    public void Dispatch(string action, string? componentId)
    {
        if (action == "timeline.play") { _ctl.Play(); Changed?.Invoke(); }
        else if (action == "timeline.pause") { _ctl.Pause(); Changed?.Invoke(); }
        else if (action.StartsWith("timeline.seek:", StringComparison.Ordinal)
                 && long.TryParse(action["timeline.seek:".Length..], out var t)) _ctl.SeekTo(t);
    }

    private static RuntimeComponentNode Button(string id, string text, string command) => new()
    {
        Id = id, Type = "button",
        Properties = new Dictionary<string, RuntimeValue> { ["text"] = Lit(text) },
        Actions = new[] { new RuntimeActionDescriptor { Event = "pressed", Command = command } },
    };
    private static RuntimeComponentNode Label(string id, string text) => new()
    { Id = id, Type = "label", Properties = new Dictionary<string, RuntimeValue> { ["text"] = Lit(text) } };
    private static RuntimeValue Lit(string s) => new() { Literal = JsonValue.Create(s) };
}
