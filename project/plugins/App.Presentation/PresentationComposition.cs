using FantaSim.App.Presentation.Tunnel;
using FantaSim.App.Resource.Bundle;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;
using ResourceService = FantaSim.App.Resource.IService;

namespace FantaSim.App.Presentation;

/// <summary>
/// Composition entry for the presentation plugin. Bundle-maximalism phase 1: called by the world
/// bundle's PresentationPlugin (same collectible ALC); the resident host consumes only the
/// IPlanetPresentation contract (contracts/App.Presentation).
/// </summary>
public static class PresentationComposition
{
    public static IPlanetPresentation CreatePlanetPresentation(
        IRegistry registry,
        ResourceService resource,
        IBundleSceneRegistry sceneRegistry,
        ILoggerFactory loggerFactory,
        string? plateViewOverride = null,
        bool showWorldGraph = false)
        => new PlanetPresentationBinder(registry, resource, sceneRegistry, loggerFactory, plateViewOverride, showWorldGraph);

    /// <summary>
    /// Tunnel timeline presentation (slice 1, vault/plans/2026-07-11-tunnel-slice1-plan.md Task 7).
    /// No ResourceService parameter: unlike PlanetPresentationBinder, TunnelPresentationBinder
    /// resolves it itself from the registry (it has no plateViewOverride/showWorldGraph knobs to
    /// justify a wider composition surface for slice 1).
    /// </summary>
    public static ITunnelPresentation CreateTunnelPresentation(
        IRegistry registry,
        IBundleSceneRegistry sceneRegistry,
        ILoggerFactory loggerFactory)
        => new TunnelPresentationBinder(registry, sceneRegistry, loggerFactory);
}
