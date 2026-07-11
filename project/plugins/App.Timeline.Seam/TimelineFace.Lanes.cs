using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using FantaSim.App.World;
using FantaSim.App.World.Composition;
using FantaSim.App.Timeline;
using FantaSim.App.Command;
using FantaSim.App.Ui.Seam;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;

namespace FantaSim.App.Timeline.Seam;

public partial class TimelineFace
{
    private sealed class TrackRowBinding
    {
        public required Control RowRoot { get; init; }
        public required Button ToggleButton { get; init; }
        public required Button ChevronButton { get; init; }
        public required Control ContentRoot { get; init; }
        public required string LayerId { get; init; }
        public required string Sphere { get; init; }
        public required StyleBoxFlat NormalStyle { get; init; }
        public required StyleBoxFlat InactiveStyle { get; init; }
        public required StyleBoxFlat SelectedStyle { get; init; }
        public required Callable ToggleCallable { get; init; }
        public required Callable ChevronCallable { get; init; }
        public IDisposable? GraphBinding { get; set; }
    }

    private sealed record TrackGraphPortItem(string PortId, string Label, string KindHint, bool Required);

    private sealed record TrackGraphNodeItem(
        string NodeId,
        string TypeId,
        int InputCount,
        int OutputCount,
        string Category,
        string TypeKey,
        string Summary,
        string Detail,
        bool IsSideEffect,
        bool IsExpensive,
        IReadOnlyList<TrackGraphPortItem> Inputs,
        IReadOnlyList<TrackGraphPortItem> Outputs,
        IReadOnlyList<string> ParameterLines);

    private sealed record TrackGraphWireItem(
        string FromNodeId,
        int FromSlot,
        string ToNodeId,
        int ToSlot,
        string FromPortId,
        string ToPortId,
        string KindHint);

    private sealed class TrackGraphEditViewModel
    {
        public TrackGraphEditViewModel(LayerTrackGraphView graph)
        {
            Nodes = graph.Nodes.Select(node => new TrackGraphNodeItem(
                    node.NodeId,
                    node.TypeId,
                    node.InputCount,
                    node.OutputCount,
                    node.Category,
                    node.TypeId,
                    node.Summary,
                    string.Join('\n', node.ParameterLines),
                    IsSideEffect: false,
                    IsExpensive: false,
                    node.Inputs.Select(port => new TrackGraphPortItem(port.PortId, port.Label, port.KindHint, port.Required)).ToArray(),
                    node.Outputs.Select(port => new TrackGraphPortItem(port.PortId, port.Label, port.KindHint, port.Required)).ToArray(),
                    node.ParameterLines))
                .ToArray();
            Wires = graph.Wires.Select(wire => new TrackGraphWireItem(
                    wire.FromNodeId,
                    wire.FromSlot,
                    wire.ToNodeId,
                    wire.ToSlot,
                    wire.FromPortId,
                    wire.ToPortId,
                    wire.KindHint))
                .ToArray();
        }

        public bool CompactCards => true;
        public IReadOnlyList<TrackGraphNodeItem> Nodes { get; }
        public IReadOnlyList<TrackGraphWireItem> Wires { get; }
    }

    private readonly HashSet<string> _expandedTracks = new(StringComparer.Ordinal);
    private WorldGenerationGraphFamilyDocument? _cachedGraphFamily;
    private long? _cachedGraphFamilyTick;

    private void UpdateLayout()
    {
        if (_lanesContainer is null) return;
        var width = _lanesContainer.Size.X;

        foreach (var bandList in _bandsBySphere.Values)
        {
            foreach (var band in bandList)
            {
                band.Button.Position = new Vector2((float)(band.Start * width), 0);
                band.Button.Size = new Vector2((float)(band.Width * width), RegimeBandHeight);
            }
        }

        UpdateTrackContentLayout(width);
        UpdateUI();
        UpdateRuler();
    }

    private void UpdateTrackContentLayout(float laneWidth)
    {
        float contentWidth = ResolveTrackContentWidth(laneWidth);
        foreach (var track in _tracks)
        {
            float rowHeight = Math.Max(track.RowRoot.CustomMinimumSize.Y, track.RowRoot.Size.Y);
            track.ContentRoot.Position = new Vector2(TrackHeaderWidth + TrackContentGap, 0f);
            track.ContentRoot.Size = new Vector2(contentWidth, rowHeight);
            track.ContentRoot.CustomMinimumSize = new Vector2(contentWidth, rowHeight);

            var strip = track.ContentRoot.GetNodeOrNull<Control>("CompactFilmstrip");
            if (strip is not null)
                strip.Size = new Vector2(contentWidth, TrackHeight);
        }
    }

