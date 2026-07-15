using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.Command;
using FantaSim.App.World;
using FantaSim.App.World.Composition;
using FantaSim.Mythosphere.Cosmology.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PluginArchi.Extensibility.Abstractions;
using ServiceArchi.Contracts;
using ServiceArchi.Core;
using Xunit;
using CommandService = FantaSim.App.Command.IService;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Ownership proof for the layer-track registry (refactor round 2, 2026-07-10): the WORLD
/// bundle's WorldPlugin -- not the timeline bundle -- composes, registers, and tears down the
/// <see cref="ILayerTrackRegistry"/> plus the <c>registry.reload</c> command. Timeline consumes
/// the shared contract via registry lookup only (referencing the App.World.Composition plugin
/// assembly from the timeline bundle dual-copied 8 Unify assemblies across the two collectible
/// ALCs -- the type-identity-split incident class).
/// </summary>
public sealed class WorldPluginLayerTrackRegistryTests
{
    private static IRegistry NewRegistry() => new ServiceRegistry();

    private static IPluginContext NewContext(IRegistry registry)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRegistry>(registry);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        return new FakePluginContext(services.BuildServiceProvider());
    }

    [Fact]
    public async Task Initialize_RegistersLayerTrackRegistry_AndShutdownRemovesIt()
    {
        var registry = NewRegistry();
        var plugin = new WorldPlugin();

        await plugin.InitializeAsync(NewContext(registry), CancellationToken.None);
        Assert.NotNull(registry.TryGet<ILayerTrackRegistry>());

        await plugin.ShutdownAsync(CancellationToken.None);
        Assert.Null(registry.TryGet<ILayerTrackRegistry>());
    }

    [Fact]
    public async Task Initialize_RegistersReloadRegistryCommand_AndShutdownUnregistersIt()
    {
        var registry = NewRegistry();
        var commands = new RecordingCommandService();
        registry.Register<CommandService>(commands, new ServiceRegistration { Tags = new[] { "command" } });

        var plugin = new WorldPlugin();
        await plugin.InitializeAsync(NewContext(registry), CancellationToken.None);
        Assert.Contains(commands.Commands, command => command.Id == WorldPlugin.ReloadRegistryCommandId);

        await plugin.ShutdownAsync(CancellationToken.None);
        Assert.DoesNotContain(commands.Commands, command => command.Id == WorldPlugin.ReloadRegistryCommandId);
    }

    [Fact]
    public async Task ReloadRegistryCommand_ReturnsOkJsonObjectResponse()
    {
        var registry = NewRegistry();
        var commands = new RecordingCommandService();
        registry.Register<CommandService>(commands, new ServiceRegistration { Tags = new[] { "command" } });

        var plugin = new WorldPlugin();
        await plugin.InitializeAsync(NewContext(registry), CancellationToken.None);

        var handler = commands.HandlerFor(WorldPlugin.ReloadRegistryCommandId);
        var responseJson = await handler(null, CancellationToken.None);

        Assert.NotNull(responseJson);
        var response = System.Text.Json.Nodes.JsonNode.Parse(responseJson!) as System.Text.Json.Nodes.JsonObject;
        Assert.NotNull(response);
        Assert.True(response!["ok"]!.GetValue<bool>());
        Assert.NotNull(response["revision"]);
        Assert.NotNull(response["trackCount"]);

        await plugin.ShutdownAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Initialize_registers_world_declaration_registry_with_exact_records()
    {
        var registry = NewRegistry();
        var plugin = new WorldPlugin();

        await plugin.InitializeAsync(NewContext(registry), CancellationToken.None);

        var declRegistry = registry.TryGet<IWorldDeclarationRegistry>();
        Assert.NotNull(declRegistry);

        var science = declRegistry!.ResolveLawSet(LawSetReference.Latest("fantasim/science"));
        Assert.Equal("fantasim/science", science.Value.Definition.LawSetId);
        Assert.Equal("1", science.Value.Definition.Version);

        var world = declRegistry.ResolveWorld(WorldDeclarationReference.Latest("default"));
        Assert.Equal("default", world.Value.Definition.WorldName);
        Assert.Equal("1", world.Value.Definition.Version);

        await plugin.ShutdownAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Shutdown_removes_world_declaration_registry()
    {
        var registry = NewRegistry();
        var plugin = new WorldPlugin();

        await plugin.InitializeAsync(NewContext(registry), CancellationToken.None);
        Assert.NotNull(registry.TryGet<IWorldDeclarationRegistry>());

        await plugin.ShutdownAsync(CancellationToken.None);
        Assert.Null(registry.TryGet<IWorldDeclarationRegistry>());
    }

    [Fact]
    public async Task Two_activations_reproduce_same_three_digests()
    {
        var registry = NewRegistry();

        var plugin1 = new WorldPlugin();
        await plugin1.InitializeAsync(NewContext(registry), CancellationToken.None);
        var decl1 = registry.TryGet<IWorldDeclarationRegistry>()!;
        var science1 = decl1.ResolveLawSet(LawSetReference.Latest("fantasim/science"));
        var world1 = decl1.ResolveWorld(WorldDeclarationReference.Latest("default"));
        await plugin1.ShutdownAsync(CancellationToken.None);

        var plugin2 = new WorldPlugin();
        await plugin2.InitializeAsync(NewContext(registry), CancellationToken.None);
        var decl2 = registry.TryGet<IWorldDeclarationRegistry>()!;
        var science2 = decl2.ResolveLawSet(LawSetReference.Latest("fantasim/science"));
        var world2 = decl2.ResolveWorld(WorldDeclarationReference.Latest("default"));
        await plugin2.ShutdownAsync(CancellationToken.None);

        Assert.Equal(science1.Value.Digest, science2.Value.Digest);
        Assert.Equal(world1.Value.TruthDigest, world2.Value.TruthDigest);
        Assert.Equal(world1.Value.PresentationDigest, world2.Value.PresentationDigest);
    }

    [Fact]
    public async Task Failed_partial_command_registration_rolls_back_all_plugin_registrations()
    {
        var registry = NewRegistry();
        var commands = new ThrowOnSecondRegistrationCommandService();
        registry.Register<CommandService>(commands, new ServiceRegistration { Tags = new[] { "command" } });

        var plugin = new WorldPlugin();

        await Assert.ThrowsAsync<InvalidOperationException>(
        () => plugin.InitializeAsync(NewContext(registry), CancellationToken.None).AsTask());

        Assert.Null(registry.TryGet<IWorldDeclarationRegistry>());
        Assert.Null(registry.TryGet<ILayerTrackRegistry>());
        Assert.Null(registry.TryGet<FantaSim.App.World.IService>());
        Assert.Empty(commands.Commands);
    }

    private sealed class FakePluginContext : IPluginContext
    {
        public FakePluginContext(IServiceProvider services) => Services = services;
        public IServiceProvider Services { get; }
    }

    private sealed class RecordingCommandService : CommandService
    {
        private readonly Dictionary<string, (CommandDescriptor Descriptor, CommandHandler Handler)> _handlers = new(StringComparer.Ordinal);

        public IReadOnlyList<CommandDescriptor> Commands => _handlers.Values.Select(entry => entry.Descriptor).ToArray();

        public CommandHandler HandlerFor(string commandId) => _handlers[commandId].Handler;

        public void Register(CommandDescriptor descriptor, CommandHandler handler)
            => _handlers[descriptor.Id] = (descriptor, handler);

        public void Unregister(string commandId)
            => _handlers.Remove(commandId);

        public Task<CommandResult> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class ThrowOnSecondRegistrationCommandService : CommandService
    {
        private readonly Dictionary<string, CommandDescriptor> _commands = new(StringComparer.Ordinal);
        private int _registrationCount;

        public IReadOnlyList<CommandDescriptor> Commands => _commands.Values.ToArray();

        public void Register(CommandDescriptor descriptor, CommandHandler handler)
        {
            _registrationCount++;
            if (_registrationCount == 2)
                throw new InvalidOperationException("Test-induced partial command-registration failure.");
            _commands.Add(descriptor.Id, descriptor);
        }

        public void Unregister(string commandId) => _commands.Remove(commandId);

        public Task<CommandResult> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
