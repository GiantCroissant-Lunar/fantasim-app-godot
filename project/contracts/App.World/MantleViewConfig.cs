namespace FantaSim.App.World;

// Mantle x-ray view (M-A): sampling + isosurface configuration for the VOLUMETRIC anomaly field
// (engine MantleAnomalyField — a true T'(direction, radius, tick), NOT the radially-constant v1
// basal layer). Pure data so the T1 contract surface (IService) and the resident seam can plumb a
// tuned profile without referencing the engine package.
//
// Resolution arithmetic: the grid is a Cartesian cube [-OuterRadius, OuterRadius]^3 at
// GridResolution points per axis. 56^3 = ~176k lattice samples, of which ~43% fall inside the
// mantle shell (~76k field evaluations) — interactive for a windowed toggle refresh. The engine
// summary's production recommendation (~0.015R steps) is finer; raise GridResolution for stills.

/// <summary>
/// Deterministic configuration for mantle field sampling + four-threshold isosurface extraction
/// (translucent outer + opaque inner per polarity, per the method-lock).
/// </summary>
public sealed record MantleViewConfig
{
    /// <summary>Per-axis resolution of the Cartesian sampling grid. 56^3 keeps a refresh interactive
    /// (~176k lattice samples); raise for higher-fidelity stills.</summary>
    public int GridResolution { get; init; } = 56;

    /// <summary>Inner radius of the sampled shell as a fraction of the unit sphere — the engine
    /// field's CMB radius (0.55).</summary>
    public double InnerRadius { get; init; } = 0.55;

    /// <summary>Outer radius of the sampled shell, just under the crust so isosurfaces never poke
    /// through the ghosted plate surface.</summary>
    public double OuterRadius { get; init; } = 0.98;

    /// <summary>Radial fade band (radius-fractions) over which sampled values taper to 0 at both
    /// shell boundaries, so marching cubes closes every isosurface INSIDE the shell instead of
    /// leaving open cuts at the sampling boundary.</summary>
    public double ShellFadeWidth { get; init; } = 0.03;

    /// <summary>Cold translucent OUTER isovalue (applied to the negated anomaly). Low: the wide
    /// thermal halo of a slab. Aged slabs at the mobile-plate playhead peak near ~0.55.</summary>
    public double ColdOuterThreshold { get; init; } = 0.15;

    /// <summary>Cold opaque INNER isovalue — the slab core.</summary>
    public double ColdInnerThreshold { get; init; } = 0.35;

    /// <summary>Warm translucent OUTER isovalue. The engine blanket peaks near ~0.9, plumes ~1.1.</summary>
    public double WarmOuterThreshold { get; init; } = 0.25;

    /// <summary>Warm opaque INNER isovalue — plume cores and the hottest blanket piles.</summary>
    public double WarmInnerThreshold { get; init; } = 0.55;

    public static MantleViewConfig Default { get; } = new();
}