    private static readonly LayerTrackRegistrySnapshot EmptyLayerTrackRegistrySnapshot =
        new(Revision: 0, Tracks: Array.Empty<LayerTrackDescriptor>());

    // Known sphereId -> regime-schedule bindings. Slice 1 has exactly these two schedules on
    // ITimelineController; a sphereId absent from this table renders its lane with tracks but no
    // regime band, per the declared-always contract
    // (vault/specs/2026-07-10-layer-track-registry-design.md). This lookup table is the one place
    // sphere-id literals may still appear -- the LANE LIST ITSELF is registry-driven (BuildLanes
    // below iterates TrackLaneViewModelBuilder's grouping, never a fixed geosphere/atmosphere pair).
    private SphereRegimeSchedule? ResolveScheduleForSphere(string sphereId) => sphereId switch
    {
        "geosphere" => _ctl?.GeosphereSchedule,
        "atmosphere" => _ctl?.AtmosphereSchedule,
        _ => null,
    };

    private void BuildLanes()
    {
        if (_ctl is null || _lanesContainer is null) return;
        _filmstrip.Supersede();

        var lanesList = GetNode<Control>("VBoxContainer/LanesContainer/LanesList");
        DisposeTrackBindings();
        ClearChildren(lanesList);
        _bandsBySphere.Clear();

        if (_ctl.MaxTick <= 0L) return;

        var snapshot = _layerTrackRegistry?.Current ?? EmptyLayerTrackRegistrySnapshot;
        var lanes = TrackLaneViewModelBuilder.BuildLanes(snapshot);
        var generationFamily = ResolveGenerationGraphFamily();

        foreach (var lane in lanes)
            BuildLane(lane, lanesList, generationFamily);

        UpdateLanesMinimumHeight();
    }

    private void BuildLane(
        TrackLaneViewModel lane,
        Control lanesList,
        WorldGenerationGraphFamilyDocument? generationFamily)
    {
        var laneRoot = new VBoxContainer { Name = $"Lane_{SafeNodeName(lane.SphereId)}" };
        lanesList.AddChild(laneRoot);

        var title = new Label { Text = FriendlyLayerLabel(lane.SphereId) };
        laneRoot.AddChild(title);

        var regimesRoot = new Control { CustomMinimumSize = new Vector2(0, RegimeBandHeight) };
        laneRoot.AddChild(regimesRoot);

        var tracksRoot = new VBoxContainer();
        laneRoot.AddChild(tracksRoot);

        var bandList = new List<(Button Button, double Start, double Width)>();
        _bandsBySphere[lane.SphereId] = bandList;

        var schedule = ResolveScheduleForSphere(lane.SphereId);
        if (schedule is not null)
            BuildLaneBands(schedule, regimesRoot, bandList);

        BuildLaneTracks(lane, tracksRoot, schedule, generationFamily);
    }

    private void BuildLaneBands(
        SphereRegimeSchedule schedule,
        Control regimesRoot,
        List<(Button Button, double Start, double Width)> bandList)
    {
        var bands = TimelineModel.Bands(schedule, _ctl!.MaxTick, _ctl.Tick, _viewStartTick, _viewEndTick);
        foreach (var b in bands)
        {
            var btn = new Button
            {
                Text = b.RegimeId,
                ClipText = true,
                FocusMode = FocusModeEnum.None
            };
            btn.AddThemeFontSizeOverride("font_size", 12);

            var normalStyle = new StyleBoxFlat();
            normalStyle.BgColor = GetRegimeColor(b.RegimeId);
            normalStyle.BorderColor = new Color(0.95f, 0.98f, 1.0f, 0.55f);
            normalStyle.SetBorderWidthAll(1);
            normalStyle.SetCornerRadiusAll(3);
            btn.AddThemeStyleboxOverride("normal", normalStyle);
            btn.AddThemeStyleboxOverride("hover", normalStyle);
            btn.AddThemeStyleboxOverride("pressed", normalStyle);

            btn.Pressed += () => OnBandPressed(b.StartTick);

            regimesRoot.AddChild(btn);
            bandList.Add((btn, b.StartFraction, b.WidthFraction));
        }
    }

