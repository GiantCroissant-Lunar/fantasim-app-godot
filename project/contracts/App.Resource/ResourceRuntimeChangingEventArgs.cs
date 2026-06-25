namespace FantaSim.App.Resource;

public enum ResourceRuntimeOperation
{
    Unload,
    Reload,
    UnloadAll,
}

public sealed class ResourceRuntimeChangingEventArgs : EventArgs
{
    public ResourceRuntimeChangingEventArgs(string bundleId, ResourceRuntimeOperation operation)
    {
        if (string.IsNullOrWhiteSpace(bundleId))
            throw new ArgumentException("Bundle id is required.", nameof(bundleId));

        BundleId = bundleId;
        Operation = operation;
    }

    public string BundleId { get; }

    public ResourceRuntimeOperation Operation { get; }
}
