using FantaSim.App.Timeline;
using Xunit;

namespace FantaSim.App.Timeline.Tests;

public sealed class TimelineTrackLayoutTests
{
    [Fact]
    public void Plan_MixesCompactAndExpandedRowsWithoutGaps()
    {
        var plan = TimelineTrackLayout.Plan(new[]
        {
            new TimelineTrackLayoutInput("plate", IsExpanded: false),
            new TimelineTrackLayoutInput("crust", IsExpanded: true),
            new TimelineTrackLayoutInput("mantle", IsExpanded: false),
        });

        Assert.Equal(252f, plan.TotalHeight);
        Assert.Collection(
            plan.Rows,
            row =>
            {
                Assert.Equal("plate", row.TrackKey);
                Assert.Equal(0f, row.Y);
                Assert.Equal(26f, row.Height);
            },
            row =>
            {
                Assert.Equal("crust", row.TrackKey);
                Assert.Equal(26f, row.Y);
                Assert.Equal(200f, row.Height);
            },
            row =>
            {
                Assert.Equal("mantle", row.TrackKey);
                Assert.Equal(226f, row.Y);
                Assert.Equal(26f, row.Height);
            });
    }
}