    private void BuildLaneTracks(
        TrackLaneViewModel lane,
        Control tracksRoot,
        SphereRegimeSchedule? schedule,
        WorldGenerationGraphFamilyDocument? generationFamily)
    {
        var trackLayouts = TimelineTrackLayout.ToRowMap(TimelineTrackLayout.Plan(
            lane.Tracks.Select(track => new TimelineTrackLayoutInput(
                TrackKey(lane.SphereId, track.Descriptor.LayerId),
                IsExpanded: _expandedTracks.Contains(TrackKey(lane.SphereId, track.Descriptor.LayerId)))),
            TrackHeight,
            ExpandedTrackHeight));

        foreach (var t in lane.Tracks)
        {
            var trackSphere = lane.SphereId;
            var trackLayerId = t.Descriptor.LayerId;
            var trackKey = TrackKey(trackSphere, trackLayerId);
            var rowLayout = trackLayouts[trackKey];
            var expanded = _expandedTracks.Contains(trackKey);
            var regimeId = schedule is not null ? ResolveTrackRegime(schedule, trackLayerId, _ctl!.Tick) : null;
            var graph = LayerTrackGraphProjection.Resolve(generationFamily, trackSphere, trackLayerId, regimeId);
            // GraphRevision reuses the family document ALREADY resolved above for the graph
            // projection -- no new call into the (expensive) generation-graph-family provider.
            // Threaded through so the filmstrip cache key can include it (2026-07-11 cache-key
            // completion, vault/specs/2026-07-11-surrealdb-persistence-slice1-design.md §1.3).
            var graphRevision = generationFamily?.Revision ?? 0;

            var row = new Control
            {
                Name = $"TrackRow_{SafeNodeName(trackLayerId)}",
                CustomMinimumSize = new Vector2(0, rowLayout.Height),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                MouseFilter = MouseFilterEnum.Ignore,
            };

            var chevron = new Button
            {
                Text = expanded ? "v" : ">",
                TooltipText = expanded ? "Collapse layer graph" : "Expand layer graph",
                FocusMode = FocusModeEnum.None,
                ClipText = true,
                MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            };
            chevron.AddThemeFontSizeOverride("font_size", 12);
            ConfigureTrackRowChild(chevron, 0f, TrackChevronWidth, TrackHeight);
            row.AddChild(chevron);

            var btn = new Button
            {
                Text = $" {FriendlyLayerLabel(trackLayerId)}",
                TooltipText = $"{trackSphere}:{trackLayerId}",
                Alignment = HorizontalAlignment.Left,
                FocusMode = FocusModeEnum.None,
                ClipText = true,
            };
            btn.AddThemeFontSizeOverride("font_size", 12);
            btn.AddThemeColorOverride("font_color", new Color(0.94f, 0.96f, 0.98f, 0.98f));
            btn.AddThemeColorOverride("font_hover_color", new Color(1.0f, 1.0f, 1.0f, 1.0f));
            ConfigureTrackRowChild(btn, TrackChevronWidth, TrackHeaderWidth - TrackChevronWidth, TrackHeight);

            var normalStyle = new StyleBoxFlat { BgColor = new Color(0.14f, 0.20f, 0.24f, 0.78f) };
            normalStyle.SetBorderWidthAll(1);
            normalStyle.BorderColor = new Color(0.28f, 0.42f, 0.48f, 0.86f);
            normalStyle.SetCornerRadiusAll(3);

            var inactiveStyle = new StyleBoxFlat { BgColor = new Color(0.08f, 0.10f, 0.12f, 0.72f) };
            inactiveStyle.SetBorderWidthAll(1);
            inactiveStyle.BorderColor = new Color(0.20f, 0.24f, 0.28f, 0.74f);
            inactiveStyle.SetCornerRadiusAll(3);

            var selectedStyle = new StyleBoxFlat { BgColor = new Color(0.20f, 0.33f, 0.46f, 0.92f) };
            selectedStyle.SetBorderWidthAll(2);
            selectedStyle.BorderColor = new Color(0.58f, 0.82f, 1.00f, 0.98f);
            selectedStyle.SetCornerRadiusAll(3);

            btn.AddThemeStyleboxOverride("normal", normalStyle);
            btn.AddThemeStyleboxOverride("hover", selectedStyle);
            btn.AddThemeStyleboxOverride("pressed", selectedStyle);

            row.AddChild(btn);

            var content = new Control
            {
                Name = "GraphContent",
                MouseFilter = expanded ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore,
                ClipContents = true,
                Modulate = t.IsDimmed ? DimmedTrackModulate : Colors.White,
            };
            ConfigureTrackContent(content, ResolveTrackContentWidth(_lanesContainer?.Size.X ?? 0f), rowLayout.Height);
            row.AddChild(content);

            IDisposable? graphBinding = null;
            if (expanded)
                graphBinding = BuildExpandedGraph(content, graph);
            else
                RenderTrackContent(t, content, trackSphere, trackLayerId, schedule, graph, graphRevision);

            var toggleCallable = Callable.From(() => OnTrackPressed(trackSphere, trackLayerId));
            var chevronCallable = Callable.From(() => OnTrackExpandPressed(trackSphere, trackLayerId));
            btn.Connect(BaseButton.SignalName.Pressed, toggleCallable);
            chevron.Connect(BaseButton.SignalName.Pressed, chevronCallable);

            tracksRoot.AddChild(row);
            _tracks.Add(new TrackRowBinding
            {
                RowRoot = row,
                ToggleButton = btn,
                ChevronButton = chevron,
                ContentRoot = content,
                LayerId = trackLayerId,
                Sphere = trackSphere,
                NormalStyle = normalStyle,
                InactiveStyle = inactiveStyle,
                SelectedStyle = selectedStyle,
                ToggleCallable = toggleCallable,
                ChevronCallable = chevronCallable,
                GraphBinding = graphBinding,
            });
        }
    }

