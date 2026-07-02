using Akka;
using Akka.Actor;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using CrosscutFoundation.Config;
using CrosscutFoundation.Logging;
using CrosscutFoundation.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PluginArchi.Extensibility.Abstractions;
using PluginArchi.Extensibility.Hosting;
using ServiceArchi.Contracts;
using ServiceArchi.Core;

namespace FantaSim.App.Common;

public sealed class Bootstrap
{
    private readonly IRegistry _registry;
    private readonly ILogger _log;
    private IPluginHost? _pluginHost;
    private ActorSystem? _actorSystem;

    public Bootstrap(ILoggerFactory? loggerFactory = null)
    {
        _registry = new ServiceRegistry();

        // Structured console logging via crosscut-foundation: register the console configurator,
        // compose the LoggingService (an ILoggerFactory) from all registered configurators, and alias
        // it under ILoggerFactory so every ILogger — app code AND collectible bundles across the ALC
        // boundary ("CrosscutFoundation." is a shared-resident prefix) — reaches the console.
        if (loggerFactory is null)
        {
            _registry.RegisterConsoleLogging();
            _registry.RegisterLoggingService();
            LoggerFactory = (ILoggerFactory)_registry.Get<CrosscutFoundation.Logging.IService>();
        }
        else
        {
            LoggerFactory = loggerFactory;
        }

        _registry.Register<ILoggerFactory>(
            LoggerFactory,
            new ServiceRegistration { Tags = new[] { "logging" }, Description = "ILoggerFactory (crosscut console LoggingService)" });

        // Layered app config: JSON base (priority 50) + environment variable override (priority 90),
        // surfaced as CrosscutFoundation.Config.IService and re-registered under that interface with
        // the "config" tag so the rest of the app resolves it from the registry.
        _registry.RegisterJsonConfig(
            Path.Combine(AppContext.BaseDirectory, "config", "app.json"),
            optional: true,
            reloadOnChange: true,
            priority: 50);
        _registry.RegisterEnvConfig(priority: 90);
        _registry.RegisterConfigService();
        var configService = (CrosscutFoundation.Config.IService)_registry.Get<CrosscutFoundation.Config.IService>();
        _registry.Register<CrosscutFoundation.Config.IService>(
            configService,
            new ServiceRegistration { Tags = new[] { "config" }, Description = "Crosscut config (json + env)" });

        _registry.RegisterMessagePipeMessageBus();

        _actorSystem = ActorSystem.Create("fantasim", @"
            akka {
                loglevel = INFO
                actor {
                    debug.receive = off
                }
            }");
        _registry.Register(
            _actorSystem,
            new ServiceRegistration { Tags = new[] { "akka", "actor-system" }, Description = "Shared Akka ActorSystem" });

        _log = LoggerFactory.CreateLogger("FantaSim.App.Common.Bootstrap");
        _log.LogInformation(
            "Resident app kernel registry #{Kernel} initialized with messaging={Messaging}, akka={Akka}.",
            RuntimeHelpers.GetHashCode(_registry),
            _registry.Get<IMessageBus>().GetType().Name,
            _actorSystem.Name);
    }

    public IRegistry Registry => _registry;

    public ILoggerFactory LoggerFactory { get; }

    public ActorSystem ActorSystem =>
        _actorSystem ?? throw new InvalidOperationException("Bootstrap has been stopped.");

    public IPluginHost PluginHost =>
        _pluginHost ?? throw new InvalidOperationException("BuildPluginHost has not been called.");

    public void BuildPluginHost(CollectibleBundles collectibleBundles)
    {
        ArgumentNullException.ThrowIfNull(collectibleBundles);

        if (_pluginHost is not null)
            throw new InvalidOperationException("BuildPluginHost has already been called.");

        var hostContext = AssemblyLoadContext.GetLoadContext(typeof(Bootstrap).Assembly)
            ?? AssemblyLoadContext.Default;

        _pluginHost = new PluginHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(_registry);
                services.AddSingleton(LoggerFactory);
            })
            .WithParentContext(hostContext)
            .WithSharedPolicy(new SharedAssemblyPolicy(
                exactMatches: new[]
                {
                    // Each name is shared WITH resident code; the tag is the forcing resident consumer.
                    "FantaSim.World.Fields.Contracts",   // App.Ecs
                    "FantaSim.World.Fields.Core",         // App.Ecs
                    "FantaSim.World.Shared.Contracts",   // App.Timeline
                    "UnifyMaths",                         // contracts/App.World; keeps .Numerics collectible
                    "UnifyStorage.Abstractions",          // App.Activity
                    "UnifyStorage.Runtime.LiteDb",        // App.Activity
                    "Arch",                               // transitive of shared UnifyEcs.Runtime.Arch
                    "MessagePack",                        // transitive of shared CrosscutFoundation.Persistence.Contracts:
                    "MessagePack.Annotations",            //   its CanonicalMessagePackOptions API exposes MessagePack types,
                                                          //   so a bundle-local MessagePack.dll splits type identity (MissingMethodException)
                },
                prefixes: new[]
                {
                    "System.",
                    "Microsoft.",
                    "Godot",
                    "GodotSharp",
                    "netstandard",
                    "PluginArchi.",
                    "ServiceArchi.",
                    "RegistryArchi.",
                    "DependencyArchi.",
                    "CrosscutFoundation.",
                    "MessagePipe",
                    "BoomHud",
                    "R3",
                    "ReactiveUI",
                    "DynamicData",
                    "FantaSim.App.",
                    "FantaSim.App.World.",
                    "FantaSim.App.Command.",
                    "Akka",
                    "Newtonsoft.Json",
                    "UnifyEcs.",   // App.Ecs
                    "TimeDete.",   // transitive via Timeline/Ecs
                },
                excludedExactMatches: collectibleBundles.AssemblyNames.ToArray()))
            .Build();

        _registry.Register<IPluginHost>(
            _pluginHost,
            new ServiceRegistration { Tags = new[] { "plugins", "plugin-archi" }, Description = "PluginArchi host" });

        _log.LogInformation(
            "Plugin host built with {Count} collectible assembly exclusion(s).",
            collectibleBundles.AssemblyNames.Count);
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (_pluginHost is null)
            throw new InvalidOperationException("BuildPluginHost has not been called.");

        try
        {
            await _pluginHost.InitializeAsync(cancellationToken).ConfigureAwait(false);
            _log.LogInformation("Plugin host initialized.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _log.LogWarning("Plugin host initialization canceled.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Plugin host initialization failed.");
        }
    }

    public async Task StopAsync()
    {
        if (_pluginHost is not null)
        {
            try
            {
                await _pluginHost.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                _pluginHost = null;
            }
        }

        if (_actorSystem is not null)
        {
            await _actorSystem.Terminate().ConfigureAwait(false);
            _actorSystem = null;
        }
    }
}
