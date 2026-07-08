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
}
