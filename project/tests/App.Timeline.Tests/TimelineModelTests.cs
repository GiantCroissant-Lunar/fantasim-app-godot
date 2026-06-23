using System;
using System.Linq;
using FantaSim.App.World.Composition;
using FantaSim.App.Timeline;
using Xunit;

namespace App.Timeline.Tests;

public class TimelineModelTests
{
    private static SphereRegimeSchedule Geo() =>
        SphereRegimeScheduleDefaults.GeosphereFor(SphereRegimeScheduleDefaults.PlateOnsetTick); // onset = 1e8

    [Fact]
    public void Bands_AreProportional_AndCoverZeroToOne()
    {
        var bands = TimelineModel.Bands(Geo(), maxTick: 120_000_000, currentTick: 0);
        Assert.Equal(3, bands.Count);                              // magma / lid / mobile
        Assert.Equal(0.0, bands[0].StartFraction, 6);             // magma starts at 0
        // widths sum to maxTick coverage (mobile clamped to maxTick): ~1.0
        Assert.Equal(1.0, bands.Sum(b => b.WidthFraction), 3);
        Assert.True(bands[0].WidthFraction < bands[1].WidthFraction); // magma (1e6) << lid (1e6..1e8)
    }

    [Fact]
    public void Bands_MarkActiveRegime()
    {
        var bands = TimelineModel.Bands(Geo(), 120_000_000, currentTick: 500_000); // magma-ocean
        Assert.Equal("magma-ocean", bands.Single(b => b.IsActive).RegimeId);

        var atOnset = TimelineModel.Bands(Geo(), 120_000_000, currentTick: 100_000_000); // mobile-plate
        Assert.Equal("mobile-plate", atOnset.Single(b => b.IsActive).RegimeId);
    }

    [Fact]
    public void Bands_ZoomedView_ClipsAndRenormalizes()
    {
        var bands = TimelineModel.Bands(
            Geo(),
            maxTick: 120_000_000,
            currentTick: 100_000_000,
            viewStartTick: 99_000_000,
            viewEndTick: 101_000_000);

        Assert.Equal(2, bands.Count);
        Assert.Equal("stagnant-lid", bands[0].RegimeId);
        Assert.Equal(0.0, bands[0].StartFraction, 6);
        Assert.Equal(0.5, bands[0].WidthFraction, 6);
        Assert.Equal(99_000_000, bands[0].StartTick);
        Assert.Equal(100_000_000, bands[0].EndTick);

        Assert.Equal("mobile-plate", bands[1].RegimeId);
        Assert.Equal(0.5, bands[1].StartFraction, 6);
        Assert.Equal(0.5, bands[1].WidthFraction, 6);
    }

    [Fact]
    public void Tracks_ListAllLayers_HighlightActive()
    {
        var tracks = TimelineModel.Tracks(Geo(), currentTick: 500_000); // magma regime active
        Assert.Contains(tracks, t => t.LayerId == "geosphere.magma-ocean" && t.IsActive);
        Assert.Contains(tracks, t => t.LayerId == "geosphere.plate" && !t.IsActive);
    }

    [Fact]
    public void Ruler_UsesCanonicalOdometerLabels()
    {
        var marks = TimelineModel.Ruler(0, 120_000_000, targetMarkCount: 8);

        Assert.Contains(marks, mark => mark.Tick == 100_000_000 && mark.Label == "1 kb");
        Assert.All(marks, mark => Assert.DoesNotContain("Ma", mark.Label));
    }

    [Fact]
    public void ZoomReadout_UsesOneCanonicalScaleForRangeAndStep()
    {
        var readout = TimelineTimeFormatter.ForViewRange(
            viewStartTick: 0,
            viewEndTick: 120_000_000,
            stepTick: 20_000_000);

        Assert.Equal("view 0 kb - 1.20 kb | step 0.20 kb", readout);
    }

    [Fact]
    public void ZoomReadout_SelectsSmallCanonicalScaleForTightRanges()
    {
        var readout = TimelineTimeFormatter.ForViewRange(
            viewStartTick: 0,
            viewEndTick: 8,
            stepTick: 1);

        Assert.Equal("view 0 jy - 80 jy | step 10 jy", readout);
    }

