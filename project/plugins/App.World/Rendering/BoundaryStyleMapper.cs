using FantaSim.App.World;

namespace FantaSim.App.World.Rendering;

/// <summary>
/// Render doctrine for one typed plate boundary (sub-project P3). Godot-free: all values are plain
/// doubles / booleans so the mapping is unit-testable and reusable by future timeline lanes that need
/// the same doctrine colors. The host seam lifts these into concrete Godot material/mesh parameters.
/// </summary>
/// <param name="Color">High-contrast doctrine color (RampColor, [0,1] per channel).</param>
/// <param name="EmissionEnergy">Emission energy multiplier for the boundary material.</param>
/// <param name="RibbonHalfWidth">Half-width of the ribbon quad-strip on the unit sphere.</param>
/// <param name="SurfaceHeight">Radial height multiplier where the ribbon sits (above 1.0 = above caps).</param>
/// <param name="RenderOnTop">Whether the ribbon should render on top of plate caps (draw priority).</param>
public readonly record struct BoundaryStyle(
    RampColor Color,
    double EmissionEnergy,
    double RibbonHalfWidth,
    double SurfaceHeight,
    bool RenderOnTop);

/// <summary>
/// Maps a <see cref="PlateBoundaryKind"/> to its render doctrine (sub-project P3). The single shared
/// source of boundary styling so the host renderer and future timeline lanes consume the same colors.
/// Convergent / divergent / transform keep their hue family (red-orange / cyan / yellow) but with
/// raised saturation and contrast so the three types are unmistakable at a glance. Ribbons are thicker
/// and sit higher above the caps than the previous defaults to eliminate z-fighting.
/// </summary>
public static class BoundaryStyleMapper
{
    // Active boundary styles: high-contrast, saturated, thick, elevated. All share the same geometry
    // (width/height) so ribbons meeting at triple junctions do not z-fight each other.
    private static readonly BoundaryStyle Convergent = new(
        Color: new RampColor(1.00, 0.26, 0.10),  // strong red-orange
        EmissionEnergy: 0.80,
        RibbonHalfWidth: 0.020,
        SurfaceHeight: 1.022,
        RenderOnTop: true);

    private static readonly BoundaryStyle Divergent = new(
        Color: new RampColor(0.06, 0.95, 0.90),  // strong cyan
        EmissionEnergy: 0.70,
        RibbonHalfWidth: 0.020,
        SurfaceHeight: 1.022,
        RenderOnTop: true);

    private static readonly BoundaryStyle Transform = new(
        Color: new RampColor(1.00, 0.92, 0.18),  // strong yellow
        EmissionEnergy: 0.65,
        RibbonHalfWidth: 0.020,
        SurfaceHeight: 1.022,
        RenderOnTop: true);

    private static readonly BoundaryStyle InactiveStyle = new(
        Color: new RampColor(0.6, 0.6, 0.6),
        EmissionEnergy: 0.0,
        RibbonHalfWidth: 0.016,
        SurfaceHeight: 1.018,
        RenderOnTop: false);

    /// <summary>Returns the render doctrine for <paramref name="kind"/>.</summary>
    public static BoundaryStyle Resolve(PlateBoundaryKind kind) => kind switch
    {
        PlateBoundaryKind.Convergent => Convergent,
        PlateBoundaryKind.Divergent => Divergent,
        PlateBoundaryKind.Transform => Transform,
        _ => InactiveStyle,
    };
}
