using FantaSim.Cartography.Globe;

namespace FantaSim.App.World.Globe;

/// <summary>
/// Declared nonuniform LOD profile (directive 4, slice 1). Drives the existing
/// <see cref="AdaptiveSubdivisionOptions"/> threshold mechanism with a COARSE CAUSAL CONTEXT
/// criterion (CellFeatures: boundary proximity/kind/weight), NOT camera and NOT query history.
/// The mesh for (tick, seed, params, R-budget) is identical regardless of navigation.
///
/// <para>
/// The win is REDISTRIBUTION, not growth: boundary-adjacent density goes UP, interior density goes
/// DOWN at equal-or-lower total cost, under a declared triangle budget. This ends the "machinery
/// exists but renders uniform" failure (vault/specs/2026-07-16 §4b).
/// </para>
/// </summary>
public static class VisibleLodProfile
{
    /// <summary>
    /// The declared total triangle budget for the whole globe at frequency 3 (1280 cells).
    /// The adaptive builder must not exceed this. The win is redistribution, not growth.
    /// </summary>
    public const int DeclaredBudgetFrequency3 = 20_000;

    /// <summary>
    /// Declared total triangle budget for frequency 4 (5120 cells).
    /// </summary>
    public const int DeclaredBudgetFrequency4 = 80_000;

    /// <summary>
    /// The minimum boundary-to-interior density ratio required for the slice to PASS.
    /// </summary>
    public const double RequiredDensityRatio = 3.0;

    /// <summary>
    /// Builds the <see cref="AdaptiveSubdivisionOptions"/> for truth-driven nonuniform refinement.
    /// Height-based splits are effectively disabled (threshold set to a very large finite value)
    /// so that ONLY the coarse causal context criterion (feature weight delta) drives subdivision.
    /// This ensures interiors stay coarse and boundaries get refined, producing measurable
    /// nonuniformity. The mesh is a pure function of (tick, seed, params, R-budget) — no camera,
    /// no query history.
    /// </summary>
    /// <param name="maxDepth">
    /// Maximum subdivision depth. Higher = more boundary detail. 3 produces a visible 3x+ ratio
    /// at frequency 3 without exceeding the budget.</param>
    /// <param name="featureWeightDeltaThreshold">
    /// The threshold for feature-weight-driven splits. With MEAN gather, a boundary-adjacent vertex
    /// (1 boundary face among ~6) gets weight ~0.167; its midpoint with an interior vertex (0.0)
    /// gets ~0.083. Threshold 0.12 allows one level of splitting at boundary-adjacent edges
    /// (0.167 >= 0.12) but stops before the midpoint propagates further (0.083 < 0.12). Pure
    /// boundary vertices (weight 1.0) split at level 0 (midpoint 0.5) and level 1 (midpoint 0.25),
    /// giving 4 triangles per pure-boundary cell while interior stays at 1.</param>
    /// <param name="radius">Base unit-sphere radius.</param>
    public static AdaptiveSubdivisionOptions BuildOptions(
        int maxDepth = 2,
        double featureWeightDeltaThreshold = 0.12,
        double radius = 1.0)
    {
        // EdgeHeightDeltaThreshold set very high (but finite, per validation) so height NEVER
        // triggers splits. Only the feature-weight criterion drives subdivision. This is the
        // key difference from the production config (threshold 0.02) which splits almost every
        // edge because most adjacent cells have different elevations.
        const double heightSplitDisabled = 1e15;

        return new AdaptiveSubdivisionOptions(
            MaxDepth: maxDepth,
            EdgeHeightDeltaThreshold: heightSplitDisabled,
            Radius: radius,
            FeatureWeightDeltaThreshold: featureWeightDeltaThreshold);
    }

    /// <summary>
    /// The declared total triangle budget for a given tessellation frequency.
    /// </summary>
    public static int DeclaredBudget(int frequency) => frequency switch
    {
        3 => DeclaredBudgetFrequency3,
        4 => DeclaredBudgetFrequency4,
        _ => frequency * frequency * 5000, // proportional to cell count (~20 * freq^2 * 20)
    };
}