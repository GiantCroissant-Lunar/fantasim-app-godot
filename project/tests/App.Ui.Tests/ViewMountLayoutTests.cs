using FantaSim.App.Ui.Providers;
using Xunit;

namespace FantaSim.App.Ui.Tests;

/// <summary>
/// Layout contract for the resident UI panels (world node graph, activity ledger) per the
/// 2026-07-04 globe-surface roadmap: viewport rects must be computed from window size, unit-tested,
/// and the docked panels must not overlap the globe viewport region (the centered 3D scene). These
/// tests capture the documented defect where the graph panel + activity ledger crowd the globe.
/// </summary>
public sealed class ViewMountLayoutTests
{
    // The globe viewport is the 3D scene mounted under Environment/PlanetMount. Its visible region
    // is the center of the window, clear of the docked side panels. A representative window size.
    private const float ViewportWidth = 1920f;
    private const float ViewportHeight = 1080f;

    [Fact]
    public void WorldGraphPanel_is_docked_left_and_does_not_overlap_the_globe_center()
    {
        var graphRect = ViewMountLayout.PlanMountRect(ViewMountLayout.WorldGraphViewId)
            .Resolve(ViewportWidth, ViewportHeight);

        // The panel docks to the left edge, spanning graphPanelWidth (760px) from the left margin.
        Assert.Equal(ViewMountLayout.Edge, graphRect.Left);
        Assert.Equal(ViewMountLayout.Edge + ViewMountLayout.GraphPanelWidth, graphRect.Left + graphRect.Width);

        // The globe viewport region is the center column to the right of the graph panel. Assert no
        // overlap with a rect that starts just past the graph panel and spans the center.
        var globeRegion = (
            Left: graphRect.Left + graphRect.Width + 8f,
            Top: ViewMountLayout.Top,
            Width: ViewportWidth - (graphRect.Left + graphRect.Width + 8f) - ViewMountLayout.SidePanelWidth - ViewMountLayout.Edge,
            Height: ViewportHeight - ViewMountLayout.Top - ViewMountLayout.Edge - ViewMountLayout.TimelineReservedHeight - ViewMountLayout.TimelineGap);

        Assert.False(ViewMountLayout.Overlaps(graphRect, globeRegion),
            "world graph panel must not overlap the globe viewport center region");
    }

    [Fact]
    public void ActivityPanel_is_docked_right_and_does_not_overlap_the_globe_center()
    {
        var activityRect = ViewMountLayout.PlanMountRect(ViewMountLayout.ActivityViewId)
            .Resolve(ViewportWidth, ViewportHeight);

        // The panel docks to the right edge, spanning sidePanelWidth (460px) in from the right margin.
        Assert.Equal(ViewportWidth - ViewMountLayout.SidePanelWidth, activityRect.Left);
        Assert.Equal(ViewportWidth - ViewMountLayout.Edge, activityRect.Left + activityRect.Width);

        var globeRegion = (
            Left: ViewMountLayout.Edge + ViewMountLayout.GraphPanelWidth + 8f,
            Top: ViewMountLayout.Top,
            Width: activityRect.Left - (ViewMountLayout.Edge + ViewMountLayout.GraphPanelWidth + 8f) - 8f,
            Height: ViewportHeight - ViewMountLayout.Top - ViewMountLayout.Edge - ViewMountLayout.TimelineReservedHeight - ViewMountLayout.TimelineGap);

        Assert.False(ViewMountLayout.Overlaps(activityRect, globeRegion),
            "activity panel must not overlap the globe viewport center region");
    }

    [Fact]
    public void WorldGraphPanel_and_activityPanel_do_not_overlap_each_other()
    {
        var graphRect = ViewMountLayout.PlanMountRect(ViewMountLayout.WorldGraphViewId)
            .Resolve(ViewportWidth, ViewportHeight);
        var activityRect = ViewMountLayout.PlanMountRect(ViewMountLayout.ActivityViewId)
            .Resolve(ViewportWidth, ViewportHeight);

        Assert.False(ViewMountLayout.Overlaps(graphRect, activityRect),
            "world graph panel and activity panel must not overlap");
    }

    [Fact]
    public void WorldGraphPanel_reserves_the_timeline_strip_at_the_bottom()
    {
        var graphRect = ViewMountLayout.PlanMountRect(ViewMountLayout.WorldGraphViewId)
            .Resolve(ViewportWidth, ViewportHeight);

        // The panel must end above the timeline strip (timelineReservedHeight + gap + edge from bottom).
        var expectedBottom = ViewportHeight - (ViewMountLayout.Edge + ViewMountLayout.TimelineReservedHeight + ViewMountLayout.TimelineGap);
        Assert.Equal(expectedBottom, graphRect.Top + graphRect.Height);
    }

    [Fact]
    public void Default_rect_spans_full_width_below_the_top_bar()
    {
        var defaultRect = ViewMountLayout.PlanMountRect("some-other-view")
            .Resolve(ViewportWidth, ViewportHeight);

        Assert.Equal(ViewMountLayout.Edge, defaultRect.Left);
        Assert.Equal(ViewportWidth - ViewMountLayout.Edge, defaultRect.Left + defaultRect.Width);
        Assert.Equal(ViewMountLayout.Top, defaultRect.Top);
    }

    [Fact]
    public void PlanMountRect_is_deterministic_across_calls()
    {
        var first = ViewMountLayout.PlanMountRect(ViewMountLayout.WorldGraphViewId);
        var second = ViewMountLayout.PlanMountRect(ViewMountLayout.WorldGraphViewId);
        Assert.Equal(first, second);
    }
}