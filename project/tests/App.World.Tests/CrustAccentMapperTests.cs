using System;
using FantaSim.App.World.Rendering;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Typed crust-feature accent proof (sub-project A2): each CrustFeatureKind that reaches the app
/// maps to a surface-level accent that gives the boundary-profile bands a face-on identity (they
/// are 2-4 cells wide and otherwise vanish without relief silhouetting them). VolcanicArc →
/// emissive vent (magnitude → intensity), Trench → darkened groove, Ridge → bright seam, Mountain
/// → bright cap. Fault/None leave the hypsometric tint untouched.
/// </summary>
public sealed class CrustAccentMapperTests
{
    // Feature kind bytes mirror FantaSim.Geosphere.Crust.CrustFeatureKind (None=0..Fault=5).
    private const byte None = 0;
    private const byte Mountain = 1;
    private const byte VolcanicArc = 2;
    private const byte Trench = 3;
    private const byte Ridge = 4;
    private const byte Fault = 5;

    [Fact]
    public void None_is_neutral()
    {
        var a = CrustAccentMapper.Map(None, magnitude: 1.0);
        Assert.Equal(1.0, a.AlbedoScale);
        Assert.Equal(0.0, a.AlbedoBrighten);
        Assert.Equal(0.0, a.VolcanicEmission);
    }

    [Fact]
    public void Mountain_brightens_for_face_on_identity()
    {
        // Mountains get an explicit brighten so a mountain cap reads bright face-on even when its
        // normalized elevation does not land at the ramp's top band (the bands are 2-4 cells wide;
        // without an accent they vanish without a relief silhouette).
        var a = CrustAccentMapper.Map(Mountain, magnitude: 50.0);
        Assert.True(a.AlbedoBrighten > 0.0, $"mountain must brighten, got {a.AlbedoBrighten}");
        Assert.True(a.AlbedoBrighten <= 0.08, $"mountain brighten must stay subtle, got {a.AlbedoBrighten}");
        Assert.Equal(1.0, a.AlbedoScale);
        Assert.Equal(0.0, a.VolcanicEmission);
    }

    [Fact]
    public void Fault_is_neutral_polylines_handle_it()
    {
        var a = CrustAccentMapper.Map(Fault, magnitude: 1.0);
        Assert.Equal(1.0, a.AlbedoScale);
        Assert.Equal(0.0, a.AlbedoBrighten);
        Assert.Equal(0.0, a.VolcanicEmission);
    }

    [Fact]
    public void VolcanicArc_emission_increases_with_magnitude()
    {
        var low = CrustAccentMapper.Map(VolcanicArc, magnitude: 5.0);
        var high = CrustAccentMapper.Map(VolcanicArc, magnitude: 40.0);

        Assert.True(low.VolcanicEmission > 0.0, "low-magnitude arc must still glow");
        Assert.True(high.VolcanicEmission > low.VolcanicEmission,
            $"arc emission must increase with magnitude: {high.VolcanicEmission} !> {low.VolcanicEmission}");
        // Emission is the ONLY effect — the albedo is unchanged (the vent tint is the glow, not the base).
        Assert.Equal(1.0, high.AlbedoScale);
        Assert.Equal(0.0, high.AlbedoBrighten);
    }

    [Fact]
    public void VolcanicArc_emission_clamps_at_maximum()
    {
        // A runaway magnitude must not blow past the shader's emission-energy range.
        var huge = CrustAccentMapper.Map(VolcanicArc, magnitude: 1_000_000.0);
        Assert.True(huge.VolcanicEmission <= 1.0,
            $"emission must clamp at 1.0, got {huge.VolcanicEmission}");
    }

    [Fact]
    public void VolcanicArc_below_threshold_produces_no_emission()
    {
        // Magnitude 0 or near-zero → no glow (the arc hasn't crossed its activity threshold yet).
        var cold = CrustAccentMapper.Map(VolcanicArc, magnitude: 0.0);
        Assert.Equal(0.0, cold.VolcanicEmission);
    }

    [Fact]
    public void Trench_darkens_albedo_into_a_groove()
    {
        var a = CrustAccentMapper.Map(Trench, magnitude: 1.0);
        Assert.InRange(a.AlbedoScale, 0.58, 0.68);
        Assert.Equal(0.0, a.VolcanicEmission);
        Assert.Equal(0.0, a.AlbedoBrighten);
    }

