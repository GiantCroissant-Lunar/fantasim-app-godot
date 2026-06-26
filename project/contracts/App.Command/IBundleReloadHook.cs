namespace FantaSim.App.Command;

public interface IBundleReloadHook
{
    Task AfterReloadAsync(string bundleId, CancellationToken cancellationToken = default);
}
