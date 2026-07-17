using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// D3 ratio-lock proof for <see cref="RadialSectionProfile"/>. The default knobs yield a fixed
/// displayed crust:mantle proportion; this test PINS it so any future knob change that silently
/// breaks the proportion fails here and forces a conscious decision (re-tune the knobs, or re-pin
/// the declared ratio if the design intent genuinely moved).
/// </summary>
public sealed class RadialSectionProfileTests
{
    // The pinned default ratio: 30 km × 8.0 / 6,371 km ≈ 0.03767R of crust against
    // (1 − 0.55) × 1.0 = 0.45R of mantle depth → 0.03767 / 0.45 ≈ 0.08371.
    // Eye-tuned 2026-07-17 with DefaultCrustThicknessExaggeration 8->36 (user's non-realistic-
    // scale directive): 30 km x 36 / 6,371 km / 0.45R mantle ~= 0.3767.
    private const double ExpectedDefaultRatio = 0.3767;
    private const double RatioTolerance = 0.0005;

    [Fact]
    public void Default_crust_fraction_is_visible_against_mantle()
    {
        var profile = RadialSectionProfile.Default;

        double crust = profile.DisplayedCrustFraction();
        double mantle = profile.DisplayedMantleDepthFraction();

        // Crust reads as visible slab walls: 30 km × 8.0 / 6,371 km ≈ 0.0377R (well above the
        // ~0.0009R invisibility of the old surface-relief-coupled exaggeration).
        Assert.True(crust > 0.03, $"default crust fraction must read as visible (>0.03R): {crust}");
        // Mantle depth keeps its physical extent: (1 − 0.55) × 1.0 = 0.45R.
        Assert.Equal(0.45, mantle, 9);
    }

    [Fact]
    public void Default_profile_pins_the_displayed_crust_to_mantle_ratio()
    {
        var profile = RadialSectionProfile.Default;
        double ratio = profile.DisplayedCrustToMantleRatio();

        Assert.Equal(ExpectedDefaultRatio, ratio, RatioTolerance);
    }

    [Fact]
    public void Default_profile_constants_match_record_defaults()
    {
        // The record's primary-constructor defaults must equal the named constants so callers
        // reaching for either stay in sync. If a knob moves, move BOTH or this test fails.
        var profile = RadialSectionProfile.Default;

        Assert.Equal(RadialSectionProfile.DefaultCmbRadiusFraction, profile.CmbRadiusFraction);
        Assert.Equal(RadialSectionProfile.DefaultCrustThicknessExaggeration, profile.CrustThicknessExaggeration);
        Assert.Equal(RadialSectionProfile.DefaultMantleDepthScale, profile.MantleDepthScale);
    }

    [Fact]
    public void Core_sphere_radius_reads_cmb_times_mantle_scale()
    {
        var profile = RadialSectionProfile.Default;
        // Default: 0.55 × 1.0 = 0.55R (the old hardcoded literal, now profile-driven).
        Assert.Equal(0.55, profile.DisplayedCoreSphereRadius(), 9);

        var scaled = profile with { MantleDepthScale = 1.2 };
        // 0.55 × 1.2 = 0.66R — scaling the mantle depth also scales the core backdrop.
        Assert.Equal(0.66, scaled.DisplayedCoreSphereRadius(), 9);
    }

    [Fact]
    public void Doubling_crust_exaggeration_doubles_displayed_crust_fraction()
    {
        // The crust exaggeration knob is LINEAR in crust fraction, independent of the mantle scale.
        var profile = RadialSectionProfile.Default;
        double baseline = profile.DisplayedCrustFraction();

        var amplified = profile with { CrustThicknessExaggeration = profile.CrustThicknessExaggeration * 2.0 };
        Assert.Equal(baseline * 2.0, amplified.DisplayedCrustFraction(), 9);
    }
}