    [Fact]
    public void Ridge_brightens_clearly()
    {
        var a = CrustAccentMapper.Map(Ridge, magnitude: 1.0);
        Assert.True(a.AlbedoBrighten > 0.04,
            $"ridge must brighten clearly (face-on band contrast), got {a.AlbedoBrighten}");
        Assert.True(a.AlbedoBrighten <= 0.09,
            $"ridge brighten must stay subtle (<= 0.16), got {a.AlbedoBrighten}");
        Assert.Equal(1.0, a.AlbedoScale);
        Assert.Equal(0.0, a.VolcanicEmission);
    }

    [Fact]
    public void Unknown_kind_is_neutral()
    {
        // Forward-compat: a feature kind the mapper doesn't know about is a no-op, not a crash.
        var a = CrustAccentMapper.Map(99, magnitude: 1.0);
        Assert.Equal(1.0, a.AlbedoScale);
        Assert.Equal(0.0, a.AlbedoBrighten);
        Assert.Equal(0.0, a.VolcanicEmission);
    }

    [Fact]
    public void Apply_neutral_accent_returns_base_unchanged()
    {
        var base_ = new RampColor(0.30, 0.50, 0.20);
        var result = CrustAccentMapper.Apply(base_, CrustAccent.Neutral);
        Assert.Equal(base_, result);
    }

    [Fact]
    public void Apply_trench_darkens_and_ridge_brightens()
    {
        var base_ = new RampColor(0.40, 0.40, 0.40);

        var trench = CrustAccentMapper.Apply(base_, CrustAccentMapper.Map(Trench, 1.0));
        Assert.True(trench.R < base_.R && trench.G < base_.G && trench.B < base_.B,
            "trench must darken all channels");

        var ridge = CrustAccentMapper.Apply(base_, CrustAccentMapper.Map(Ridge, 1.0));
        Assert.True(ridge.R > base_.R && ridge.G > base_.G && ridge.B > base_.B,
            "ridge must brighten all channels");
    }

    [Fact]
    public void Band_contrast_is_legible_face_on()
    {
        // The core face-on problem: boundary-profile bands (trench/ridge/mountain, 2-4 cells wide)
        // vanish when there is no relief silhouette because the hypsometric base is nearly identical
        // across the band. The accent must produce a minimum luminance delta against the SAME base
        // tint (zero relief difference) so the band reads face-on. Trench darkens; ridge/mountain
        // brighten — each by at least this delta.
        const double MinFaceOnDelta = 0.035;
        var base_ = new RampColor(0.30, 0.28, 0.24); // a warm mid-ramp bare-crust tone
        var baseLuma = Luma(base_);

        var trench = CrustAccentMapper.Apply(base_, CrustAccentMapper.Map(Trench, 1.0));
        Assert.True(baseLuma - Luma(trench) >= MinFaceOnDelta,
            $"trench face-on contrast too small: delta {baseLuma - Luma(trench):F3} < {MinFaceOnDelta}");

        var ridge = CrustAccentMapper.Apply(base_, CrustAccentMapper.Map(Ridge, 1.0));
        Assert.True(Luma(ridge) - baseLuma >= MinFaceOnDelta,
            $"ridge face-on contrast too small: delta {Luma(ridge) - baseLuma:F3} < {MinFaceOnDelta}");

        var mountain = CrustAccentMapper.Apply(base_, CrustAccentMapper.Map(Mountain, 1.0));
        Assert.True(Luma(mountain) - baseLuma >= MinFaceOnDelta,
            $"mountain face-on contrast too small: delta {Luma(mountain) - baseLuma:F3} < {MinFaceOnDelta}");
    }

    [Fact]
    public void Bright_accents_do_not_wash_pale_rock_to_white()
    {
        var paleRock = new RampColor(0.70, 0.69, 0.66);

        var ridge = CrustAccentMapper.Apply(paleRock, CrustAccentMapper.Map(Ridge, 1.0));
        var mountain = CrustAccentMapper.Apply(paleRock, CrustAccentMapper.Map(Mountain, 1.0));

        Assert.True(Luma(ridge) <= 0.73,
            $"ridge accent washed pale rock too close to white: luma={Luma(ridge):F3}");
        Assert.True(Luma(mountain) <= 0.73,
            $"mountain accent washed pale rock too close to white: luma={Luma(mountain):F3}");
    }

    private static double Luma(RampColor c) => 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
}
