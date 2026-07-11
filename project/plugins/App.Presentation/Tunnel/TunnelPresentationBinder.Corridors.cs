using System;
using System.Collections.Generic;
using FantaSim.App.Timeline;
using FantaSim.App.Timeline.Seam;
using FantaSim.App.World.Composition;
using Godot;

namespace FantaSim.App.Presentation.Tunnel;

internal sealed partial class TunnelPresentationBinder
{
    private static readonly Color CorridorActiveColor = new(0.30f, 0.55f, 0.62f);
    private static readonly Color CorridorInactiveColor = new(0.42f, 0.44f, 0.46f);
    private static readonly Color CorridorFocusColor = new(0.42f, 0.68f, 0.52f);
    private const double CorridorGapDeg = 2.0;

    private Node3D? _corridorsRoot;
    private Node3D? _fineRailRoot;
    private MeshInstance3D? _fineCursor;
    private readonly List<(MeshInstance3D Node, long Tick)> _frameNodes = new();
    private readonly List<(MeshInstance3D Node, LayerTrackDescriptor Descriptor, bool IsFocused)> _corridorNodes = new();
    private long _requestedFrameStartTick;
    private long _requestedFrameEndTick;
    private bool _hasRequestedFrameWindow;

    private void EnsureCorridorsRoot()
    {
        if (_mount is null)
            return;

        if (_corridorsRoot is null || !GodotObject.IsInstanceValid(_corridorsRoot))
        {
            _corridorsRoot = new Node3D { Name = "Corridors" };
            _mount.AddChild(_corridorsRoot);
        }
    }

    private void ClearCorridorsRoot()
    {
        _corridorsRoot = null;
        _fineRailRoot = null;
        _fineCursor = null;
        _frameNodes.Clear();
        _corridorNodes.Clear();
        _hasRequestedFrameWindow = false;
    }

    private void RebuildCorridors()
    {
        if (_disposed || _mount is null || !GodotObject.IsInstanceValid(_mount) || _ctl is null)
            return;

        _pendingCorridorRebuild = false;
        EnsureCorridorsRoot();
        ClearChildren(_corridorsRoot!);
        _frameNodes.Clear();
        _corridorNodes.Clear();
        _fineRailRoot = null;
        _fineCursor = null;

        var gen = _generation;
        var baseTick = _ctl.Tick;
        var coarse = TunnelScrubMapper.MapOuterAngleToTick(360d, baseTick, _ctl.MaxTick);
        var requestEnd = Math.Min(coarse.ClampedTargetTick, _ctl.MaxTick);
        _requestedFrameStartTick = baseTick;
        _requestedFrameEndTick = requestEnd;
        _hasRequestedFrameWindow = true;

        if (_sourceTracks.Count == 0)
        {
            UpdateInnerBinding(gen);
            UpdateInnerControlVisuals();
            return;
        }

        var window = TunnelCorridorLayout.BuildFocusedWindow(_sourceTracks, _focusIndex);

        foreach (var slot in window)
        {
            BuildCorridorSlot(slot, baseTick, coarse.Rung.UnitTicks, requestEnd, gen);
        }

        UpdateInnerBinding(gen);
        BuildFineRailIfFocused(window);
        UpdateInnerControlVisuals();
    }

