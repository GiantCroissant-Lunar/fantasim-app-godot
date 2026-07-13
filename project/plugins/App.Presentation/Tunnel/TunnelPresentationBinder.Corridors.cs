using System;
using System.Collections.Generic;
using FantaSim.App.Timeline;
using FantaSim.App.Timeline.Seam;
using FantaSim.App.World.Composition;
using Godot;
using Microsoft.Extensions.Logging;

namespace FantaSim.App.Presentation.Tunnel;

/// <summary>
/// Godot-free style policy for corridor wall, header, and current-plane cue. Keeping the
/// active/inactive/focused color computation in one place guarantees that <see cref="BuildCorridorHeader"/>,
/// <see cref="BuildCurrentPlaneCue"/>, and <see cref="UpdateCorridorActivityStyles"/> cannot drift.
/// </summary>
internal static class TunnelCorridorActivityStylePolicy
{
    public static readonly Color ActiveColor = new(0.30f, 0.55f, 0.62f);
    public static readonly Color InactiveColor = new(0.42f, 0.44f, 0.46f);
    public static readonly Color FocusColor = new(0.42f, 0.68f, 0.52f);

    public readonly record struct CorridorStyle(
        Color WallColor,
        Color CueColor,
        Color TitleColor,
        Color SubtitleColor);

    public static CorridorStyle Resolve(bool isActive, bool isFocused)
    {
        var wallColor = isFocused
            ? FocusColor
            : (isActive ? ActiveColor : InactiveColor);
        var titleColor = isFocused
            ? new Color(1.0f, 0.98f, 0.85f, 0.98f)
            : (isActive ? new Color(0.92f, 0.94f, 0.97f, 0.94f) : new Color(0.62f, 0.63f, 0.68f, 0.85f));
        var subtitleColor = isActive
            ? new Color(0.72f, 0.86f, 0.78f, 0.90f)
            : new Color(0.55f, 0.56f, 0.60f, 0.80f);

        return new CorridorStyle(wallColor, wallColor, titleColor, subtitleColor);
    }
}

internal sealed partial class TunnelPresentationBinder
{
    private static readonly Color CorridorActiveColor = TunnelCorridorActivityStylePolicy.ActiveColor;
    private static readonly Color CorridorInactiveColor = TunnelCorridorActivityStylePolicy.InactiveColor;
    private static readonly Color CorridorFocusColor = TunnelCorridorActivityStylePolicy.FocusColor;
    private static readonly Color CurrentPlaneCueColor = new(0.52f, 0.88f, 0.84f);
    private static readonly Color AxialCueColor = new(0.35f, 0.62f, 0.78f);
    private const double CorridorGapDeg = 2.0;

    private sealed record TunnelFrameBinding(
        long RequestedTick,
        LayerTrackDescriptor Descriptor,
        Node3D Root,
        SnapshotSphereMaterial Material,
        MeshInstance3D Sphere,
        Node3D Unavailable,
        SnapshotSphereFilmstripSink Sink,
        int MountGeneration);

    private sealed record CorridorWallBinding(
        MeshInstance3D Node,
        LayerTrackDescriptor Descriptor,
        bool IsFocused,
        float DepthFraction);

    private sealed record CorridorHeaderBinding(
        Label3D Title,
        Label3D Subtitle,
        LayerTrackDescriptor Descriptor,
        bool IsFocused);

    private sealed record CorridorCueBinding(
        Node3D Root,
        MeshInstance3D Slice,
        LayerTrackDescriptor Descriptor,
        bool IsFocused,
        bool IsActive);

    private Node3D? _corridorsRoot;
    private Node3D? _fineRailRoot;
    private MeshInstance3D? _fineCursor;
    private readonly List<TunnelFrameBinding> _frameBindings = new();
    private readonly List<CorridorWallBinding> _corridorNodes = new();
    private readonly List<CorridorHeaderBinding> _corridorHeaders = new();
    private readonly List<CorridorCueBinding> _corridorCues = new();

