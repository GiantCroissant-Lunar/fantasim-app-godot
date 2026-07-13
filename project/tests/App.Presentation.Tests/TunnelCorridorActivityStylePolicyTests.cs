using FantaSim.App.Presentation.Tunnel;
using Godot;
using Xunit;

namespace FantaSim.App.Presentation.Tests;

/// <summary>
/// Headless coverage for the corridor activity style policy. Active/inactive transitions must
/// update the wall material, header text/color, and current-plane cue color together; the policy
/// keeps the style computation in one place so build and refresh paths cannot diverge.
/// </summary>
public sealed class TunnelCorridorActivityStylePolicyTests
{
    [Fact]
    public void Active_NotFocused_WallIsActiveColor()
    {
        var style = TunnelCorridorActivityStylePolicy.Resolve(isActive: true, isFocused: false);
        Assert.Equal(new Color(0.30f, 0.55f, 0.62f), style.WallColor);
    }

    [Fact]
    public void Inactive_NotFocused_WallIsInactiveColor()
    {
        var style = TunnelCorridorActivityStylePolicy.Resolve(isActive: false, isFocused: false);
        Assert.Equal(new Color(0.42f, 0.44f, 0.46f), style.WallColor);
    }

    [Fact]
    public void Focused_OverridesActivity_WithFocusColor()
    {
        var activeFocused = TunnelCorridorActivityStylePolicy.Resolve(isActive: true, isFocused: true);
        var inactiveFocused = TunnelCorridorActivityStylePolicy.Resolve(isActive: false, isFocused: true);

        Assert.Equal(new Color(0.42f, 0.68f, 0.52f), activeFocused.WallColor);
        Assert.Equal(activeFocused.WallColor, inactiveFocused.WallColor);
    }

    [Fact]
    public void Active_TitleIsBright_SubtitleIsActiveTint()
    {
        var style = TunnelCorridorActivityStylePolicy.Resolve(isActive: true, isFocused: false);

        Assert.Equal(new Color(0.92f, 0.94f, 0.97f, 0.94f), style.TitleColor);
        Assert.Equal(new Color(0.72f, 0.86f, 0.78f, 0.90f), style.SubtitleColor);
    }

    [Fact]
    public void Inactive_TitleAndSubtitleAreDimmed()
    {
        var style = TunnelCorridorActivityStylePolicy.Resolve(isActive: false, isFocused: false);

        Assert.Equal(new Color(0.62f, 0.63f, 0.68f, 0.85f), style.TitleColor);
        Assert.Equal(new Color(0.55f, 0.56f, 0.60f, 0.80f), style.SubtitleColor);
    }

    [Fact]
    public void Focused_TitleIsWarmTint()
    {
        var style = TunnelCorridorActivityStylePolicy.Resolve(isActive: false, isFocused: true);
        Assert.Equal(new Color(1.0f, 0.98f, 0.85f, 0.98f), style.TitleColor);
    }

    [Fact]
    public void CueColor_AlwaysMatchesWallColor()
    {
        var style = TunnelCorridorActivityStylePolicy.Resolve(isActive: true, isFocused: false);
        Assert.Equal(style.WallColor, style.CueColor);
    }
}
