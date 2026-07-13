using System;
using System.Collections.Generic;
using System.Globalization;
using FantaSim.App.Timeline;
using FantaSim.App.Timeline.Seam;
using Godot;
using NumericsVector3 = System.Numerics.Vector3;

namespace FantaSim.App.Presentation.Tunnel;

internal readonly record struct TunnelInstrumentNodePlan(string Name, string ParentName);

internal readonly record struct TunnelInspectionLensSettings(
    string Label,
    NumericsVector3 LocalPosition,
    float Radius);

internal static class TunnelInspectionLensContract
{
    internal static TunnelInspectionLensSettings Settings => new(
        Label: "inspection",
        LocalPosition: new NumericsVector3(1.95f, 0f, 0.03f),
        Radius: 0.48f);
}

/// <summary>
/// Godot-free, testable contract for the camera-local cockpit instrument. Production consumes these
/// names, transforms, radii, phase, and status strings directly; the node plan documents the exact
/// non-rotating ownership boundary required by asymmetric-cockpit design section 5.1.
/// </summary>
internal static class TunnelInstrumentContract
{
    internal const string InstrumentRootName = "InstrumentRoot";
    internal const string OuterRotationRootName = "OuterRotationRoot";
    internal const string InnerRotationRootName = "InnerRotationRoot";
    internal const string ReadoutRootName = "ReadoutRoot";
    internal const string OuterReadoutName = "OuterReadout";
    internal const string InnerReadoutName = "InnerReadout";
    internal const string StatusReadoutName = "StatusReadout";
    internal const string InspectionLensRootName = "InspectionLensRoot";
    internal const float GeometryPlaneZ = 0f;

    internal static IReadOnlyList<TunnelInstrumentNodePlan> NodePlan { get; } =
        Array.AsReadOnly(new[]
        {
            new TunnelInstrumentNodePlan(InstrumentRootName, "TunnelMount"),
            new TunnelInstrumentNodePlan(OuterRotationRootName, InstrumentRootName),
            new TunnelInstrumentNodePlan(InnerRotationRootName, InstrumentRootName),
            new TunnelInstrumentNodePlan(ReadoutRootName, InstrumentRootName),
            new TunnelInstrumentNodePlan(OuterReadoutName, ReadoutRootName),
            new TunnelInstrumentNodePlan(InnerReadoutName, ReadoutRootName),
            new TunnelInstrumentNodePlan(StatusReadoutName, ReadoutRootName),
            new TunnelInstrumentNodePlan(InspectionLensRootName, ReadoutRootName),
        });

    internal static NumericsVector3 LocalAnchor => TunnelCameraFraming.InstrumentLocalAnchor;
    internal static float InnerRingInnerRadius => TunnelCameraFraming.InnerRingInnerRadius;
    internal static float InnerRingOuterRadius => TunnelCameraFraming.InnerRingOuterRadius;
    internal static float OuterRingInnerRadius => TunnelCameraFraming.OuterRingInnerRadius;
    internal static float OuterRingOuterRadius => TunnelCameraFraming.OuterRingOuterRadius;

    // Readout stack Y positions are chosen to fit inside a conservative 16:9/16:10 viewport safe
    // area when the instrument is at InstrumentLocalAnchor. See TunnelCameraFramingSafeBoundsTests.
    internal const float OuterReadoutYOffset = 1.82f;
    internal const float InnerReadoutYOffset = -1.70f;
    internal const float StatusReadoutYOffset = -1.95f;

    internal static float CanonicalOuterRotationDegrees(long tick, long unitTicks)
        => (float)TunnelScrubMapper.CanonicalPhaseDegrees(tick, unitTicks);

    internal static bool IsExpectedParent(string nodeName, string parentName)
    {
        foreach (var node in NodePlan)
        {
            if (string.Equals(node.Name, nodeName, StringComparison.Ordinal))
                return string.Equals(node.ParentName, parentName, StringComparison.Ordinal);
        }

        return false;
    }

    internal static string StatusText(bool hasTrack, bool isActive)
        => !hasTrack ? "No track" : isActive ? "active at current time" : "inactive at current time";
}

