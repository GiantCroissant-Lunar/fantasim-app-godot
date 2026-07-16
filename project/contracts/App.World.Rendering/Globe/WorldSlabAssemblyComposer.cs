using System.Collections.Generic;
using FantaSim.Cartography.Globe.Core;

namespace FantaSim.App.World.Globe;

/// <summary>
/// Pure, Godot-free composer for the DEFAULT World view's slab assembly (assembled-world slice 1):
/// one closed watertight <see cref="PlateSolid"/> per plate from the relief-applied top caps
/// (the SAME <see cref="PlateSolidBuilder"/> machinery the mantle-layer and exploded views use),
/// translated by the declared JOINT GAP along each plate's area-weighted centroid direction — the
/// EXISTING separation math (<see cref="PlateSolidBuilder.ApplyExplodedFactor"/>) at joint scale
/// instead of explode scale.
/// </summary>
/// <remarks>
/// <para>The gap is a pure per-plate translation: topology unchanged, the slab undeformed, and
/// adjacent slabs' formerly-coincident boundary vertices open by <c>gap × |dirA − dirB|</c>. Two
/// assemblies from identical inputs are bit-identical (everything downstream of the deterministic
/// builder + transform).</para>
/// <para>The joint gap must be positive and finite — the spec requires a VISIBLE joint. A seamless
/// sphere is the <see cref="WorldSurfacePresentation.WatertightSphere"/> presentation (the old
/// single-surface path), never a zero gap smuggled through the slab path.</para>
/// </remarks>
public static class WorldSlabAssemblyComposer
{
    /// <summary>
    /// Builds the assembled World slabs: <see cref="PlateSolidBuilder.Build"/> over the caps +
    /// thickness field, then the joint-gap translation via
    /// <see cref="PlateSolidBuilder.ApplyExplodedFactor"/> with <c>factor = 1</c> and
    /// <c>maxOffset = SlabJointGapUnitRadius</c>.
    /// </summary>
    /// <param name="caps">Per-plate relief-applied top caps (e.g. from <see cref="SlabTopReliefComposer.BuildCaps"/> — slab-declared relief, no World silhouette clamp).</param>
    /// <param name="centroids">Per-plate centroid directions (from <see cref="PlateSolidBuilder.ComputeCentroids"/>), indexed by plate id.</param>
    /// <param name="crustThicknessByCellMetres">Per-cell crust THICKNESS in metres, indexed by cell id.</param>
    /// <param name="thicknessDepthScale">Metres-to-unit-radius thickness depth scale (D3's <c>RadialSectionProfile.ThicknessDepthScale()</c>).</param>
    /// <param name="profile">The declared World-surface presentation profile (owns the joint gap).</param>
    /// <param name="baseRadius">The unit-sphere base radius (default 1.0, matching <see cref="GlobeSurfaceBuilder.DefaultRadius"/>).</param>
    /// <returns>One gapped <see cref="PlateSolid"/> per input cap, in the SAME order as <paramref name="caps"/>.</returns>
    public static IReadOnlyList<PlateSolid> BuildAssembly(
        IReadOnlyList<PlateCap> caps,
        IReadOnlyList<PlateSolidCentroid> centroids,
        IReadOnlyList<double> crustThicknessByCellMetres,
        double thicknessDepthScale,
        WorldSurfacePresentationProfile profile,
        double baseRadius = GlobeSurfaceBuilder.DefaultRadius)
    {
        ArgumentNullException.ThrowIfNull(caps);
        ArgumentNullException.ThrowIfNull(centroids);
        ArgumentNullException.ThrowIfNull(crustThicknessByCellMetres);
        ArgumentNullException.ThrowIfNull(profile);

        double gap = profile.SlabJointGapUnitRadius;
        if (double.IsNaN(gap) || double.IsInfinity(gap) || gap <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(profile),
                gap,
                "The slab joint gap must be positive and finite — the assembled world requires a visible joint. "
                + "Use the WatertightSphere presentation for a seamless sphere.");
        }

        var assembled = PlateSolidBuilder.Build(caps, crustThicknessByCellMetres, thicknessDepthScale, baseRadius);
        return PlateSolidBuilder.ApplyExplodedFactor(assembled, centroids, factor: 1.0, maxOffset: gap);
    }
}
