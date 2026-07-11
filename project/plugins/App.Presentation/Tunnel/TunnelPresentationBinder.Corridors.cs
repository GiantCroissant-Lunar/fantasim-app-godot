using System;
using System.Linq;
using FantaSim.App.Timeline;
using FantaSim.App.Timeline.Seam;
using FantaSim.App.World.Composition;
using Godot;

namespace FantaSim.App.Presentation.Tunnel;

// Track corridors: sphere-sector wedges built from TrackLaneViewModelBuilder.BuildLanes +
// TunnelCorridorLayout.BuildWedges (plan Task 8 Step 3), with filmstrip-kind wedges additionally
// getting a texture quad wired through FilmstripPreviewController via QuadMaterialFilmstripSink
// (Task 9). Split from the core file 2026-07-11 (vault/plans/2026-07-11-tunnel-slice1-plan.md).
internal sealed partial class TunnelPresentationBinder
{
    // Base tints per presenter kind -- a slice-1 look-dev choice, not spec-pinned. Dimmed alpha
    // copies TimelineFace.Lanes.cs's private DimmedTrackModulateAlpha VALUE (0.55f) verbatim per
    // plan Task 8 Step 3 ("do not reintroduce a different dim level") -- the constant itself is
    // private to that file/assembly, so the value is copied, not referenced.
    private static readonly Color FilmstripWedgeColor = new(0.30f, 0.55f, 0.62f);
    private static readonly Color GraphWedgeColor = new(0.52f, 0.40f, 0.62f);
    private static readonly Color GenericWedgeColor = new(0.42f, 0.44f, 0.46f);
    private const float DimmedWedgeAlpha = 0.55f;
    private const double WedgeGapDeg = 2.0;
    private const float FilmstripQuadHalfWidth = 1.4f;
    private const float FilmstripQuadHalfHeight = 1.0f;