internal sealed partial class TunnelPresentationBinder
{
    private static readonly Color OuterRingColor = new(0.35f, 0.62f, 0.78f);
    private static readonly Color InnerRingColor = new(0.98f, 0.72f, 0.20f);
    private static readonly Color InnerRingInactiveColor = new(0.50f, 0.50f, 0.55f);
    private Node3D? _instrumentRoot;
    private Node3D? _outerRingRoot;
    private Node3D? _innerRingRoot;
    private Node3D? _readoutRoot;
    private Node3D? _inspectionLensRoot;
    private Label3D? _outerLabel;
    private Label3D? _innerLabel;
    private Label3D? _statusLabel;
    private MeshInstance3D? _innerRingMesh;
    private MeshInstance3D? _innerRingMarker;
    private MeshInstance3D? _inspectionLensSphere;
    private SnapshotSphereMaterial? _inspectionLensMaterial;
    private SnapshotSphereFilmstripSink? _inspectionLensSink;

    private bool EnsureInstrumentHierarchy()
    {
        // Part B: the instrument is parented to the mount and centered on the tunnel axis so the
        // rings encircle the throat as tunnel geometry, rather than riding the camera as a corner
        // dial. Readout labels stay billboarded (face the camera) regardless of parent.
        var mount = _mount;
        if (mount is null || !GodotObject.IsInstanceValid(mount) || !mount.IsInsideTree())
            return false;

        if (_instrumentRoot is null || !GodotObject.IsInstanceValid(_instrumentRoot))
            _instrumentRoot = new Node3D { Name = TunnelInstrumentContract.InstrumentRootName };
        EnsureInstrumentParent(mount, _instrumentRoot);

        _instrumentRoot.Position = ToGodot(TunnelInstrumentContract.LocalAnchor);
        _instrumentRoot.Rotation = Vector3.Zero;
        _instrumentRoot.Scale = Vector3.One;

        if (_outerRingRoot is null || !GodotObject.IsInstanceValid(_outerRingRoot))
            _outerRingRoot = new Node3D { Name = TunnelInstrumentContract.OuterRotationRootName };
        EnsureInstrumentParent(_instrumentRoot, _outerRingRoot);

        if (_innerRingRoot is null || !GodotObject.IsInstanceValid(_innerRingRoot))
            _innerRingRoot = new Node3D { Name = TunnelInstrumentContract.InnerRotationRootName };
        EnsureInstrumentParent(_instrumentRoot, _innerRingRoot);

        if (_readoutRoot is null || !GodotObject.IsInstanceValid(_readoutRoot))
            _readoutRoot = new Node3D { Name = TunnelInstrumentContract.ReadoutRootName };
        EnsureInstrumentParent(_instrumentRoot, _readoutRoot);

        if (_inspectionLensRoot is null || !GodotObject.IsInstanceValid(_inspectionLensRoot))
            _inspectionLensRoot = new Node3D { Name = TunnelInstrumentContract.InspectionLensRootName };
        EnsureInstrumentParent(_readoutRoot, _inspectionLensRoot);
        _inspectionLensRoot.Position = ToGodot(TunnelInspectionLensContract.Settings.LocalPosition);

        return true;
    }

    private void ClearRingRoots()
    {
        ClearInspectionLens();
        var instrumentRoot = _instrumentRoot;
        _instrumentRoot = null;
        _outerRingRoot = null;
        _innerRingRoot = null;
        _readoutRoot = null;
        _inspectionLensRoot = null;
        _outerLabel = null;
        _innerLabel = null;
        _statusLabel = null;
        _innerRingMesh = null;
        _innerRingMarker = null;
        _inspectionLensSphere = null;
        _inspectionLensMaterial = null;
        _inspectionLensSink = null;

        if (instrumentRoot is null || !GodotObject.IsInstanceValid(instrumentRoot))
            return;

        instrumentRoot.GetParent()?.RemoveChild(instrumentRoot);
        instrumentRoot.QueueFree();
    }