    private void BuildCorridorSlot(
        TunnelCorridorLayout.TunnelTrackSlot slot,
        long baseTick,
        double coarseUnitTicks,
        long requestEnd,
        int gen)
    {
        if (_corridorsRoot is null || _ctl is null)
            return;

        var isActive = TunnelTrackActivity.IsActive(
            slot.Descriptor, _ctl.Tick, _ctl.GeosphereSchedule, _ctl.AtmosphereSchedule);

        var color = slot.IsFocused
            ? CorridorFocusColor
            : (isActive ? CorridorActiveColor : CorridorInactiveColor);

        var centerAngle = slot.CenterAngleDegrees;
        var start = centerAngle - (CorridorSpanDegrees / 2.0) + (CorridorGapDeg / 2.0);
        var span = Math.Max(0.0, CorridorSpanDegrees - CorridorGapDeg);

        var wallMesh = BuildCylinderSectorMesh(start, span, CorridorSurfaceRadius, MouthZ, ThroatZ);
        if (wallMesh is not null)
        {
            var wall = new MeshInstance3D
            {
                Name = $"Corridor_{SafeNodeName(slot.Descriptor.SphereId)}_{SafeNodeName(slot.Descriptor.LayerId)}",
                Mesh = wallMesh,
                MaterialOverride = BuildUnlitMaterial(color),
            };
            _corridorsRoot!.AddChild(wall);
            _corridorNodes.Add((wall, slot.Descriptor, slot.IsFocused));
        }

        var labelRad = Mathf.DegToRad((float)centerAngle);
        var labelPos = new Vector3(
            Mathf.Cos(labelRad) * (CorridorSurfaceRadius - 0.3f),
            Mathf.Sin(labelRad) * (CorridorSurfaceRadius - 0.3f),
            0.1f);
        var label = new Label3D
        {
            Name = "CorridorLabel",
            Text = slot.Descriptor.DisplayName,
            Position = labelPos,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 22,
            Modulate = new Color(0.92f, 0.94f, 0.97f, 0.92f),
            OutlineModulate = new Color(0f, 0f, 0f, 0.65f),
            NoDepthTest = false,
        };
        _corridorsRoot!.AddChild(label);

        if (slot.Descriptor.Content.Type == "filmstrip")
            BuildFilmstripFrames(slot.Descriptor, centerAngle, baseTick, coarseUnitTicks, requestEnd, gen);
    }

    private void BuildFilmstripFrames(
        LayerTrackDescriptor descriptor,
        double centerAngleDeg,
        long baseTick,
        double coarseUnitTicks,
        long requestEnd,
        int gen)
    {
        if (_corridorsRoot is null || _ctl is null || coarseUnitTicks <= 0)
            return;

        var requestStart = baseTick;
        if (requestEnd <= requestStart)
            return;

        var contentWidth = TimelineFilmstrip.ThumbnailWidth * FilmstripFramesPerCorridor;
        var slots = TimelineFilmstrip.PlanSlots(requestStart, requestEnd, contentWidth);

        var rad = Mathf.DegToRad((float)centerAngleDeg);
        var rung = TunnelCorridorLayout.ResolveCorridorRung(descriptor.TimeDomain.Rung, TunnelScrubMapper.ResolveOuterRung());

        foreach (var fs in slots)
        {
            var fraction = coarseUnitTicks > 0
                ? (double)(fs.Tick - baseTick) / coarseUnitTicks
                : 0;
            if (fraction < 0 || fraction > 1)
                continue;

            var z = MouthZ - (float)(fraction * TunnelDepth);
            var tangentX = -Mathf.Sin(rad);
            var tangentY = Mathf.Cos(rad);

            var frameCenter = new Vector3(
                Mathf.Cos(rad) * (CorridorSurfaceRadius - 0.05f),
                Mathf.Sin(rad) * (CorridorSurfaceRadius - 0.05f),
                z);

            var halfW = 0.5f;
            var halfH = 0.35f;
            var tangent = new Vector3(tangentX, tangentY, 0f);
            var axial = Vector3.Back;

            // Local tangent-by-axial quad: node position owns the real tick's XYZ placement, so
            // later base-time refreshes can replace Position.Z without double-applying mesh Z.
            var q0 = -tangent * halfW - axial * halfH;
            var q1 = -tangent * halfW + axial * halfH;
            var q2 = tangent * halfW + axial * halfH;
            var q3 = tangent * halfW - axial * halfH;

            var mesh = BuildQuadMesh(q0, q1, q2, q3);
            var material = new StandardMaterial3D
            {
                AlbedoColor = Colors.White,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                Transparency = BaseMaterial3D.TransparencyEnum.Disabled,
            };
            var owner = new MeshInstance3D
            {
                Name = $"Frame_{SafeNodeName(descriptor.SphereId)}_{SafeNodeName(descriptor.LayerId)}_{fs.Tick}",
                Mesh = mesh,
                MaterialOverride = material,
                Position = frameCenter,
            };
            _corridorsRoot!.AddChild(owner);
            _frameNodes.Add((owner, fs.Tick));

            var sink = new QuadMaterialFilmstripSink(owner, material);
            _filmstrip.RequestTexture(sink, descriptor.SphereId, descriptor.LayerId, fs.Tick, rung.Symbol, graphRevision: 0);
        }
    }

