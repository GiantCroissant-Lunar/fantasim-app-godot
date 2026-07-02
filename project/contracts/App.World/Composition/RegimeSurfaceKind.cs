namespace FantaSim.App.World.Composition;

/// <summary>
/// The surface PRESENTATION kind for a <see cref="SphereRegime"/>: which material/shader the planet
/// mantle uses while the regime is current. Pure (no Godot dependency) so the regime -&gt; kind mapping
/// is unit-testable; the host (<c>PlanetPresentationBinder</c>) maps each kind to a concrete Godot
/// material. Mirrors the presentation-hint pattern already on <see cref="SphereRegime"/>
/// (<see cref="SphereRegime.ShowsPlateFeatures"/>, <see cref="SphereRegime.DefaultColorByField"/>).
/// </summary>
public enum RegimeSurfaceKind
{
    /// <summary>Unknown / fallback regime: a neutral dark base sphere.</summary>
    Default,

    /// <summary>magma-ocean: emissive molten-lava look with slow animated noise churn.</summary>
    MagmaOcean,

    /// <summary>stagnant-lid: dark basaltic cooled crust with subtle noise-modulated albedo.</summary>
    StagnantLid,

    /// <summary>mobile-plate: plain dark base under the per-plate coloured surface (plates own the look).</summary>
    MobilePlate,
}

/// <summary>
/// Pure regime-id -&gt; <see cref="RegimeSurfaceKind"/> resolver. Ordinal, case-sensitive: regime ids are
/// stable lowercase strings (e.g. "magma-ocean", "stagnant-lid", "mobile-plate"). A null or unknown id
/// falls back to <see cref="RegimeSurfaceKind.Default"/> so the host always has a material to render.
/// </summary>
public static class RegimeSurfaceResolver
{
    /// <summary>Returns the surface kind for <paramref name="regimeId"/>, or <c>Default</c> if null/unknown.</summary>
    public static RegimeSurfaceKind Resolve(string? regimeId)
        => regimeId switch
        {
            "magma-ocean" => RegimeSurfaceKind.MagmaOcean,
            "stagnant-lid" => RegimeSurfaceKind.StagnantLid,
            "mobile-plate" => RegimeSurfaceKind.MobilePlate,
            _ => RegimeSurfaceKind.Default,
        };
}