    private void RebuildTwoRingControls()
    {
        if (_disposed || !EnsureInstrumentHierarchy())
            return;

        ClearChildren(_outerRingRoot!);
        ClearChildren(_innerRingRoot!);

        var outerMesh = BuildPlanarAnnulusSectorMesh(
            0.0,
            360.0,
            TunnelInstrumentContract.OuterRingInnerRadius,
            TunnelInstrumentContract.OuterRingOuterRadius,
            TunnelInstrumentContract.GeometryPlaneZ);
        if (outerMesh is not null)
        {
            var outer = new MeshInstance3D
            {
                Name = "OuterRing",
                Mesh = outerMesh,
                MaterialOverride = BuildUnlitMaterial(OuterRingColor),
            };
            _outerRingRoot!.AddChild(outer);

            var marker = BuildAsymmetricMarker(
                OuterRingColor,
                TunnelInstrumentContract.OuterRingOuterRadius);
            _outerRingRoot!.AddChild(marker);
        }

        var innerColor = _fineBinding.CanAdjust ? InnerRingColor : InnerRingInactiveColor;
        var innerMesh = BuildPlanarAnnulusSectorMesh(
            0.0,
            360.0,
            TunnelInstrumentContract.InnerRingInnerRadius,
            TunnelInstrumentContract.InnerRingOuterRadius,
            TunnelInstrumentContract.GeometryPlaneZ);
        if (innerMesh is not null)
        {
            _innerRingMesh = new MeshInstance3D
            {
                Name = "InnerRing",
                Mesh = innerMesh,
                MaterialOverride = BuildUnlitMaterial(innerColor),
            };
            _innerRingRoot!.AddChild(_innerRingMesh);

            _innerRingMarker = BuildAsymmetricMarker(
                innerColor,
                TunnelInstrumentContract.InnerRingOuterRadius);
            _innerRingRoot!.AddChild(_innerRingMarker);
        }

        UpdateOuterRingVisualForTick(_ctl?.Tick ?? 0L);
    }

