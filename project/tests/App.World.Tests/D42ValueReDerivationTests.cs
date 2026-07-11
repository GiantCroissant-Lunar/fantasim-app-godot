using FantaSim.App.World.Composition;
using FantaSim.App.World.Services;
using FantaSim.World.Contracts.Units;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// D4.2 Task 4: pins the re-derived tick values against stated intent at the canonical
/// 100k ticks/Ma conversion. These tests prevent the values from drifting back to the
/// wrong- assumption (1M ticks/Ma) originals.
/// </summary>
public sealed class D42ValueReDerivationTests
{
    [Fact]
    public void MobilePlateWindowTicks_matches_stated_intent_of_1_Gy_at_100k_ticks_per_Ma()
    {
        // Stated intent: "1 Gy" = 1000 Ma. At 100,000 ticks/Ma: 1000 * 100,000 = 100,000,000 ticks.
        const long statedIntentMegaAnnum = 1000L; // 1 Gy = 1000 Ma
        long expected = statedIntentMegaAnnum * UnitConverter.TicksPerMegaAnnum;

        Assert.Equal(expected, Service.MobilePlateWindowTicks);
    }

    [Fact]
    public void PlateFeatureFadeInTicks_matches_stated_intent_of_5_Ma_at_100k_ticks_per_Ma()
    {
        // Stated intent: "~5 Ma". At 100,000 ticks/Ma: 5 * 100,000 = 500,000 ticks.
        const double statedIntentMegaAnnum = 5.0;
        long expected = UnitConverter.MegaAnnumToTickDelta(statedIntentMegaAnnum);

        Assert.Equal(expected, SphereRegimeScheduleDefaults.PlateFeatureFadeInTicks);
    }
}