    private static ArrayMesh BuildQuadMesh(Vector3 q0, Vector3 q1, Vector3 q2, Vector3 q3)
    {
        var normal = CalculateNormal(q0, q1, q2);
        var vertices = new List<Vector3> { q0, q1, q2, q0, q2, q3 };
        var normals = new List<Vector3> { normal, normal, normal, normal, normal, normal };
        var uvs = new List<Vector2>
        {
            new(0f, 1f), new(0f, 0f), new(1f, 0f),
            new(0f, 1f), new(1f, 0f), new(1f, 1f),
        };
        return BuildMeshFromArrays(vertices, normals, uvs);
    }

    private void BuildFineRailIfFocused(IReadOnlyList<TunnelCorridorLayout.TunnelTrackSlot> window)
    {
        if (_corridorsRoot is null)
            return;

        var focused = System.Linq.Enumerable.FirstOrDefault(window, s => s.IsFocused);
        if (focused.Descriptor is null)
            return;

        _fineRailRoot = new Node3D { Name = "FineRail" };
        _corridorsRoot.AddChild(_fineRailRoot);
        var centerAngle = focused.CenterAngleDegrees;

        var railMesh = BuildCylinderSectorMesh(
            centerAngle - 2.0, 4.0, CorridorSurfaceRadius - 0.15f,
            FineRailCenterZ + FineRailHalfLength,
            FineRailCenterZ - FineRailHalfLength);
        if (railMesh is not null)
        {
            var rail = new MeshInstance3D
            {
                Name = "FineRailBar",
                Mesh = railMesh,
                MaterialOverride = BuildUnlitMaterial(new Color(0.6f, 0.6f, 0.65f, 0.4f)),
            };
            _fineRailRoot.AddChild(rail);
        }

        var cursorMesh = BuildPlanarAnnulusSectorMesh(
            centerAngle - 4.0, 8.0,
            CorridorSurfaceRadius - 0.25f, CorridorSurfaceRadius - 0.05f, 0f);
        if (cursorMesh is not null)
        {
            _fineCursor = new MeshInstance3D
            {
                Name = "FineCursor",
                Mesh = cursorMesh,
                MaterialOverride = BuildUnlitMaterial(new Color(0.95f, 0.85f, 0.3f)),
                Position = new Vector3(0f, 0f, (float)_finePreview.CursorZ),
            };
            _fineRailRoot.AddChild(_fineCursor);
        }
    }

    private void UpdateInnerBinding(int gen)
    {
        if (_disposed || gen != _generation)
            return;

        var focused = TunnelCorridorLayout.ResolveFocusedTrack(_sourceTracks, _focusIndex);
        var isActive = false;
        if (focused is not null && _ctl is not null)
        {
            isActive = TunnelTrackActivity.IsActive(
                focused, _ctl.Tick, _ctl.GeosphereSchedule, _ctl.AtmosphereSchedule);
        }

        var globalFallback = TunnelScrubMapper.ResolveOuterRung();
        _fineBinding = TunnelFinePreviewMapper.Bind(focused, isActive, globalFallback);
        _finePreview = TunnelFinePreviewMapper.Reset(_fineBinding, FineRailCenterZ, FineRailHalfLength);
    }

    private void RepositionExistingFrames(long baseTick, double coarseSpanTicks, long requestEndTick)
    {
        if (_corridorsRoot is null || coarseSpanTicks <= 0)
            return;

        foreach (var (node, tick) in _frameNodes)
        {
            if (!GodotObject.IsInstanceValid(node))
                continue;

            var fraction = (double)(tick - baseTick) / coarseSpanTicks;
            if (tick < baseTick || tick > requestEndTick || fraction < 0 || fraction > 1)
            {
                node.Visible = false;
                continue;
            }

            node.Visible = true;
            var z = MouthZ - (float)(fraction * TunnelDepth);
            var pos = node.Position;
            node.Position = new Vector3(pos.X, pos.Y, z);
        }
    }