    private SnapshotSphereFilmstripSink? EnsureInspectionLens()
    {
        if (!EnsureInstrumentHierarchy()
            || _inspectionLensRoot is null
            || !GodotObject.IsInstanceValid(_inspectionLensRoot))
            return null;

        if (_inspectionLensSink is not null
            && _inspectionLensSphere is not null
            && GodotObject.IsInstanceValid(_inspectionLensSphere)
            && _inspectionLensMaterial is not null
            && _inspectionLensMaterial.IsAlive)
        {
            _inspectionLensRoot.Visible = true;
            return _inspectionLensSink;
        }

        ClearInspectionLens();
        var settings = TunnelInspectionLensContract.Settings;
        _inspectionLensRoot.Visible = true;

        _inspectionLensMaterial = new SnapshotSphereMaterial();
        _inspectionLensSphere = new MeshInstance3D
        {
            Name = "InspectionSphere",
            Mesh = new SphereMesh
            {
                Radius = settings.Radius,
                Height = settings.Radius * 2f,
                RadialSegments = 40,
                Rings = 24,
            },
            MaterialOverride = _inspectionLensMaterial.Material,
        };
        _inspectionLensRoot.AddChild(_inspectionLensSphere);

        var unavailable = new Node3D { Name = "InspectionUnavailable" };
        unavailable.AddChild(new Label3D
        {
            Text = "preview unavailable",
            Position = new Vector3(0f, 0f, 0.04f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 12,
            Modulate = new Color(0.74f, 0.76f, 0.79f, 0.94f),
            OutlineModulate = new Color(0f, 0f, 0f, 0.72f),
            NoDepthTest = true,
        });
        _inspectionLensRoot.AddChild(unavailable);

        _inspectionLensRoot.AddChild(new Label3D
        {
            Name = "InspectionLabel",
            Text = settings.Label,
            Position = new Vector3(0f, -(settings.Radius + 0.22f), 0.02f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 16,
            Modulate = new Color(0.82f, 0.90f, 0.96f, 0.94f),
            OutlineModulate = new Color(0f, 0f, 0f, 0.72f),
            NoDepthTest = true,
        });
        _inspectionLensRoot.AddChild(new MeshInstance3D
        {
            Name = "InspectionTether",
            Position = new Vector3(-(settings.Radius + 0.24f), 0f, -0.01f),
            Mesh = new BoxMesh { Size = new Vector3(0.44f, 0.025f, 0.025f) },
            MaterialOverride = BuildUnlitMaterial(new Color(0.35f, 0.62f, 0.78f), 0.58f),
        });

        _inspectionLensSink = new SnapshotSphereFilmstripSink(
            _inspectionLensSphere,
            _inspectionLensMaterial,
            unavailable);
        return _inspectionLensSink;
    }

    private void ClearInspectionLens()
    {
        _inspectionLensSink = null;
        if (_inspectionLensSphere is not null
            && GodotObject.IsInstanceValid(_inspectionLensSphere))
        {
            _inspectionLensSphere.MaterialOverride = null;
            _inspectionLensSphere.Mesh = null;
        }
        _inspectionLensSphere = null;

        _inspectionLensMaterial?.Dispose();
        _inspectionLensMaterial = null;

        if (_inspectionLensRoot is null || !GodotObject.IsInstanceValid(_inspectionLensRoot))
            return;
        ClearChildren(_inspectionLensRoot);
        _inspectionLensRoot.Visible = false;
    }

    private void UpdateRingLabels()
    {
        if (_disposed || !EnsureInstrumentHierarchy() || _readoutRoot is null)
            return;

        // Idempotent: create each readout Label3D once, then update text in place on later calls
        // (matching the in-place .Text writes in UpdateOuterRingVisual*). Reallocating three nodes
        // per call was a node-churn trap for any future per-refresh caller.
        // Readout stack is drawn with NoDepthTest so it sits above the rings/planet. The Y
        // offsets are chosen so the outer/inner/status labels stay inside the viewport safe area
        // at both 16:9 and 16:10 without clipping the top/bottom edges.
        _outerLabel = EnsureReadoutLabel(
            _outerLabel,
            TunnelInstrumentContract.OuterReadoutName,
            new Vector3(
                0f,
                TunnelInstrumentContract.OuterReadoutYOffset,
                TunnelInstrumentContract.GeometryPlaneZ + 0.02f),
            fontSize: 22,
            new Color(0.85f, 0.90f, 0.95f, 0.96f));
        _outerLabel.Text = BuildOuterLabelText();

        _innerLabel = EnsureReadoutLabel(
            _innerLabel,
            TunnelInstrumentContract.InnerReadoutName,
            new Vector3(
                0f,
                TunnelInstrumentContract.InnerReadoutYOffset,
                TunnelInstrumentContract.GeometryPlaneZ + 0.02f),
            fontSize: 20,
            new Color(0.92f, 0.94f, 0.97f, 0.96f));
        _innerLabel.Text = BuildInnerLabelText();

        _statusLabel = EnsureReadoutLabel(
            _statusLabel,
            TunnelInstrumentContract.StatusReadoutName,
            new Vector3(
                0f,
                TunnelInstrumentContract.StatusReadoutYOffset,
                TunnelInstrumentContract.GeometryPlaneZ + 0.02f),
            fontSize: 18,
            new Color(0.68f, 0.68f, 0.72f, 0.96f));
        _statusLabel.Text = BuildStatusLabelText();
        _statusLabel.Modulate = _fineBinding.CanAdjust
            ? new Color(0.82f, 0.92f, 0.82f, 0.94f)
            : new Color(0.68f, 0.68f, 0.72f, 0.94f);

        UpdateOuterRingVisualForTick(_ctl?.Tick ?? 0L);
    }

    private Label3D EnsureReadoutLabel(Label3D? existing, string name, Vector3 position, int fontSize, Color modulate)
    {
        if (existing is not null && GodotObject.IsInstanceValid(existing) && existing.IsInsideTree())
            return existing;

        RemoveAndQueueFree(existing);
        var label = new Label3D
        {
            Name = name,
            Position = position,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = fontSize,
            Modulate = modulate,
            OutlineModulate = new Color(0f, 0f, 0f, 0.65f),
            NoDepthTest = true,
        };
        EnsureInstrumentParent(_readoutRoot!, label);
        return label;
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

        var rung = _fineBinding.Rung?.Symbol ?? "?";
        var delta = FormatFineDelta(_finePreview, rung);
        return $"{_fineBinding.OwnerLabel} | {rung} | {delta}";
    }

    private string BuildStatusLabelText()
        => TunnelInstrumentContract.StatusText(
            hasTrack: _fineBinding.Descriptor is not null,
            isActive: _fineBinding.IsActive);

    private void UpdateOuterRingVisual(TunnelOuterTickMapping mapping)
    {
        UpdateOuterRingVisualForTick(mapping.ClampedTargetTick);

        if (_outerLabel is not null && GodotObject.IsInstanceValid(_outerLabel))
            _outerLabel.Text = $"tick {mapping.ClampedTargetTick} | {mapping.Rung.Symbol}";
    }

    private void UpdateOuterRingVisualForTick(long tick)
    {
        var kb = TunnelScrubMapper.ResolveOuterRung();
        var unitTicks = TimelineModel.SpanTicksForRung(kb, units: 1);
        var phase = TunnelInstrumentContract.CanonicalOuterRotationDegrees(
            tick,
            unitTicks);
        if (_outerRingRoot is not null && GodotObject.IsInstanceValid(_outerRingRoot))
            _outerRingRoot.RotationDegrees = new Vector3(0f, 0f, phase);

        if (_outerLabel is not null && GodotObject.IsInstanceValid(_outerLabel))
            _outerLabel.Text = $"tick {tick} | {kb.Symbol}";
    }

    private void UpdateInnerRingVisual(TunnelFineTrackBinding binding, TunnelFinePreview preview)
    {
        if (_innerRingRoot is not null && GodotObject.IsInstanceValid(_innerRingRoot))
            _innerRingRoot.RotationDegrees = new Vector3(0f, 0f, -(float)preview.AccumulatedDegrees);

        if (_innerLabel is not null && GodotObject.IsInstanceValid(_innerLabel))
        {
            var rung = binding.Rung?.Symbol ?? "?";
            var delta = FormatFineDelta(preview, rung);
            _innerLabel.Text = binding.Descriptor is null
                ? "No track"
                : $"{binding.OwnerLabel} | {rung} | {delta}";
        }

        if (_statusLabel is not null && GodotObject.IsInstanceValid(_statusLabel))
        {
            _statusLabel.Text = TunnelInstrumentContract.StatusText(
                hasTrack: binding.Descriptor is not null,
                isActive: binding.IsActive);
            _statusLabel.Modulate = binding.CanAdjust
                ? new Color(0.82f, 0.92f, 0.82f, 0.94f)
                : new Color(0.68f, 0.68f, 0.72f, 0.94f);
        }

        if (_fineCursor is not null && GodotObject.IsInstanceValid(_fineCursor))
            _fineCursor.Position = new Vector3(0f, 0f, (float)preview.CursorZ);
    }

    private void UpdateInnerControlVisuals()
    {
        UpdateOuterRingVisualForTick(_ctl?.Tick ?? 0L);

        var color = _fineBinding.CanAdjust ? InnerRingColor : InnerRingInactiveColor;
        if (_innerRingMesh is not null && GodotObject.IsInstanceValid(_innerRingMesh))
            ApplyUnlitRingColor(_innerRingMesh, color);
        if (_innerRingMarker is not null && GodotObject.IsInstanceValid(_innerRingMarker))
            ApplyUnlitRingColor(_innerRingMarker, Brighten(color));

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
        => Brighten(color, 0.3f);

    private static Color Brighten(Color color, float amount)
        => new(
            Math.Min(1f, color.R + amount),
            Math.Min(1f, color.G + amount),
            Math.Min(1f, color.B + amount));

    private static void ApplyUnlitRingColor(MeshInstance3D node, Color color)
    {
        if (node.MaterialOverride is StandardMaterial3D material
            && GodotObject.IsInstanceValid(material))
        {
            material.AlbedoColor = color;
            material.Emission = color;
            return;
        }

        node.MaterialOverride = BuildUnlitMaterial(color);
    }

    private static MeshInstance3D BuildAsymmetricMarker(Color color, float outerRadius)
    {
        var markerMesh = BuildPlanarAnnulusSectorMesh(
            -8.0,
            16.0,
            outerRadius - 0.10f,
            outerRadius - 0.03f,
            TunnelInstrumentContract.GeometryPlaneZ + 0.01f);
        return new MeshInstance3D
        {
            Name = "RingMarker",
            Mesh = markerMesh,
            MaterialOverride = BuildUnlitMaterial(Brighten(color)),
        };
    }

    private static void RemoveAndQueueFree(Node? node)
    {
        if (node is null || !GodotObject.IsInstanceValid(node))
            return;

        node.GetParent()?.RemoveChild(node);
        node.QueueFree();
    }

    private static void EnsureInstrumentParent(Node parent, Node child)
    {
        var childName = child.Name.ToString();
        var parentName = parent.Name.ToString();
        if (!TunnelInstrumentContract.IsExpectedParent(childName, parentName))
        {
            throw new InvalidOperationException(
                $"Instrument node '{childName}' cannot be parented under '{parentName}'.");
        }

        var currentParent = child.GetParent();
        if (ReferenceEquals(currentParent, parent))
            return;
        if (currentParent is null)
            parent.AddChild(child);
        else
            child.Reparent(parent, keepGlobalTransform: false);
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
