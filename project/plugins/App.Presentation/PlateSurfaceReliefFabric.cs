using FantaSim.App.World.Composition;
using FantaSim.App.World.Globe;
using FantaSim.Cartography.Globe;

namespace FantaSim.App.Presentation;

internal static class PlateSurfaceReliefFabric
{
    // Concentrated drama (north-star spec §3): interior fabric amplitude <= 0.15x the effective
    // belt (active) amplitude. Belts are the drama now that the silhouette is capped (§1).
    private const double CrustDiagnosticInteriorAmplitudeMultiplier = 0.15;
    private const double WorldInteriorAmplitudeMultiplier = 0.15;

    // World-view seeded peaks (W1, §5c "sub-cell detail"): base fabric, not garnish. The height lens
    // is non-linear in world view, so a high nominal amplitude produces a rocky silhouette without
    // turning every high orogenic envelope sample into a spear.
    private static readonly NoiseParams WorldPeaks = new(
        Seed: 1337,
        BaseFrequency: 8.0,
        Octaves: 6,
        Lacunarity: 2.0,
        Gain: 0.5,
        Amplitude: 17_000.0,
        Ridged: false);

    // Crust diagnostic keeps the linear height scale, but the dry-crust view is intentionally an
    // amplified rocky shell. Keep the fabric broad enough to read as crumpled crust rather than
    // high-frequency fuzz; the presentation binder pairs it with a higher interior multiplier so
    // broad crust remains visible while active tectonic ridges stay below the blade-like spike range.
    private static readonly NoiseParams CrustDiagnosticPeaks = new(
        Seed: 1337,
        BaseFrequency: 5.5,
        Octaves: 4,
        Lacunarity: 2.0,
        Gain: 0.4,
        Amplitude: 12_500.0,
        Ridged: false);

    private static readonly NoiseParams FlatIdentity = new(Amplitude: 0.0);

    public static NoiseParams ForView(GlobeViewMode viewMode)
        => viewMode switch
        {
            GlobeViewMode.World => WorldPeaks,
            GlobeViewMode.HypsometricTerrain => CrustDiagnosticPeaks,
            GlobeViewMode.PlateIdentity => FlatIdentity,
            GlobeViewMode.Continents => FlatIdentity, // M0: membership map — flat, no relief fabric
            _ => GlobePlateSurfaces.DefaultPeaks,
        };

    public static double InteriorAmplitudeMultiplierForView(GlobeViewMode viewMode)
        => viewMode == GlobeViewMode.HypsometricTerrain
            ? CrustDiagnosticInteriorAmplitudeMultiplier
            : viewMode == GlobeViewMode.World
                ? WorldInteriorAmplitudeMultiplier
                : TectonicDetailSampler.DefaultInteriorAmplitudeMultiplier;

    // Spec §3: ridging ON for active features (mountain/arc/trench/ridge) in BOTH planet views —
    // belts are thin, ridged, boundary-aligned (mountain CHAINS, not crumple).
    public static bool RidgeActiveFeaturesForView(GlobeViewMode viewMode)
        => viewMode is GlobeViewMode.World or GlobeViewMode.HypsometricTerrain;

    // Spec §3: no active damping in planet views — belts are the drama now that the silhouette is
    // capped. The 0.45 crust damping is removed; all views use the full active amplitude.
    public static double ActiveAmplitudeMultiplierForView(GlobeViewMode viewMode) => 1.0;
}
