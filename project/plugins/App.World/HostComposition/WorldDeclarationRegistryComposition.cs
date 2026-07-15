using FantaSim.App.World.GenerationGraph;
using FantaSim.Mythosphere.Cosmology.Contracts;
using FantaSim.Mythosphere.Cosmology.Registry;
using FantaSim.Mythosphere.Cosmology.Science;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;

namespace FantaSim.App.World;

public sealed record WorldDeclarationRegistryResult(
    IWorldDeclarationRegistry Registry,
    IDisposable RegistrationHandle,
    LawSet ScienceLawSet,
    WorldDeclaration DefaultWorld);

public static class WorldDeclarationRegistryComposition
{
    public static WorldDeclarationRegistryResult Compose(
        IRegistry serviceRegistry,
        ILoggerFactory loggerFactory)
    {
        var log = loggerFactory.CreateLogger("HostComposition.WorldDeclarationRegistry");

        var registry = new InMemoryWorldDeclarationRegistry();
        var science = FantaSimScienceLawSetAuthoring.AppendTo(registry);
        var world = DefaultWorldDeclarationAuthoring.AppendTo(registry, science);

        registry.ResolveLawSet(LawSetReference.Exact(science.Definition.LawSetId, science.Digest));
        registry.ResolveWorld(WorldDeclarationReference.Exact(
            world.Definition.WorldName, world.TruthDigest, world.PresentationDigest));

        var handle = serviceRegistry.RegisterOwned<IWorldDeclarationRegistry>(
            registry,
            new ServiceRegistration
            {
                OwnerId = "app.world",
                Tags = new[] { "world", "world-declaration-registry" },
                Description = "World declaration registry (world bundle, G-010 plugin-owned)"
            });

        log.LogInformation(
            "Declaration registry composed: science={ScienceDigest}, truth={TruthDigest}, presentation={PresentationDigest}",
            science.Digest.Value,
            world.TruthDigest.Value,
            world.PresentationDigest.Value);

        return new WorldDeclarationRegistryResult(registry, handle, science, world);
    }
}