    private const float DimmedTrackModulateAlpha = 0.55f;
    private static readonly Color DimmedTrackModulate = new(1f, 1f, 1f, DimmedTrackModulateAlpha);

    private sealed record TrackContentRenderContext(
        Control Content,
        string SphereId,
        string LayerId,
        SphereRegimeSchedule? Schedule,
        LayerTrackGraphView Graph,
        LayerTrackDescriptor Descriptor,
        int GraphRevision);

    // Content strip rendering dispatch, keyed by TrackLaneViewModelBuilder's presenter-kind
    // resolution (itself keyed by descriptor.Content.Type -- Task 4's presenter lookup). A
    // presenter kind with no registered entry falls back to the generic strip, never throws.
    private void RenderTrackContent(
        TrackRowViewModel track,
        Control content,
        string sphereId,
        string layerId,
        SphereRegimeSchedule? schedule,
        LayerTrackGraphView graph,
        int graphRevision)
    {
        var context = new TrackContentRenderContext(content, sphereId, layerId, schedule, graph, track.Descriptor, graphRevision);
        if (_trackContentPresenters.TryGetValue(track.PresenterKind, out var presenter))
            presenter(context);
        else
            RenderGenericTrackContent(context);
    }

    private void RenderFilmstripTrackContent(TrackContentRenderContext context)
        => BuildCompactFilmstrip(context.Content, context.Schedule, context.SphereId, context.LayerId, context.GraphRevision);

    // Existing D7c chip/graph path: a small label summarizing the resolved layer graph. The
    // chevron expand-to-full-GraphEdit affordance (BuildExpandedGraph) is separate and works for
    // every track regardless of presenter kind, so this stays a compact chip, not a live editor.
    private void RenderGraphTrackContent(TrackContentRenderContext context)
    {
        context.Content.MouseFilter = MouseFilterEnum.Ignore;
        var hasNodes = context.Graph.Nodes.Count > 0;
        var summary = hasNodes
            ? $"{context.Graph.Label} ({context.Graph.Nodes.Count} node{(context.Graph.Nodes.Count == 1 ? "" : "s")})"
            : "graph unavailable";
        var label = CompactStripLabel(summary, muted: !hasNodes);
        label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        label.VerticalAlignment = VerticalAlignment.Center;
        context.Content.AddChild(label);
    }

