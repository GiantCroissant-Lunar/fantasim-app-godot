using System;
using System.IO;
using ConfigService = CrosscutFoundation.Config.IService;

namespace FantaSim.App.Common.Storage;

/// <summary>
/// Config-gated options for the resident crust-product cache store (2026-07-11 persistence slice 1,
/// vault/specs/2026-07-11-surrealdb-persistence-slice1-design.md section 3.2/4.3/6). Read from the
/// layered app.json + env config (<see cref="Bootstrap"/> already registers a <c>CrosscutFoundation.
/// Config.IService</c> under the "config" tag) via the same <c>config.Get</c>/<c>GetValue</c> pattern
/// <c>WorldTruthStoreOptions.FromConfig</c> (App.World) uses — flip <c>persistence:crustCache:enabled</c>
/// to false and restart to disable persistence without a rebuild; no key present defaults to enabled
/// per the task's resolved default.
/// </summary>
public sealed record ResidentPersistenceOptions(bool CrustCacheEnabled, string CrustCacheStorePath)
{
    private const string EnabledKey = "persistence:crustCache:enabled";
    private const string PathKey = "persistence:crustCache:path";
    private const string DefaultStorePath = "user://crust-cache.litedb";

    public static ResidentPersistenceOptions FromConfig(ConfigService? config)
    {
        var enabled = config?.GetValue(EnabledKey, true) ?? true;
        var path = config?.Get(PathKey);
        return new ResidentPersistenceOptions(
            enabled,
            string.IsNullOrWhiteSpace(path) ? DefaultStorePath : path.Trim());
    }

    /// <summary>
    /// Resolves a <c>user://</c>-prefixed (or exe-relative, or rooted) store path to an absolute
    /// filesystem path. Mirrors App.Activity's <c>ResolveStorePath</c>
    /// (project/plugins/App.Activity/Services/Service.cs) exactly — the same convention, kept
    /// resident here since App.Common (not a per-bundle plugin) now owns the construction site.
    /// <c>user://</c> resolves to <c>Environment.SpecialFolder.LocalApplicationData/FantaSim/
    /// complete-app/&lt;relative&gt;</c>, falling back to <see cref="AppContext.BaseDirectory"/> when
    /// that special folder is empty (matches the Godot-export BaseDirectory convention, G26).
    /// </summary>
    public static string ResolveStorePath(string configuredPath)
    {
        var path = string.IsNullOrWhiteSpace(configuredPath) ? DefaultStorePath : configuredPath.Trim();

        const string UserPrefix = "user://";
        if (path.StartsWith(UserPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
                localAppData = AppContext.BaseDirectory;

            var relative = path[UserPrefix.Length..]
                .Replace('/', Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return Path.GetFullPath(Path.Combine(localAppData, "FantaSim", "complete-app", relative));
        }

        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
    }
}