    [Fact]
    public void ZoomReadout_SelectsScaleFromZoomSpan_NotEpochMagnitude()
    {
        var readout = TimelineTimeFormatter.ForViewRange(
            viewStartTick: 100_000_000,
            viewEndTick: 100_000_008,
            stepTick: 1);

        Assert.EndsWith("| step 10 jy", readout);
        Assert.DoesNotContain("kb", readout);
    }

    [Fact]
    public void Ruler_UsesOneCanonicalScaleForZoomSpan()
    {
        var marks = TimelineModel.Ruler(100_000_000, 100_000_008, targetMarkCount: 8);

        Assert.NotEmpty(marks);
        Assert.All(marks, mark => Assert.EndsWith("jy", mark.Label));
        Assert.All(marks, mark => Assert.DoesNotContain("kb", mark.Label));
    }

    [Fact]
    public void Ruler_MinimumStep_RespectsIntegerCanonicalTicks()
    {
        long step = TimelineModel.RulerStepTicks(0, 8, targetMarkCount: 8);

        Assert.Equal(1, step);
        Assert.Equal("10 jy", TimelineTimeFormatter.ForTick(step));
    }

    // FIX 8 guard: lock the gap-free candidate invariant for mid-zoom spans.
    [Theory]
    [InlineData(8_000L)]
    [InlineData(58_000L)]
    public void RulerStep_MidZoomSpan_IsNotDegenerate(long span)
    {
        const int targetMarkCount = 8;

        long step = TimelineModel.RulerStepTicks(0, span, targetMarkCount: targetMarkCount);
        Assert.True(step > 0, "step must be positive");
        int markCount = (int)Math.Ceiling(span / (double)step);

        Assert.InRange(markCount, 4, 16);
    }

    [Theory]
    [InlineData(8_000L)]
    [InlineData(58_000L)]
    public void Ruler_MidZoomSpan_ProducesSaneMarkCount(long span)
    {
        var marks = TimelineModel.Ruler(0, span, targetMarkCount: 8);

        Assert.InRange(marks.Count, 4, 16);
    }

    [Fact]
    public void Ruler_DenseSpanSweep_NeverDegenerate()
    {
        for (long span = 100; span <= 1_000_000; span += 100)
        {
            long step = TimelineModel.RulerStepTicks(0, span, targetMarkCount: 8);
            Assert.True(step > 0, $"step<=0 at span={span}");
            int marks = (int)Math.Ceiling(span / (double)step);
            Assert.True(marks >= 4 && marks <= 16, $"degenerate at span={span}: step={step} marks={marks}");
        }
    }

    // FIX 9: degenerate range (MaxTick == 0 / viewEnd <= viewStart) must not throw.
    [Fact]
    public void Ruler_EmptyRange_ReturnsEmpty_DoesNotThrow()
    {
        Assert.Null(Record.Exception(() => TimelineModel.Ruler(0, 0)));
        Assert.Empty(TimelineModel.Ruler(0, 0));

        Assert.Null(Record.Exception(() => TimelineModel.Ruler(7, 7)));
        Assert.Empty(TimelineModel.Ruler(7, 7));

        Assert.Null(Record.Exception(() => TimelineModel.Ruler(10, 3)));
        Assert.Empty(TimelineModel.Ruler(10, 3));
    }

    [Fact]
    public void RulerStepTicks_EmptyRange_ReturnsZero_DoesNotThrow()
    {
        Assert.Null(Record.Exception(() => TimelineModel.RulerStepTicks(0, 0)));
        Assert.Equal(0L, TimelineModel.RulerStepTicks(0, 0));

        Assert.Null(Record.Exception(() => TimelineModel.RulerStepTicks(7, 7)));
        Assert.Equal(0L, TimelineModel.RulerStepTicks(7, 7));

        Assert.Null(Record.Exception(() => TimelineModel.RulerStepTicks(10, 3)));
        Assert.Equal(0L, TimelineModel.RulerStepTicks(10, 3));
    }
}