    private Node3D? _corridorsRoot;

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
    }

    private void RebuildCorridors()
    {
        if (_disposed || _mount is null || !GodotObject.IsInstanceValid(_mount)
            || _ctl is null || _layerTrackRegistry is null)
            return;

        EnsureCorridorsRoot();
        ClearChildren(_corridorsRoot!);

        var lanes = TrackLaneViewModelBuilder.BuildLanes(_layerTrackRegistry.Current);
        var wedges = TunnelCorridorLayout.BuildWedges(lanes);
        // Zip: TunnelCorridorLayout.BuildWedges iterates `lanes` in the exact nested lane/track
        // order it was given (Task 3's own contiguous/ordered-span contract + tests), so flattening
        // `lanes` the identical way recovers each wedge's originating descriptor without BuildWedges
        // itself needing to carry it (its record stays UI-shaped, not domain-shaped).
        var descriptors = lanes.SelectMany(lane => lane.Tracks).ToList();

        var globalRung = GlobalRung;

        for (var i = 0; i < wedges.Count; i++)
        {
            var wedge = wedges[i];
            var descriptor = i < descriptors.Count ? descriptors[i].Descriptor : null;

            var baseColor = wedge.PresenterKind switch
            {
                TrackContentPresenterKind.Filmstrip => FilmstripWedgeColor,
                TrackContentPresenterKind.Graph => GraphWedgeColor,
                _ => GenericWedgeColor,
            };
            var alpha = wedge.IsDimmed ? DimmedWedgeAlpha : 1f;

            var start = wedge.StartAngleDeg + (WedgeGapDeg / 2.0);
            var span = Math.Max(0.0, wedge.SpanAngleDeg - WedgeGapDeg);
            var panelMesh = BuildAnnulusSectorMesh(start, span, ThroatRadius, OuterRadius);
            var panel = new MeshInstance3D
            {
                Name = $"Corridor_{SafeNodeName(wedge.SphereId)}_{SafeNodeName(wedge.LayerId)}",
                Mesh = panelMesh,
                MaterialOverride = BuildUnlitMaterial(baseColor, alpha),
            };
            _corridorsRoot!.AddChild(panel);

            var midAngleDeg = wedge.StartAngleDeg + (wedge.SpanAngleDeg / 2.0);
            var midRadius = (ThroatRadius + OuterRadius) / 2.0;

            if (wedge.PresenterKind == TrackContentPresenterKind.Filmstrip && descriptor is not null)
            {
                // Task 9: PLAIN tinted wedge above is already built for every presenter kind
                // (including Filmstrip); this adds the real-texture quad on top, never blocking
                // Task 8's base rendering on Task 9's wiring.
                BuildFilmstripQuad(descriptor, midAngleDeg, midRadius, globalRung);
            }
            else
            {
                // Graph/Generic content types render as a plain labeled/dimmed wedge, spec §7:
                // no in-3D graph, no pop-out yet -- the Unity round-trip degradation guarantee.
                var label = BuildCorridorLabel(descriptor?.DisplayName ?? wedge.LayerId, midAngleDeg, midRadius);
                _corridorsRoot!.AddChild(label);
            }
        }
    }

    private static Label3D BuildCorridorLabel(string text, double midAngleDeg, double midRadius)
    {
        var rad = Math.PI / 180.0 * midAngleDeg;
        var position = new Vector3((float)(Math.Cos(rad) * midRadius), (float)(Math.Sin(rad) * midRadius), 0.05f);
        return new Label3D
        {
            Name = "CorridorLabel",
            Text = text,
            Position = position,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 28,
            Modulate = new Color(0.92f, 0.94f, 0.97f, 0.95f),
            OutlineModulate = new Color(0f, 0f, 0f, 0.65f),
            NoDepthTest = true,
        };
    }

    // Task 9 Step 2: "pick the simpler single-quad-per-corridor for slice 1" -- one small flat quad
    // tangent to the wedge's mid-radius, not a curved wedge-shaped texture. Task 9 Step 1's
    // FilmstripPreviewController instance (constructed once in the ctor, provider set per-Rebind)
    // is reused verbatim through the sink seam (Task 5/Task 6), same cache/queue/ALC-discipline
    // machinery the 2D face uses -- this is purely a different sink for the same texture.
    private void BuildFilmstripQuad(LayerTrackDescriptor descriptor, double midAngleDeg, double midRadius, TimelineLadderRung globalRung)
    {
        if (_ctl is null)
            return;

        var rad = Math.PI / 180.0 * midAngleDeg;
        var center = new Vector3((float)(Math.Cos(rad) * midRadius), (float)(Math.Sin(rad) * midRadius), 0.06f);
        var tangent = new Vector3((float)-Math.Sin(rad), (float)Math.Cos(rad), 0f);
        var radial = new Vector3((float)Math.Cos(rad), (float)Math.Sin(rad), 0f);

        var q0 = center - (tangent * FilmstripQuadHalfWidth) - (radial * FilmstripQuadHalfHeight);
        var q1 = center + (tangent * FilmstripQuadHalfWidth) - (radial * FilmstripQuadHalfHeight);
        var q2 = center + (tangent * FilmstripQuadHalfWidth) + (radial * FilmstripQuadHalfHeight);
        var q3 = center - (tangent * FilmstripQuadHalfWidth) + (radial * FilmstripQuadHalfHeight);

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
            Name = $"FilmstripQuad_{SafeNodeName(descriptor.SphereId)}_{SafeNodeName(descriptor.LayerId)}",
            Mesh = mesh,
            MaterialOverride = material,
        };
        _corridorsRoot!.AddChild(owner);

        var sink = new QuadMaterialFilmstripSink(owner, material);
        // First real consumer of LayerTrackDescriptor.TimeDomain.Rung (plan's Grounding facts /
        // TunnelCorridorLayout.ResolveCorridorRung's own doc comment): an unrecognized or absent
        // native rung symbol degrades to the tunnel's global rung, never a throw.
        var rung = TunnelCorridorLayout.ResolveCorridorRung(descriptor.TimeDomain.Rung, globalRung);

        // Slice-1 simplification (plan Task 9 Step 1): graphRevision fixed at 0. The tunnel has no
        // cheap access to the generation-graph-family revision the 2D face threads through
        // (WorldService.GetPlanetPresentationAsync(tick).GenerationGraphFamily is an expensive call
        // to make once per corridor per rebuild); the tunnel's filmstrip cache is already documented
        // as a SEPARATE cache from the 2D face's (independent re-fetch/re-cache is expected, not a
        // bug), so this is a compatible degradation, not a new one.
        _filmstrip.RequestTexture(sink, descriptor.SphereId, descriptor.LayerId, _ctl.Tick, rung.Symbol, graphRevision: 0);
    }

    private void OnRegistryChanged(LayerTrackRegistrySnapshot snapshot)
    {
        // SetArchived/Reload may be invoked from a command handler off the main thread; the
        // rebuild walks into AddChild/QueueFree, which Godot only allows on the main thread --
        // same CallDeferred discipline TimelineFace.OnLayerTrackRegistryChanged already uses.
        if (OS.GetThreadCallerId() == OS.GetMainThreadId())
        {
            RebuildCorridors();
            return;
        }

        Callable.From(RebuildCorridors).CallDeferred();
    }
}
