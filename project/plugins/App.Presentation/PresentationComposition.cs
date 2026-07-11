using System;
using FantaSim.App.Presentation.Tunnel;
using FantaSim.App.Resource.Bundle;
using Godot;
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
    /// Tunnel timeline two-ring presentation. The planetBodyProvider resolves the real PlanetBody
    /// node at execution time (not capture time) so the tunnel mount aligns its local throat to
    /// the current globe through whatever presentation root is live after a rebind.
    /// </summary>
    public static ITunnelPresentation CreateTunnelPresentation(
        IRegistry registry,
        IBundleSceneRegistry sceneRegistry,
        ILoggerFactory loggerFactory,
        Func<Node3D?> planetBodyProvider)
        => new TunnelPresentationBinder(registry, sceneRegistry, loggerFactory, planetBodyProvider);
}
