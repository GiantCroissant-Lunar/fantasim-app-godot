using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ServiceArchi.Contracts;

namespace FantaSim.App.Common;

/// <summary>
/// Resident app-root composition. This is the root parent for dynamic scene scopes.
/// </summary>
public sealed class AppComposition : IDisposable
{
    private readonly ServiceProvider _rootServices;

    private AppComposition(ServiceProvider rootServices, Bootstrap bootstrap)
    {
        _rootServices = rootServices;
        Bootstrap = bootstrap;
    }

    public Bootstrap Bootstrap { get; }

    public IServiceProvider RootServices => _rootServices;

    public static AppComposition Activate(ILoggerFactory? loggerFactory = null)
    {
        var bootstrap = new Bootstrap(loggerFactory);
        var services = new ServiceCollection();

        services.AddSingleton(bootstrap);
        services.AddSingleton<IRegistry>(bootstrap.Registry);
        services.AddSingleton(bootstrap.LoggerFactory);

        return new AppComposition(services.BuildServiceProvider(), bootstrap);
    }

    public void Dispose()
    {
        Bootstrap.StopAsync().GetAwaiter().GetResult();
        _rootServices.Dispose();
    }
}
