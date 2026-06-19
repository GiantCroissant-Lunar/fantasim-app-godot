using Akka;
using Akka.Actor;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using CrosscutFoundation.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
        LoggerFactory = loggerFactory ?? NullLoggerFactory.Instance;

        _registry.Register(
            LoggerFactory,
            new ServiceRegistration { Tags = new[] { "logging" }, Description = "Resident ILoggerFactory" });
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
                exactMatches: Array.Empty<string>(),
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
                    "Akka",
                    "Newtonsoft.Json",
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