    private void OnTickChanged(long tick)
    {
        if (_disposed || _tearingDown)
            return;

        if (OS.GetThreadCallerId() == OS.GetMainThreadId())
        {
            RefreshTunnelForBaseTick(tick, rebuildFrameRequests: false);
            return;
        }

        var gen = _generation;
        Callable.From(() =>
        {
            if (gen == _generation)
                RefreshTunnelForBaseTick(tick, rebuildFrameRequests: false);
        }).CallDeferred();
    }

    private void RefreshTunnelForBaseTick(long tick, bool rebuildFrameRequests)
    {
        if (_disposed || _tearingDown || _ctl is null)
            return;

        UpdateInnerBinding(_generation);
        UpdateCorridorActivityStyles(tick);
        UpdateInnerControlVisuals();

        if (_outerLabel is not null && GodotObject.IsInstanceValid(_outerLabel))
            _outerLabel.Text = BuildOuterLabelText();

        if (rebuildFrameRequests)
        {
            _pendingCorridorRebuild = false;
            _filmstrip.Supersede();
            RebuildCorridors();
        }
        else
        {
            var coarse = TunnelScrubMapper.MapOuterAngleToTick(360d, tick, _ctl.MaxTick);
            var requestEnd = Math.Min(coarse.ClampedTargetTick, _ctl.MaxTick);
            RepositionExistingFrames(tick, coarse.Rung.UnitTicks, requestEnd);

            if (!_applyingOuterScrubAction
                && (!_hasRequestedFrameWindow
                    || tick < _requestedFrameStartTick
                    || tick > _requestedFrameEndTick))
            {
                ScheduleCorridorRequestRebuild();
            }
        }
    }

    private void UpdateCorridorActivityStyles(long tick)
    {
        if (_ctl is null)
            return;

        foreach (var (node, descriptor, isFocused) in _corridorNodes)
        {
            if (!GodotObject.IsInstanceValid(node))
                continue;

            var active = TunnelTrackActivity.IsActive(
                descriptor, tick, _ctl.GeosphereSchedule, _ctl.AtmosphereSchedule);
            var color = isFocused
                ? CorridorFocusColor
                : (active ? CorridorActiveColor : CorridorInactiveColor);
            node.MaterialOverride = BuildUnlitMaterial(color);
        }
    }

    private void ScheduleCorridorRequestRebuild()
    {
        if (_pendingCorridorRebuild || !_enabled || !_builtOnce)
            return;

        _pendingCorridorRebuild = true;
        var expectedGeneration = _generation;
        Callable.From(() =>
        {
            if (_disposed || _tearingDown || expectedGeneration != _generation)
                return;
            if (!_pendingCorridorRebuild)
                return;
            if (!_enabled)
            {
                _pendingCorridorRebuild = false;
                return;
            }

            _pendingCorridorRebuild = false;
            _filmstrip.Supersede();
            RebuildCorridors();
        }).CallDeferred();
    }

    private void OnRegistryChanged(LayerTrackRegistrySnapshot snapshot)
    {
        if (_disposed || _tearingDown)
            return;

        if (OS.GetThreadCallerId() == OS.GetMainThreadId())
        {
            HandleRegistryChanged();
            return;
        }

        var gen = _generation;
        Callable.From(() =>
        {
            if (!_tearingDown && gen == _generation)
                HandleRegistryChanged();
        }).CallDeferred();
    }

    private void HandleRegistryChanged()
    {
        if (_disposed || _tearingDown)
            return;

        CancelTunnelGesture("registry_changed");
        _filmstrip.Supersede();
        ResolveSourceTracks();
        RebuildCorridors();
        ResetFinePreview(TunnelFineResetReason.FocusChanged);
    }
}
