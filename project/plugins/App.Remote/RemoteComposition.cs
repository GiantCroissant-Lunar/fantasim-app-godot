using FantaSim.App.Command;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceArchi.Contracts;

namespace FantaSim.App.Remote;

public static class RemoteComposition
{
    public static void Compose(IRegistry registry, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(registry);
        loggerFactory ??= NullLoggerFactory.Instance;

        var options = RemoteOptions.FromEnvironment();
        if (!options.Enabled)
            return;

        var logger = loggerFactory.CreateLogger("App.Remote.Composition");
        try
        {
            var client = registry.Get<IClient>();
            var dispatcher = registry.Get<IMainThreadDispatcher>();
            var transport = new HttpTransport(options, client, dispatcher, loggerFactory);

            registry.Register<ITransport>(
                transport,
                new ServiceRegistration { Tags = new[] { "transport" }, Description = "Remote HTTP command transport" });

            transport.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Remote composition failed.");
        }
    }
}
