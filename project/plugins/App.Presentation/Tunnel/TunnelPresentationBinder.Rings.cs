using System;
using FantaSim.App.Timeline;
using FantaSim.App.Timeline.Seam;
using Godot;

namespace FantaSim.App.Presentation.Tunnel;

// Depth rings: the ladder ruler (spec §2.2) reused verbatim through TimelineModel.Ruler, plus a
// separately-colored, cheaply-rebuilt current-tick ring (plan Task 8 Step 2). Split from the core
// file 2026-07-11 (vault/plans/2026-07-11-tunnel-slice1-plan.md Task 8).
internal sealed partial class TunnelPresentationBinder
{
    private const float LadderRingThickness = 0.04f;
    private const float CurrentTickRingThickness = 0.09f;
    private static readonly Color LadderRingColor = new(0.35f, 0.62f, 0.78f, 0.85f);
    private static readonly Color CurrentTickRingColor = new(0.98f, 0.72f, 0.20f, 0.95f); // amber, per the wireframe's jogRing

    private Node3D? _ladderRingsRoot;
    private Node3D? _currentTickRingRoot;

    // Read by Input.cs (Task 10) for the current-tick ring's screen-space hit test.
    private float _currentTickRingRadius;

    // Slice 1 reuses whichever [0, MaxTick] view range the 2D face's zoom controls already imply
    // globally -- plan Task 8 Step 2: "the tunnel does not own a second view-range." A per-track
    // native rung (spec §3.2) is intentionally NOT read here; that stays deferred (Decision Points
    // 5/6) to Corridors.cs' filmstrip request only (Task 9), the one place this slice actually
    // consumes LayerTrackDescriptor.TimeDomain.Rung.
    private TimelineLadderRung GlobalRung => TimelineModel.SelectRungForSpan(Math.Max(0L, _ctl?.MaxTick ?? 0L));

    private void EnsureRingRoots()
    {
        if (_mount is null)
            return;

        if (_ladderRingsRoot is null || !GodotObject.IsInstanceValid(_ladderRingsRoot))
        {
            _ladderRingsRoot = new Node3D { Name = "LadderRings" };
            _mount.AddChild(_ladderRingsRoot);
        }

        if (_currentTickRingRoot is null || !GodotObject.IsInstanceValid(_currentTickRingRoot))
        {
            _currentTickRingRoot = new Node3D { Name = "CurrentTickRing" };
            _mount.AddChild(_currentTickRingRoot);
        }
    }

    private void ClearRingRoots()
    {
        _ladderRingsRoot = null;
        _currentTickRingRoot = null;
    }

    private void RebuildRings()
    {
        if (_disposed || _mount is null || !GodotObject.IsInstanceValid(_mount) || _ctl is null)
            return;

        EnsureRingRoots();
        ClearChildren(_ladderRingsRoot!);

        var maxTick = Math.Max(0L, _ctl.MaxTick);
        var marks = TimelineModel.Ruler(0L, maxTick, GlobalRung);

        foreach (var mark in marks)
        {
            var radius = (float)TunnelDepthMapper.RadiusForFraction(mark.Fraction, ThroatRadius, OuterRadius);

            var ringMesh = BuildAnnulusSectorMesh(0.0, 360.0, radius - (LadderRingThickness / 2f), radius + (LadderRingThickness / 2f));
            var ring = new MeshInstance3D
            {
                Name = "Ring",
                Mesh = ringMesh,
                MaterialOverride = BuildUnlitMaterial(LadderRingColor),
            };
            _ladderRingsRoot!.AddChild(ring);

            var label = new Label3D
            {
                Name = "RingLabel",
                Text = mark.Label,
                Position = new Vector3(radius, 0f, 0.05f),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                FontSize = 32,
                Modulate = new Color(0.85f, 0.90f, 0.95f, 0.90f),
                OutlineModulate = new Color(0f, 0f, 0f, 0.65f),
                NoDepthTest = true,
            };
            _ladderRingsRoot!.AddChild(label);
        }

        RebuildCurrentTickRing();
    }

    // Rebuilt on every OnTickChanged -- cheap, one ring, never the whole ladder (plan Task 8 Step 2).
    private void RebuildCurrentTickRing()
    {
        if (_disposed || _mount is null || !GodotObject.IsInstanceValid(_mount) || _ctl is null)
            return;

        EnsureRingRoots();
        ClearChildren(_currentTickRingRoot!);

        var maxTick = Math.Max(0L, _ctl.MaxTick);
        var fraction = TimelineScrubMapper.TickToFraction(_ctl.Tick, 0L, maxTick);
        var radius = (float)TunnelDepthMapper.RadiusForFraction(fraction, ThroatRadius, OuterRadius);
        _currentTickRingRadius = radius;

        var ringMesh = BuildAnnulusSectorMesh(0.0, 360.0, radius - (CurrentTickRingThickness / 2f), radius + (CurrentTickRingThickness / 2f));
        var ring = new MeshInstance3D
        {
            Name = "Ring",
            Mesh = ringMesh,
            MaterialOverride = BuildUnlitMaterial(CurrentTickRingColor),
        };
        _currentTickRingRoot!.AddChild(ring);
    }

    private void OnTickChanged(long tick)
    {
        // SetArchived/PushTick may fire from a command handler off the main thread; the rebuild
        // walks into AddChild/QueueFree, which Godot only allows on the main thread -- same
        // CallDeferred discipline TimelineFace.OnLayerTrackRegistryChanged already uses.
        if (OS.GetThreadCallerId() == OS.GetMainThreadId())
        {
            RebuildCurrentTickRing();
            return;
        }

        Callable.From(RebuildCurrentTickRing).CallDeferred();
    }
}
