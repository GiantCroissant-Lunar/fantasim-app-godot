using System;
using System.IO;
using Microsoft.Extensions.Logging;
using UnifyStorage.Abstractions;
using UnifyStorage.Runtime.LiteDb;

namespace FantaSim.App.Common.Storage;

/// <summary>
/// Constructs the resident LiteDB-backed <see cref="IDocumentStore"/> (2026-07-11 persistence slice
/// 1). Mirrors App.Activity's <c>CreateDocumentStore</c> (Services/Service.cs) construction pattern:
/// never throws out of the app boot path — a failure (bad path, locked file, disk full) logs and
/// returns a null store, degrading every consumer to in-memory-only rather than crashing boot.
/// </summary>
public static class ResidentDocumentStoreFactory
{
    /// <summary>
    /// Returns the constructed store plus its disposer (same instance today — LiteDbDocumentStore
    /// owns its own <c>ILiteDatabase</c> when built from a connection string), or (null, null) when
    /// persistence is disabled by config or construction failed.
    /// </summary>
    public static (IDocumentStore? Store, IDisposable? Owned) Create(ResidentPersistenceOptions options, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(log);

        if (!options.CrustCacheEnabled)
        {
            log.LogInformation("Resident crust-product cache persistence disabled via config ({Key}=false); running in-memory-only.", "persistence:crustCache:enabled");
            return (null, null);
        }

        var storePath = ResidentPersistenceOptions.ResolveStorePath(options.CrustCacheStorePath);
        try
        {
            var directory = Path.GetDirectoryName(storePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var store = new LiteDbDocumentStore(storePath);
            log.LogInformation("Resident document store opened at {Path}.", storePath);
            return (store, store);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to open resident document store at {Path}; persistence disabled.", storePath);
            return (null, null);
        }
    }
}
