using FantaSim.App.World.Composition;
using FantaSim.App.World.Globe;
using FantaSim.Cartography.Globe;

namespace FantaSim.App.Presentation;

internal static class PlateSurfaceReliefFabric
{
    private const double CrustDiagnosticInteriorAmplitudeMultiplier = 0.15;
    private const double WorldInteriorAmplitudeMultiplier = 0.15;

    private static readonly NoiseParams WorldPeaks = new(
        Seed: 1337,
        BaseFrequency: 8.0,
        Octaves: 6,
        Lacunarity: 2.0,
        Gain: 0.5,
        Amplitude: 172.0,
        Ridged: false);

    private static readonly NoiseParams CrustDiagnosticPeaks = new(
        Seed: 1337,
        BaseFrequency: 5.5,
        Octaves: 4,
        Lacunarity: 2.0,
        Gain: 0.4,
        Amplitude: 172.0,
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