    // Generic fallback for any content.Type with no dedicated presenter (including
    // LayerTrackContentTypes.DeclaredEmpty) -- the Unity round-trip degradation guarantee: never
    // invisible, never a crash, richer only when a presenter exists.
    private void RenderGenericTrackContent(TrackContentRenderContext context)
    {
        context.Content.MouseFilter = MouseFilterEnum.Ignore;
        var label = CompactStripLabel(context.Descriptor.DisplayName, muted: true);
        label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        label.VerticalAlignment = VerticalAlignment.Center;
        context.Content.AddChild(label);
    }

    private void UpdateLanesMinimumHeight()
    {
        if (_lanesContainer is null)
            return;

        var lanesList = GetNodeOrNull<Control>("VBoxContainer/LanesContainer/LanesList");
        if (lanesList is null)
            return;

        var minHeight = Math.Max(120f, lanesList.GetCombinedMinimumSize().Y);
        _lanesContainer.CustomMinimumSize = new Vector2(0f, minHeight);
    }

    private static void ConfigureTrackRowChild(Control child, float left, float width, float height)
    {
        child.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        child.Position = new Vector2(left, 0f);
        child.Size = new Vector2(width, height);
        child.CustomMinimumSize = new Vector2(width, height);
    }

    private static void ConfigureTrackContent(Control content, float width, float height)
    {
        content.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        content.Position = new Vector2(TrackHeaderWidth + TrackContentGap, 0f);
        content.Size = new Vector2(width, height);
        content.CustomMinimumSize = new Vector2(width, height);
    }

    private static float ResolveTrackContentWidth(float laneWidth)
        => Math.Max(1f, laneWidth - TrackHeaderWidth - TrackContentGap);

    private void BuildCompactFilmstrip(
        Control content,
        SphereRegimeSchedule? schedule,
        string sphere,
        string layerId,
        int graphRevision)
    {
        content.MouseFilter = MouseFilterEnum.Ignore;
        var contentWidth = ResolveTrackContentWidth(_lanesContainer?.Size.X ?? content.Size.X);
        var strip = new Control
        {
            Name = "CompactFilmstrip",
            MouseFilter = MouseFilterEnum.Ignore,
            ClipContents = true,
        };
        strip.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        strip.Position = Vector2.Zero;
        strip.Size = new Vector2(contentWidth, TrackHeight);
        strip.CustomMinimumSize = new Vector2(contentWidth, TrackHeight);
        content.AddChild(strip);

        var slots = TimelineFilmstrip.PlanSlots(
            _viewStartTick,
            _viewEndTick,
            contentWidth,
            TimelineFilmstrip.ThumbnailWidth);

        if (slots.Count == 0)
        {
            strip.AddChild(CompactStripLabel("preview unavailable", muted: true));
            return;
        }

        var rung = SelectedRung.Symbol;
        var orderedSlots = TimelineFilmstrip.OrderSlotsNearestToTick(slots, _ctl?.Tick ?? _viewStartTick);
        foreach (var slot in slots)
        {
            bool activeAtSlot = schedule?.RegimeAt(slot.Tick)?.ActiveLayers.Any(layer =>
                string.Equals(layer.Value, layerId, StringComparison.Ordinal)) == true;
            var frame = FilmstripPreviewController.BuildFramePlaceholder(slot, activeAtSlot);
            strip.AddChild(frame);
        }

        foreach (var slot in orderedSlots)
        {
            var frame = strip.GetNodeOrNull<Control>($"Frame_{slot.Index}");
            if (frame is null)
                continue;
            var textureRect = frame.GetNode<TextureRect>("Texture");
            _filmstrip.RequestTexture(textureRect, sphere, layerId, slot.Tick, rung, graphRevision);
        }
    }

    private IDisposable? BuildExpandedGraph(Control content, LayerTrackGraphView graph)
    {
        content.MouseFilter = MouseFilterEnum.Stop;
        if (graph.Nodes.Count == 0)
        {
            var label = CompactStripLabel(graph.Label, muted: true);
            label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            label.VerticalAlignment = VerticalAlignment.Center;
            content.AddChild(label);
            return null;
        }

        var graphEdit = new GraphEdit
        {
            Name = "LayerGraphEdit",
            MouseFilter = MouseFilterEnum.Stop,
            ClipContents = true,
        };
        graphEdit.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        graphEdit.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        graphEdit.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        content.AddChild(graphEdit);

        return EmbeddedNodeGraphRenderer.TryBindReadOnly(
            graphEdit,
            new TrackGraphEditViewModel(graph),
            _log);
    }

