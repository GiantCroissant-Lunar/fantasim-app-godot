namespace FantaSim.App.Ui.Providers;

/// <summary>
/// Deterministic layout contract for <see cref="IViewHost"/> mount rects. The 2026-07-04 globe-surface
/// roadmap calls for viewport rects computed from window size and unit-tested, not another offset
/// nudge. This record captures the constants and the pure planner so <see cref="ViewHost"/> does not
/// own layout arithmetic, and tests can assert the world-graph panel never overlaps the globe viewport
/// region (the documented defect: panel + activity ledger crowd the globe).
/// </summary>
public static class ViewMountLayout
{
    // Edge insets and reserved regions. These match the values previously inlined in
    // ViewHost.ConfigureMountLayout; centralized here so the layout is owned in one place.
    public const float Edge = 12f;
    public const float Top = 44f;
    public const float SidePanelWidth = 460f;
    public const float GraphPanelWidth = 760f;
    public const float TimelineReservedHeight = 292f;
    public const float TimelineGap = 8f;

    /// <summary>Well-known view ids that get a dedicated mount rect. Anything else falls back to the
    /// full-width default (the globe viewport stays clear of only the side panels).</summary>
    public const string ActivityViewId = "activity";
    public const string WorldGraphViewId = "world-generation-node-graph";

    /// <summary>A mount rect expressed in the Godot anchor/offset model: anchors are fractions of the
    /// parent [0,1] and offsets are pixel insets from the anchor. <see cref="Right"/>/-<see cref="Bottom"/>
    /// are positive insets from the right/bottom edge (so a Bottom of -300 means 300px up from bottom).
    /// Tests assert against the resolved pixel rect via <see cref="Resolve"/>.</summary>
    public readonly record struct MountRect(
        float AnchorLeft,
        float AnchorRight,
        float AnchorTop,
        float AnchorBottom,
        float OffsetLeft,
        float OffsetRight,
        float OffsetTop,
        float OffsetBottom)
    {
        /// <summary>Resolve the rect to pixel coordinates against a viewport of the given size. Used by
        /// tests to assert non-overlap with the globe viewport region (the centered 3D scene).</summary>
        public (float Left, float Top, float Width, float Height) Resolve(float viewportWidth, float viewportHeight)
        {
            var left = AnchorLeft * viewportWidth + OffsetLeft;
            var right = AnchorRight * viewportWidth + OffsetRight;
            var top = AnchorTop * viewportHeight + OffsetTop;
            var bottom = AnchorBottom * viewportHeight + OffsetBottom;
            return (left, top, Math.Max(0f, right - left), Math.Max(0f, bottom - top));
        }
    }

    /// <summary>Compute the mount rect for a view id against a viewport of the given size. The globe
    /// viewport occupies the full window under the UI overlay; the world-graph and activity panels are
    /// docked to the left/right edges respectively, reserving the timeline strip at the bottom. The
    /// center column between them stays clear for the globe.</summary>
    public static MountRect PlanMountRect(string viewId)
    {
        if (string.Equals(viewId, ActivityViewId, System.StringComparison.Ordinal))
        {
            return new MountRect(
                AnchorLeft: 1f, AnchorRight: 1f,
                AnchorTop: 0f, AnchorBottom: 1f,
                OffsetLeft: -SidePanelWidth, OffsetRight: -Edge,
                OffsetTop: Top, OffsetBottom: -(Edge + TimelineReservedHeight + TimelineGap));
        }

        if (string.Equals(viewId, WorldGraphViewId, System.StringComparison.Ordinal))
        {
            return new MountRect(
                AnchorLeft: 0f, AnchorRight: 0f,
                AnchorTop: 0f, AnchorBottom: 1f,
                OffsetLeft: Edge, OffsetRight: Edge + GraphPanelWidth,
                OffsetTop: Top, OffsetBottom: -(Edge + TimelineReservedHeight + TimelineGap));
        }

        // Default: full width minus edges, below the top bar, above the timeline strip. This is the
        // globe viewport's overlay lane — resident views that are not the side panels stay clear of
        // the docked graph/activity columns by spanning the center only when those are absent.
        return new MountRect(
            AnchorLeft: 0f, AnchorRight: 1f,
            AnchorTop: 0f, AnchorBottom: 1f,
            OffsetLeft: Edge, OffsetRight: -Edge,
            OffsetTop: Top, OffsetBottom: -(Edge + TimelineReservedHeight + TimelineGap));
    }

    /// <summary>True if the two resolved pixel rects overlap (share any interior area). Used by tests
    /// to assert the world-graph panel and the globe viewport region do not collide.</summary>
    public static bool Overlaps(
        (float Left, float Top, float Width, float Height) a,
        (float Left, float Top, float Width, float Height) b)
    {
        if (a.Width <= 0f || a.Height <= 0f || b.Width <= 0f || b.Height <= 0f)
            return false;
        return a.Left < b.Left + b.Width
            && b.Left < a.Left + a.Width
            && a.Top < b.Top + b.Height
            && b.Top < a.Top + a.Height;
    }
}