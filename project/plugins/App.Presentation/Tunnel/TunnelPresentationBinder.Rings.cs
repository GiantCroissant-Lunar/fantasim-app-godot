using System;
using System.Globalization;
using FantaSim.App.Timeline;
using FantaSim.App.Timeline.Seam;
using Godot;

namespace FantaSim.App.Presentation.Tunnel;

internal sealed partial class TunnelPresentationBinder
{
    private static readonly Color OuterRingColor = new(0.35f, 0.62f, 0.78f);
    private static readonly Color InnerRingColor = new(0.98f, 0.72f, 0.20f);
    private static readonly Color InnerRingInactiveColor = new(0.50f, 0.50f, 0.55f);
    private const float RingMarkerHalfSize = 0.35f;

    private Node3D? _outerRingRoot;
    private Node3D? _innerRingRoot;
    private Label3D? _outerLabel;
    private Label3D? _innerLabel;
    private MeshInstance3D? _innerRingMesh;
    private MeshInstance3D? _innerRingMarker;

    private void EnsureRingRoots()
    {
        if (_mount is null)
            return;

        if (_outerRingRoot is null || !GodotObject.IsInstanceValid(_outerRingRoot))
        {
            _outerRingRoot = new Node3D { Name = "OuterCoarseRing" };
            _mount.AddChild(_outerRingRoot);
        }

        if (_innerRingRoot is null || !GodotObject.IsInstanceValid(_innerRingRoot))
        {
            _innerRingRoot = new Node3D { Name = "InnerFocusRing" };
            _mount.AddChild(_innerRingRoot);
        }
    }

    private void ClearRingRoots()
    {
        _outerRingRoot = null;
        _innerRingRoot = null;
        _outerLabel = null;
        _innerLabel = null;
        _innerRingMesh = null;
        _innerRingMarker = null;
    }

    private void RebuildTwoRingControls()
    {
        if (_disposed || _mount is null || !GodotObject.IsInstanceValid(_mount))
            return;

        EnsureRingRoots();
        ClearChildren(_outerRingRoot!);
        ClearChildren(_innerRingRoot!);

        var outerMesh = BuildPlanarAnnulusSectorMesh(
            0.0, 360.0, OuterRingInnerRadius, OuterRingOuterRadius, MouthZ);
        if (outerMesh is not null)
        {
            var outer = new MeshInstance3D
            {
                Name = "OuterRing",
                Mesh = outerMesh,
                MaterialOverride = BuildUnlitMaterial(OuterRingColor),
            };
            _outerRingRoot!.AddChild(outer);

            var marker = BuildAsymmetricMarker(OuterRingColor, OuterRingOuterRadius);
            _outerRingRoot!.AddChild(marker);
        }

        var innerColor = _fineBinding.CanAdjust ? InnerRingColor : InnerRingInactiveColor;
        var innerMesh = BuildPlanarAnnulusSectorMesh(
            0.0, 360.0, InnerRingInnerRadius, InnerRingOuterRadius, MouthZ);
        if (innerMesh is not null)
        {
            _innerRingMesh = new MeshInstance3D
            {
                Name = "InnerRing",
                Mesh = innerMesh,
                MaterialOverride = BuildUnlitMaterial(innerColor),
            };
            _innerRingRoot!.AddChild(_innerRingMesh);

            _innerRingMarker = BuildAsymmetricMarker(innerColor, InnerRingOuterRadius);
            _innerRingRoot!.AddChild(_innerRingMarker);
        }
    }