    // Last-applied corridor active-flag bitmask (one bit per corridor, LSB = first). -1 means
    // "unknown/invalidated" so the next style pass always applies. Reset wherever _corridorNodes is
    // rebuilt, since a given mask value only maps to the corridor set that produced it.
    private int _lastCorridorActivityMask = -1;
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
        // Every detach invalidates direct sink waiters before their Godot nodes are released. The
        // final Dispose path is the sole exception: SeverFilmstrip already superseded/cancelled and
        // disposed the controller immediately before ClearMount calls this reset.
        ReleaseFrameBindings(
            supersedeWaiters: TunnelCorridorFilmstripPolicy.ShouldSupersedeWaitersOnReset(_disposed));
        _corridorsRoot = null;
        _fineRailRoot = null;
        _fineCursor = null;
        _corridorNodes.Clear();
        _corridorHeaders.Clear();
        _corridorCues.Clear();
        _lastCorridorActivityMask = -1;
        _hasRequestedFrameWindow = false;
    }

    private void RebuildCorridors()
    {
        if (_disposed || _mount is null || !GodotObject.IsInstanceValid(_mount) || _ctl is null)
            return;

        // Every ordinary rebuild supersedes presentation-only inspection before replacing the
        // sphere/material bindings that its focus and emphasis guards reference.
        ResetFinePreview(TunnelFineResetReason.BaseTimeChanged);
        _pendingCorridorRebuild = false;
        EnsureCorridorsRoot();
        ReleaseFrameBindings(supersedeWaiters: true);
        ClearChildren(_corridorsRoot!);
        _corridorNodes.Clear();
        _corridorHeaders.Clear();
        _corridorCues.Clear();
        _lastCorridorActivityMask = -1;
        _fineRailRoot = null;
        _fineCursor = null;

        var gen = _generation;
        var baseTick = _ctl.Tick;
        var coarse = TunnelScrubMapper.MapOuterAngleToTick(360d, baseTick, _ctl.MaxTick);
        var coarseUnitTicks = TimelineModel.SpanTicksForRung(coarse.Rung, units: 1);
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
        var samplePlans = TunnelCorridorFilmstripPolicy.PlanVisibleTracks(
            window.Count,
            baseTick,
            _ctl.MaxTick,
            coarseUnitTicks);
        for (var trackIndex = 0; trackIndex < window.Count; trackIndex++)
        {
            BuildCorridorSlot(window[trackIndex], trackIndex, baseTick, coarseUnitTicks, samplePlans, gen);
        }

        BuildAxialDepthCues();

        UpdateInnerBinding(gen);
        BuildFineRailIfFocused(window);
        UpdateInnerControlVisuals();

        // Resolve the world service only after every real corridor/sample has mounted its explicit
        // unavailable state. The integer revision is the only bundle product retained by waiters.
        var resolvedRevision = ResolveGraphRevision();
        if (resolvedRevision is not > 0)
            return;
        RequestOrdinaryFrameTextures(resolvedRevision.Value);
    }

    private void BuildCorridorSlot(
        TunnelCorridorLayout.TunnelTrackSlot slot,
        int trackIndex,
        long baseTick,
        long coarseUnitTicks,
        IReadOnlyList<TunnelCorridorFramePlan> samplePlans,
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

        var depthBands = TunnelCorridorDepthPolicy.Plan(
            TunnelCameraFraming.CurrentPlaneZ,
            TunnelCameraFraming.ThroatZ);
        for (var depthBand = 0; depthBand < depthBands.Count; depthBand++)
        {
            var band = depthBands[depthBand];
            var wallMesh = BuildCylinderSectorMesh(
                start,
                span,
                CorridorSurfaceRadius,
                band.NearZ,
                band.FarZ);
            if (wallMesh is null)
                continue;

            var wall = new MeshInstance3D
            {
                Name = $"Corridor_{SafeNodeName(slot.Descriptor.SphereId)}_{SafeNodeName(slot.Descriptor.LayerId)}_Depth{depthBand}",
                Mesh = wallMesh,
                MaterialOverride = BuildCorridorDepthMaterial(color, band.DepthFraction),
            };
            _corridorsRoot!.AddChild(wall);
            _corridorNodes.Add(new CorridorWallBinding(
                wall,
                slot.Descriptor,
                slot.IsFocused,
                band.DepthFraction));
        }

        BuildCorridorHeader(slot.Descriptor, centerAngle, isActive, slot.IsFocused);

        BuildCurrentPlaneCue(slot.Descriptor, centerAngle, isActive, slot.IsFocused);

        if (slot.Descriptor.Content.Type == "filmstrip")
            BuildFilmstripFrames(
                slot.Descriptor,
                trackIndex,
                centerAngle,
                baseTick,
                coarseUnitTicks,
                samplePlans,
                gen);
    }

    // Per-corridor header mirroring the normal 2D track header (name + state), plus the canonical
    // display rung on a sub-line. Active/inactive is color-coded like the 2D header's style-swap;
    // the focused corridor's title is tinted. Two billboarded Label3Ds stacked vertically.
    private void BuildCorridorHeader(
        LayerTrackDescriptor descriptor, double centerAngleDeg, bool isActive, bool isFocused)
    {
        if (_corridorsRoot is null)
            return;

        var header = TunnelCorridorHeader.Build(descriptor, isActive);
        var style = TunnelCorridorActivityStylePolicy.Resolve(isActive, isFocused);
        var rad = Mathf.DegToRad((float)centerAngleDeg);
        var headZ = (float)TunnelCorridorFilmstripPolicy.TrackIdentityLabelZ(
            TunnelCameraFraming.CurrentPlaneZ);
        var basePos = new Vector3(
            Mathf.Cos(rad) * (CorridorSurfaceRadius - 0.3f),
            Mathf.Sin(rad) * (CorridorSurfaceRadius - 0.3f),
            headZ);

        // Headers are intentionally depth-tested off and bright/large so they identify each
        // corridor even when the foreground rings or planet body would otherwise occlude them.
        var title = new Label3D
        {
            Name = "CorridorHeaderTitle",
            Text = header.Title,
            Position = basePos + new Vector3(0f, 0.24f, 0f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 28,
            Modulate = Brighten(style.TitleColor, 0.18f),
            OutlineModulate = new Color(0f, 0f, 0f, 0.82f),
            NoDepthTest = true,
        };

        var subtitle = new Label3D
        {
            Name = "CorridorHeaderSubtitle",
            Text = header.Subtitle,
            Position = basePos - new Vector3(0f, 0.24f, 0f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 16,
            Modulate = Brighten(style.SubtitleColor, 0.12f),
            OutlineModulate = new Color(0f, 0f, 0f, 0.72f),
            NoDepthTest = true,
        };

        _corridorsRoot!.AddChild(title);
        _corridorsRoot!.AddChild(subtitle);
        _corridorHeaders.Add(new CorridorHeaderBinding(title, subtitle, descriptor, isFocused));
    }

    private void BuildFilmstripFrames(
        LayerTrackDescriptor descriptor,
        int trackIndex,
        double centerAngleDeg,
        long baseTick,
        long coarseUnitTicks,
        IReadOnlyList<TunnelCorridorFramePlan> samplePlans,
        int gen)
    {
        if (_corridorsRoot is null || _ctl is null || coarseUnitTicks <= 0)
            return;

        var rad = Mathf.DegToRad((float)centerAngleDeg);

        foreach (var framePlan in samplePlans)
        {
            if (framePlan.TrackIndex != trackIndex)
                continue;
            var fs = framePlan.Slot;
            if (!TunnelCameraFraming.TryTickToZ(fs.Tick, baseTick, coarseUnitTicks, out var z))
                continue;
            var frameCenter = new Vector3(
                Mathf.Cos(rad) * (CorridorSurfaceRadius - 0.55f),
                Mathf.Sin(rad) * (CorridorSurfaceRadius - 0.55f),
                z);

            var frameRoot = new Node3D
            {
                Name = $"Frame_{SafeNodeName(descriptor.SphereId)}_{SafeNodeName(descriptor.LayerId)}_{fs.Index}_{fs.Tick}",
                Position = frameCenter,
            };
            var material = new SnapshotSphereMaterial();
            var sphere = new MeshInstance3D
            {
                Name = "SnapshotSphere",
                Mesh = new SphereMesh
                {
                    Radius = 0.35f,
                    Height = 0.70f,
                    RadialSegments = 32,
                    Rings = 16,
                },
                MaterialOverride = material.Material,
            };
            frameRoot.AddChild(sphere);

            var unavailable = new Node3D { Name = "PreviewUnavailable" };
            unavailable.AddChild(new Label3D
            {
                Text = "preview unavailable",
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                FontSize = 12,
                Modulate = new Color(0.74f, 0.76f, 0.79f, 0.90f),
                OutlineModulate = new Color(0f, 0f, 0f, 0.70f),
            });
            frameRoot.AddChild(unavailable);

            _corridorsRoot!.AddChild(frameRoot);

            var sink = new SnapshotSphereFilmstripSink(sphere, material, unavailable);
            _frameBindings.Add(new TunnelFrameBinding(
                fs.Tick,
                descriptor,
                frameRoot,
                material,
                sphere,
                unavailable,
                sink,
                gen));
        }
    }

    private int? ResolveGraphRevision()
    {
        try
        {
            var world = _registry.TryGet<FantaSim.App.World.IService>();
            return world?.GetGenerationProductsAsync().GraphRevision;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Tunnel snapshot graph revision unavailable; previews remain unavailable.");
            return null;
        }
    }

    private void RequestOrdinaryFrameTextures(int graphRevision)
    {
        if (graphRevision <= 0)
            return;

        foreach (var binding in _frameBindings)
        {
            if (binding.MountGeneration != _generation || !binding.Sink.IsAlive)
                continue;

            var rung = TunnelCorridorLayout.ResolveCorridorRung(
                binding.Descriptor.TimeDomain.Rung,
                TunnelScrubMapper.ResolveOuterRung());
            _filmstrip.RequestTexture(
                binding.Sink,
                binding.Descriptor.SphereId,
                binding.Descriptor.LayerId,
                binding.RequestedTick,
                rung.Symbol,
                graphRevision);
        }
    }

    private void ReleaseFrameBindings(bool supersedeWaiters)
    {
        if (supersedeWaiters)
            _filmstrip.Supersede();
        if (_frameBindings.Count == 0)
            return;

        foreach (var binding in _frameBindings)
        {
            // Snapshot materials are intentionally per-sphere; detach the shared immutable texture
            // before releasing the material so the controller cache remains its sole owner.
            binding.Material.SetTexture(null);
            if (GodotObject.IsInstanceValid(binding.Sphere))
            {
                binding.Sphere.MaterialOverride = null;
                binding.Sphere.Mesh = null;
            }
            binding.Material.Dispose();

            if (!GodotObject.IsInstanceValid(binding.Root))
                continue;
            binding.Root.GetParent()?.RemoveChild(binding.Root);
            binding.Root.QueueFree();
        }

        _frameBindings.Clear();
    }

    private void BuildCurrentPlaneCue(
        LayerTrackDescriptor descriptor,
        double centerAngleDegrees,
        bool isActive,
        bool isFocused)
    {
        if (_corridorsRoot is null)
            return;

        var style = TunnelCorridorActivityStylePolicy.Resolve(isActive, isFocused);
        var rad = Mathf.DegToRad((float)centerAngleDegrees);
        var radialDistance = CorridorSurfaceRadius - 0.58f;
        var root = new Node3D
        {
            Name = $"CurrentPlaneCue_{SafeNodeName(descriptor.SphereId)}_{SafeNodeName(descriptor.LayerId)}",
            Position = new Vector3(
                Mathf.Cos(rad) * radialDistance,
                Mathf.Sin(rad) * radialDistance,
                TunnelCameraFraming.CurrentPlaneZ),
            RotationDegrees = new Vector3(0f, 0f, (float)centerAngleDegrees - 90f),
        };
        _corridorsRoot.AddChild(root);

        // BoxMesh is a render-only PrimitiveMesh; these nodes deliberately have no physics body,
        // collision shape, annular geometry, or input relay registration.
        // Source: https://docs.godotengine.org/en/4.7/classes/class_boxmesh.html
        var slice = new MeshInstance3D
        {
            Name = "CurrentPlaneSlice",
            Mesh = new BoxMesh { Size = new Vector3(1.10f, 0.72f, 0.035f) },
            MaterialOverride = BuildCueMaterial(style.CueColor, 0.075f),
        };
        root.AddChild(slice);
        _corridorCues.Add(new CorridorCueBinding(root, slice, descriptor, isFocused, isActive));

        var chevronMaterial = BuildCueMaterial(CurrentPlaneCueColor, 0.82f);
        root.AddChild(new MeshInstance3D
        {
            Name = "CurrentPlaneChevronLeft",
            Position = new Vector3(-0.12f, -0.12f, 0.045f),
            RotationDegrees = new Vector3(0f, 0f, 34f),
            Mesh = new BoxMesh { Size = new Vector3(0.40f, 0.075f, 0.045f) },
            MaterialOverride = chevronMaterial,
        });
        root.AddChild(new MeshInstance3D
        {
            Name = "CurrentPlaneChevronRight",
            Position = new Vector3(0.12f, -0.12f, 0.045f),
            RotationDegrees = new Vector3(0f, 0f, -34f),
            Mesh = new BoxMesh { Size = new Vector3(0.40f, 0.075f, 0.045f) },
            MaterialOverride = chevronMaterial,
        });
    }

    private void BuildAxialDepthCues()
    {
        if (_corridorsRoot is null)
            return;

        BuildNearInteriorLipChevrons();

        var cuePlans = TunnelCorridorFilmstripPolicy.PlanAxialCues(
            TunnelCameraFraming.CurrentPlaneZ,
            TunnelCameraFraming.ThroatZ);
        var angleDegrees = TunnelCorridorLayout.LeftFocusAngleDegrees;
        var angleRad = Mathf.DegToRad((float)angleDegrees);
        var radialDistance = CorridorSurfaceRadius - 0.88f;

        foreach (var cuePlan in cuePlans)
        {
            var cueRoot = new Node3D
            {
                Name = $"AxialDepthCue_{cuePlan.Index}",
                Position = new Vector3(
                    Mathf.Cos(angleRad) * radialDistance,
                    Mathf.Sin(angleRad) * radialDistance,
                    (float)cuePlan.Z),
                RotationDegrees = new Vector3(0f, 0f, (float)angleDegrees - 90f),
            };
            _corridorsRoot.AddChild(cueRoot);

            cueRoot.AddChild(new MeshInstance3D
            {
                Name = "DepthValueMarker",
                Mesh = new BoxMesh { Size = new Vector3(0.10f, 0.42f, 0.10f) },
                MaterialOverride = BuildCueMaterial(AxialCueColor, 0.72f),
            });
            cueRoot.AddChild(new Label3D
            {
                Name = "DepthValueLabel",
                Text = FormattableString.Invariant($"+{cuePlan.FractionFromCurrent:0.00} kb"),
                Position = new Vector3(0.42f, -0.08f, 0f),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                FontSize = 15,
                Modulate = new Color(0.64f, 0.78f, 0.88f, 0.72f),
                OutlineModulate = new Color(0f, 0f, 0f, 0.72f),
                NoDepthTest = false,
            });

            // OmniLight3D is position-dependent, so these two separated nodes add real axial
            // illumination changes to shaded snapshot spheres and wall bands.
            // Source: https://docs.godotengine.org/en/4.7/classes/class_omnilight3d.html
            cueRoot.AddChild(new OmniLight3D
            {
                Name = "DepthLight",
                Position = new Vector3(0f, -0.75f, 0f),
                LightColor = AxialCueColor,
                LightEnergy = 0.72f,
                OmniRange = 4.8f,
                ShadowEnabled = false,
            });
        }
    }

    private void BuildNearInteriorLipChevrons()
    {
        if (_corridorsRoot is null)
            return;

        for (var cueIndex = 0; cueIndex < TunnelCameraFraming.NearInteriorLipCueCount; cueIndex++)
        {
            var cuePoint = TunnelCameraFraming.NearInteriorLipCuePoint(cueIndex);
            var angleDegrees = (float)(Math.Atan2(cuePoint.Y, cuePoint.X) * 180.0 / Math.PI);
            var root = new Node3D
            {
                Name = $"InteriorLipChevron_{cueIndex}",
                Position = new Vector3(cuePoint.X, cuePoint.Y, cuePoint.Z),
                RotationDegrees = new Vector3(0f, 0f, angleDegrees - 90f),
            };
            _corridorsRoot.AddChild(root);

            var material = BuildCueMaterial(CurrentPlaneCueColor, 0.52f);
            root.AddChild(new MeshInstance3D
            {
                Name = "LipChevronLeft",
                Position = new Vector3(-0.10f, -0.10f, 0f),
                RotationDegrees = new Vector3(0f, 0f, 34f),
                Mesh = new BoxMesh { Size = new Vector3(0.34f, 0.065f, 0.05f) },
                MaterialOverride = material,
            });
            root.AddChild(new MeshInstance3D
            {
                Name = "LipChevronRight",
                Position = new Vector3(0.10f, -0.10f, 0f),
                RotationDegrees = new Vector3(0f, 0f, -34f),
                Mesh = new BoxMesh { Size = new Vector3(0.34f, 0.065f, 0.05f) },
                MaterialOverride = material,
            });
        }
    }

    private static StandardMaterial3D BuildCorridorDepthMaterial(Color color, float depthFraction)
    {
        var material = new StandardMaterial3D();
        ApplyCorridorDepthMaterial(material, color, depthFraction);
        return material;
    }

    private static void ApplyCorridorDepthMaterial(
        StandardMaterial3D material,
        Color color,
        float depthFraction)
    {
        var tone = TunnelCorridorDepthPolicy.ToneAt(depthFraction);
        var tinted = new Color(
            color.R * tone.Brightness,
            color.G * tone.Brightness,
            color.B * tone.Brightness,
            1f);
        material.AlbedoColor = tinted;
        material.Transparency = BaseMaterial3D.TransparencyEnum.Disabled;
        material.ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel;
        material.Roughness = 0.86f;
        material.Metallic = 0.02f;
        material.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        material.EmissionEnabled = tone.EmissionEnabled;
        material.Emission = Colors.Black;
        material.EmissionEnergyMultiplier = 0f;
        material.NoDepthTest = false;
        // StandardMaterial3D exposes per-pixel lighting/roughness without a custom shader. Walls
        // deliberately do not self-illuminate: light falloff and the continuous shell must carry
        // the curvature/depth read while each wall band keeps its own value.
        // Source: https://docs.godotengine.org/en/4.7/classes/class_standardmaterial3d.html
    }

    private static StandardMaterial3D BuildCueMaterial(Color color, float alpha)
    {
        var tinted = new Color(color.R, color.G, color.B, alpha);
        return new StandardMaterial3D
        {
            AlbedoColor = tinted,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            EmissionEnabled = true,
            Emission = new Color(color.R, color.G, color.B, 1f),
            EmissionEnergyMultiplier = 0.55f,
            NoDepthTest = false,
        };
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

    private void RepositionExistingFrames(long baseTick, long coarseSpanTicks, long requestEndTick)
    {
        if (_corridorsRoot is null || coarseSpanTicks <= 0)
            return;

        foreach (var binding in _frameBindings)
        {
            if (binding.MountGeneration != _generation)
                continue;
            var node = binding.Root;
            var tick = binding.RequestedTick;
            if (!GodotObject.IsInstanceValid(node))
                continue;

            if (tick > requestEndTick
                || !TunnelCameraFraming.TryTickToZ(tick, baseTick, coarseSpanTicks, out var z))
            {
                node.Visible = false;
                continue;
            }

            node.Visible = true;
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

        ResetFinePreview(TunnelFineResetReason.BaseTimeChanged);
        UpdateInnerBinding(_generation);
        UpdateCorridorActivityStyles(tick);
        UpdateInnerControlVisuals();

        if (_outerLabel is not null && GodotObject.IsInstanceValid(_outerLabel))
            _outerLabel.Text = BuildOuterLabelText();

        if (rebuildFrameRequests)
        {
            _pendingCorridorRebuild = false;
            RebuildCorridors();
        }
        else
        {
            var coarse = TunnelScrubMapper.MapOuterAngleToTick(360d, tick, _ctl.MaxTick);
            var coarseSpanTicks = TimelineModel.SpanTicksForRung(coarse.Rung, units: 1);
            var requestEnd = Math.Min(coarse.ClampedTargetTick, _ctl.MaxTick);
            RepositionExistingFrames(tick, coarseSpanTicks, requestEnd);

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

        // Corridor color only changes when a track's regime activity flips, which happens at rare
        // schedule boundaries — not on most drag frames. Recompute the small active-flag mask (cheap
        // schedule lookups) and early-out before touching any material when it is unchanged, so a
        // scrub does not rewrite ~10 properties per corridor every frame for an identical result.
        var activityByDescriptor = new Dictionary<(string SphereId, string LayerId), bool>();
        var mask = 0;
        var bit = 1;
        foreach (var binding in _corridorHeaders)
        {
            var active = TunnelTrackActivity.IsActive(
                binding.Descriptor, tick, _ctl.GeosphereSchedule, _ctl.AtmosphereSchedule);
            activityByDescriptor[(binding.Descriptor.SphereId, binding.Descriptor.LayerId)] = active;
            if (active)
                mask |= bit;
            bit <<= 1;
        }

        if (mask == _lastCorridorActivityMask)
            return;
        _lastCorridorActivityMask = mask;

        foreach (var binding in _corridorNodes)
        {
            var node = binding.Node;
            if (!GodotObject.IsInstanceValid(node)
                || !activityByDescriptor.TryGetValue(
                    (binding.Descriptor.SphereId, binding.Descriptor.LayerId),
                    out var active))
                continue;

            var style = TunnelCorridorActivityStylePolicy.Resolve(active, binding.IsFocused);
            if (node.MaterialOverride is StandardMaterial3D material
                && GodotObject.IsInstanceValid(material))
            {
                ApplyCorridorDepthMaterial(material, style.WallColor, binding.DepthFraction);
            }
            else
            {
                node.MaterialOverride = BuildCorridorDepthMaterial(style.WallColor, binding.DepthFraction);
            }
        }

        var headerIndex = 0;
        foreach (var binding in _corridorHeaders)
        {
            var active = activityByDescriptor[(binding.Descriptor.SphereId, binding.Descriptor.LayerId)];
            var style = TunnelCorridorActivityStylePolicy.Resolve(active, binding.IsFocused);
            UpdateCorridorHeaderStyle(headerIndex, active, binding.IsFocused, style);
            headerIndex++;
        }

        var cueIndex = 0;
        foreach (var binding in _corridorCues)
        {
            if (!activityByDescriptor.TryGetValue(
                    (binding.Descriptor.SphereId, binding.Descriptor.LayerId),
                    out var active))
            {
                cueIndex++;
                continue;
            }

            var style = TunnelCorridorActivityStylePolicy.Resolve(active, binding.IsFocused);
            UpdateCorridorCueStyle(cueIndex, style);
            cueIndex++;
        }
    }

    private void UpdateCorridorHeaderStyle(
        int index,
        bool isActive,
        bool isFocused,
        TunnelCorridorActivityStylePolicy.CorridorStyle style)
    {
        if (index < 0 || index >= _corridorHeaders.Count)
            return;

        var binding = _corridorHeaders[index];
        if (!GodotObject.IsInstanceValid(binding.Title) || !GodotObject.IsInstanceValid(binding.Subtitle))
            return;

        var header = TunnelCorridorHeader.Build(binding.Descriptor, isActive);
        binding.Title.Text = header.Title;
        binding.Title.Modulate = style.TitleColor;
        binding.Subtitle.Text = header.Subtitle;
        binding.Subtitle.Modulate = style.SubtitleColor;
    }

    private void UpdateCorridorCueStyle(
        int index,
        TunnelCorridorActivityStylePolicy.CorridorStyle style)
    {
        if (index < 0 || index >= _corridorCues.Count)
            return;

        var binding = _corridorCues[index];
        if (!GodotObject.IsInstanceValid(binding.Slice))
            return;

        if (binding.Slice.MaterialOverride is StandardMaterial3D material
            && GodotObject.IsInstanceValid(material))
        {
            ApplyCueMaterial(material, style.CueColor, 0.075f);
        }
        else
        {
            binding.Slice.MaterialOverride = BuildCueMaterial(style.CueColor, 0.075f);
        }
    }

    private static void ApplyCueMaterial(StandardMaterial3D material, Color color, float alpha)
    {
        var tinted = new Color(color.R, color.G, color.B, alpha);
        material.AlbedoColor = tinted;
        material.Emission = new Color(color.R, color.G, color.B, 1f);
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
        ResetFinePreview(TunnelFineResetReason.FocusChanged);
        _filmstrip.Supersede();
        ResolveSourceTracks();
        RebuildCorridors();
    }
}