    private static Label CompactStripLabel(string text, bool muted)
    {
        var label = new Label
        {
            Text = text,
            ClipText = true,
            MouseFilter = MouseFilterEnum.Ignore,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.AddThemeFontSizeOverride("font_size", 10);
        label.AddThemeColorOverride("font_color", muted
            ? new Color(0.64f, 0.70f, 0.76f, 0.72f)
            : new Color(0.90f, 0.94f, 0.98f, 0.95f));
        return label;
    }

    private WorldGenerationGraphFamilyDocument? ResolveGenerationGraphFamily()
    {
        if (_ctl is null)
            return null;

        if (_cachedGraphFamilyTick == _ctl.Tick)
            return _cachedGraphFamily;

        _cachedGraphFamilyTick = _ctl.Tick;
        try
        {
            _cachedGraphFamily = _generationGraphFamilyProvider?.Invoke(_ctl.Tick);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Timeline layer graph family unavailable.");
            _cachedGraphFamily = null;
        }

        return _cachedGraphFamily;
    }

    private static string? ResolveTrackRegime(SphereRegimeSchedule schedule, string layerId, long currentTick)
    {
        var active = schedule.RegimeAt(currentTick);
        if (active is not null && HasLayer(active, layerId))
            return active.RegimeId;

        return schedule.Regimes.FirstOrDefault(regime => HasLayer(regime, layerId))?.RegimeId;
    }

    private static bool HasLayer(SphereRegime regime, string layerId)
        => regime.ActiveLayers.Any(layer => string.Equals(layer.Value, layerId, StringComparison.Ordinal));

    private void OnTrackExpandPressed(string sphere, string layerId)
    {
        var key = TrackKey(sphere, layerId);
        if (!_expandedTracks.Add(key))
            _expandedTracks.Remove(key);
        BuildLanes();
        UpdateLayout();
    }

    private async void OnTrackPressed(string sphere, string layerId)
    {
        if (_ctl is null || !IsLayerActive(sphere, layerId))
            return;

        var commandClient = _commandClient;
        if (commandClient is null)
        {
            _ctl.ToggleLayer(sphere, layerId);
            UpdateUI();
            return;
        }

        try
        {
            var schedule = ResolveScheduleForSphere(sphere);
            var payload = new JsonObject
            {
                ["sphereId"] = sphere,
                ["layerId"] = layerId,
                ["regimeId"] = schedule?.RegimeAt(_ctl.Tick)?.RegimeId,
            }.ToJsonString();
            var result = await commandClient.CommandAsync(new CommandRequest(
                Command: "timeline.toggle_layer",
                PayloadJson: payload,
                ActorKind: "user",
                ActorId: "godot"));
            if (!result.Ok)
            {
                _log.LogWarning(
                    "Timeline layer toggle command failed: {LayerId} ({Error})",
                    layerId,
                    result.Error?.Message ?? "unknown error");
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Timeline layer toggle command failed for {LayerId}.", layerId);
            _ctl.ToggleLayer(sphere, layerId);
        }

        UpdateUI();
    }

    private bool IsLayerActive(string sphere, string layerId)
    {
        if (_ctl is null)
            return false;

        return ResolveScheduleForSphere(sphere)?.RegimeAt(_ctl.Tick)?.ActiveLayers.Any(layer =>
            string.Equals(layer.Value, layerId, StringComparison.Ordinal)) == true;
    }

    private Color GetRegimeColor(string regimeId) => regimeId switch
    {
        "magma-ocean" => Color.FromHtml("#ff9800"),
        "stagnant-lid" => Color.FromHtml("#607d8b"),
        "mobile-plate" => Color.FromHtml("#008080"),
        "primordial-steam" or "secondary-co2" => Color.FromHtml("#1e88e5"),
        "coupled-climate" => Color.FromHtml("#008080"),
        _ => Color.FromHtml("#9e9e9e")
    };

    private void DisposeTrackBindings()
    {
        foreach (var track in _tracks)
        {
            track.GraphBinding?.Dispose();
            DisconnectIfConnected(track.ToggleButton, BaseButton.SignalName.Pressed, track.ToggleCallable);
            DisconnectIfConnected(track.ChevronButton, BaseButton.SignalName.Pressed, track.ChevronCallable);
        }

        _tracks.Clear();
    }
}