    private void UpdateRingLabels()
    {
        if (_disposed || _outerRingRoot is null || _innerRingRoot is null)
            return;

        _outerLabel?.QueueFree();
        _innerLabel?.QueueFree();

        var outerText = BuildOuterLabelText();
        _outerLabel = new Label3D
        {
            Name = "OuterLabel",
            Text = outerText,
            Position = new Vector3(0f, OuterRingOuterRadius + 0.8f, 0.1f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 28,
            Modulate = new Color(0.85f, 0.90f, 0.95f, 0.92f),
            OutlineModulate = new Color(0f, 0f, 0f, 0.65f),
            NoDepthTest = false,
        };
        _outerRingRoot.AddChild(_outerLabel);

        var innerText = BuildInnerLabelText();
        _innerLabel = new Label3D
        {
            Name = "InnerLabel",
            Text = innerText,
            Position = new Vector3(0f, -(InnerRingOuterRadius + 0.8f), 0.1f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 24,
            Modulate = new Color(0.92f, 0.94f, 0.97f, 0.92f),
            OutlineModulate = new Color(0f, 0f, 0f, 0.65f),
            NoDepthTest = false,
        };
        _innerRingRoot.AddChild(_innerLabel);
    }

    private string BuildOuterLabelText()
    {
        if (_ctl is null)
            return "tick: —";

        var kbRung = TunnelScrubMapper.ResolveOuterRung();
        return $"tick {_ctl.Tick} | {kbRung.Symbol}";
    }

    private string BuildInnerLabelText()
    {
        if (_fineBinding.Descriptor is null)
            return "No track";

        var state = _fineBinding.IsActive ? "active" : "inactive";
        var rung = _fineBinding.Rung?.Symbol ?? "?";
        var delta = FormatFineDelta(_finePreview, rung);
        return $"{_fineBinding.OwnerLabel} | {rung} | {state} | {delta}";
    }

    private void UpdateOuterRingVisual(TunnelOuterTickMapping mapping)
    {
        if (_outerRingRoot is not null && GodotObject.IsInstanceValid(_outerRingRoot))
            _outerRingRoot.RotationDegrees = new Vector3(0f, 0f, -(float)(mapping.AccumulatedDegrees % 360.0));

        if (_outerLabel is not null && GodotObject.IsInstanceValid(_outerLabel))
            _outerLabel.Text = $"tick {mapping.ClampedTargetTick} | {mapping.Rung.Symbol}";
    }

    private void UpdateInnerRingVisual(TunnelFineTrackBinding binding, TunnelFinePreview preview)
    {
        if (_innerRingRoot is not null && GodotObject.IsInstanceValid(_innerRingRoot))
            _innerRingRoot.RotationDegrees = new Vector3(0f, 0f, -(float)preview.AccumulatedDegrees);

        if (_innerLabel is not null && GodotObject.IsInstanceValid(_innerLabel))
        {
            var state = binding.IsActive ? "active" : "inactive";
            var rung = binding.Rung?.Symbol ?? "?";
            var delta = FormatFineDelta(preview, rung);
            _innerLabel.Text = $"{binding.OwnerLabel} | {rung} | {state} | {delta}";
        }

        if (_fineCursor is not null && GodotObject.IsInstanceValid(_fineCursor))
            _fineCursor.Position = new Vector3(0f, 0f, (float)preview.CursorZ);
    }

    private void UpdateInnerControlVisuals()
    {
        var color = _fineBinding.CanAdjust ? InnerRingColor : InnerRingInactiveColor;
        if (_innerRingMesh is not null && GodotObject.IsInstanceValid(_innerRingMesh))
            _innerRingMesh.MaterialOverride = BuildUnlitMaterial(color);
        if (_innerRingMarker is not null && GodotObject.IsInstanceValid(_innerRingMarker))
            _innerRingMarker.MaterialOverride = BuildUnlitMaterial(Brighten(color));

        UpdateInnerRingVisual(_fineBinding, _finePreview);
    }

    private static string FormatFineDelta(TunnelFinePreview preview, string rung)
    {
        if (preview.IntegralTickDelta is { } integral)
            return $"{integral:+0;-0;0} ticks";

        return $"{FormatSigned(preview.RawTickQuantity)} ticks "
            + $"({FormatSigned(preview.RungUnits)} {rung}) fractional";
    }

    private static string FormatSigned(double value)
    {
        var magnitude = Math.Abs(value).ToString("G10", CultureInfo.InvariantCulture);
        return value > 0d ? "+" + magnitude : value < 0d ? "-" + magnitude : "0";
    }

    private static Color Brighten(Color color)
        => new(
            Math.Min(1f, color.R + 0.3f),
            Math.Min(1f, color.G + 0.3f),
            Math.Min(1f, color.B + 0.3f));

    private static MeshInstance3D BuildAsymmetricMarker(Color color, float outerRadius)
    {
        var markerMesh = BuildPlanarAnnulusSectorMesh(
            -8.0, 16.0, outerRadius - 0.38f, outerRadius - 0.08f, MouthZ + 0.02f);
        return new MeshInstance3D
        {
            Name = "RingMarker",
            Mesh = markerMesh,
            MaterialOverride = BuildUnlitMaterial(Brighten(color)),
        };
    }

    private void ClearChildren(Node3D root)
    {
        foreach (var child in root.GetChildren())
        {
            root.RemoveChild(child);
            child.QueueFree();
        }
    }
}
