namespace FantaSim.App.Presentation;

/// <summary>
/// Host-supplied options for the planet presentation binder. The resident host reads the app
/// config knobs (<c>globe:plateView</c>, <c>world:showGraph</c>) and registers an instance BEFORE
/// the world bundle loads; the bundle's PresentationPlugin resolves it at bundle load, falling
/// back to <see cref="Default"/>. Lives in the contract assembly because config reads are banned
/// inside the App.Presentation seam project (SeamConfigBanTests) while the values still originate
/// from host config.
/// </summary>
public sealed record PlanetPresentationOptions(string? PlateViewOverride, bool ShowWorldGraph)
{
    public static PlanetPresentationOptions Default { get; } = new(null, false);
}
