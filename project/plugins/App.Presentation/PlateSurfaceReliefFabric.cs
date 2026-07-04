using FantaSim.App.World.Composition;
using FantaSim.App.World.Globe;
using FantaSim.Cartography.Globe;

namespace FantaSim.App.Presentation;

internal static class PlateSurfaceReliefFabric
{
    private const double CrustDiagnosticInteriorAmplitudeMultiplier = 0.85;

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
    // amplified rocky shell. NoiseRelief's typical values are far below its amplitude bound, so the
    // presentation binder pairs this with a higher interior multiplier: broad crust reads rough while
    // active tectonic ridges stay below the blade-like spike range.
    private static readonly NoiseParams CrustDiagnosticPeaks = new(
        Seed: 1337,
        BaseFrequency: 20.0,
        Octaves: 6,
        Lacunarity: 2.0,
        Gain: 0.5,
        Amplitude: 10_000.0,
        Ridged: false);

    private static readonly NoiseParams FlatIdentity = new(Amplitude: 0.0);

    public static NoiseParams ForView(GlobeViewMode viewMode)
        => viewMode switch
        {
            GlobeViewMode.World => WorldPeaks,
            GlobeViewMode.HypsometricTerrain => CrustDiagnosticPeaks,
            GlobeViewMode.PlateIdentity => FlatIdentity,
            _ => GlobePlateSurfaces.DefaultPeaks,
        };

    public static double InteriorAmplitudeMultiplierForView(GlobeViewMode viewMode)
        => viewMode == GlobeViewMode.HypsometricTerrain
            ? CrustDiagnosticInteriorAmplitudeMultiplier
            : TectonicDetailSampler.DefaultInteriorAmplitudeMultiplier;

    public static bool RidgeActiveFeaturesForView(GlobeViewMode viewMode)
        => viewMode != GlobeViewMode.HypsometricTerrain;
}
