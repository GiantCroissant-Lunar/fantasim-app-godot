using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;

namespace FantaSim.App.Resource.Bundle;

/// <summary>
/// MessagePack is deliberately shared with the collectible bundles (SharedAssemblyPolicy in the
/// host Bootstrap) so serialized types keep cross-ALC identity. The cost: MessagePack 3.x's
/// <c>SourceGeneratedFormatterResolver</c> holds a static
/// <c>ConcurrentDictionary&lt;Assembly, IFormatterResolver?&gt;</c> keyed by every assembly it
/// probed for a source-generated resolver. An entry keyed by a collectible bundle assembly roots
/// that assembly -&gt; LoaderAllocator -&gt; the whole ALC, so a reloaded bundle's old context can
/// never be collected (dump-verified 2026-07-03: gcroot showed this cache as the SINGLE root of
/// the pinned PluginGroup_world context).
///
/// Evicting every collectible-keyed entry on bundle unload is safe: the cache is a pure
/// memoization and repopulates on the next formatter lookup, and evicting live bundles' entries
/// too keeps the predicate independent of plugin-archi's context naming while self-healing any
/// previously accumulated stale generations.
/// </summary>
internal static class SharedMessagePackCachePurge
{
    private const string ResolverTypeName = "MessagePack.Resolvers.SourceGeneratedFormatterResolver, MessagePack";
    private const string CacheFieldName = "AssemblyResolverCache";

    internal static void EvictCollectibleEntries(string bundleId, ILogger logger)
    {
        try
        {
            var resolverType = Type.GetType(ResolverTypeName, throwOnError: false);
            var cacheField = resolverType?.GetField(CacheFieldName, BindingFlags.NonPublic | BindingFlags.Static);
            if (cacheField?.GetValue(null) is not IDictionary cache)
            {
                logger.LogWarning(
                    "Hot-reload: MessagePack resolver cache not found ({Type}.{Field}) -- collectible ALCs may stay pinned after unload of {BundleId}.",
                    ResolverTypeName,
                    CacheFieldName,
                    bundleId);
                return;
            }

            var collectibleKeys = cache.Keys
                .OfType<Assembly>()
                .Where(assembly => AssemblyLoadContext.GetLoadContext(assembly) is { IsCollectible: true })
                .ToArray();

            foreach (var key in collectibleKeys)
                cache.Remove(key);

            if (collectibleKeys.Length > 0)
            {
                logger.LogInformation(
                    "Hot-reload: evicted {Count} collectible-keyed MessagePack resolver cache entries on unload of {BundleId}: {Assemblies}",
                    collectibleKeys.Length,
                    bundleId,
                    string.Join(", ", collectibleKeys.Select(assembly => assembly.GetName().Name)));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Hot-reload: MessagePack resolver cache eviction failed for bundle {BundleId} -- the old ALC may stay pinned.",
                bundleId);
        }
    }
}
