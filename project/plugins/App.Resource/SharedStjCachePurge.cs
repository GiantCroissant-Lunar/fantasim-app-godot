using System;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace FantaSim.App.Resource;

/// <summary>
/// System.Text.Json pools <c>CachingContext</c> instances across VALUE-EQUAL
/// <see cref="System.Text.Json.JsonSerializerOptions"/> (<c>JsonSerializerOptions.TrackedCachingContexts</c>,
/// .NET 8). A bundle-local <c>static readonly JsonSerializerOptions</c> is value-equal across bundle
/// generations, so the reloaded generation adopts the pooled context that still caches the previous
/// generation's <c>JsonTypeInfo</c>/<c>RuntimeType</c> entries -&gt; old LoaderAllocator -&gt; the old
/// ALC never collects (dump-verified 2026-07-10: the world bundle's
/// <c>LayerTrackRegistryService.AssetReadOptions</c> pinned generation N-1 through exactly this chain).
///
/// The purge invokes <c>System.Text.Json.JsonSerializerOptionsUpdateHandler.ClearCache</c> -- the
/// runtime's own hot-reload hook -- which clears every live options' caching context, per-options
/// type-info fields, and the reflection-emit member-accessor cache. Safe: these are pure memoizations
/// and repopulate on the next (de)serialization. Companion to <c>SharedMessagePackCachePurge</c> in
/// App.Resource.Bundle.Seam, which evicts the equivalent MessagePack resolver cache.
/// </summary>
public static class SharedStjCachePurge
{
    private const string HandlerTypeName = "System.Text.Json.JsonSerializerOptionsUpdateHandler, System.Text.Json";
    private const string ClearMethodName = "ClearCache";

    public static void ClearReflectionCaches(string bundleId, ILogger logger)
    {
        try
        {
            var handlerType = Type.GetType(HandlerTypeName, throwOnError: false);
            var clearMethod = handlerType?.GetMethod(ClearMethodName, BindingFlags.Public | BindingFlags.Static);
            if (clearMethod is null)
            {
                logger.LogWarning(
                    "Hot-reload: STJ cache-clear hook not found ({Type}.{Method}) -- collectible ALCs may stay pinned after unload of {BundleId}.",
                    HandlerTypeName,
                    ClearMethodName,
                    bundleId);
                return;
            }

            clearMethod.Invoke(null, new object?[] { null });
            logger.LogInformation(
                "Hot-reload: cleared shared System.Text.Json reflection caches on unload of {BundleId} (pooled CachingContexts no longer root collectible types).",
                bundleId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Hot-reload: STJ reflection cache purge failed for bundle {BundleId} -- the old ALC may stay pinned.",
                bundleId);
        }
    }
}
