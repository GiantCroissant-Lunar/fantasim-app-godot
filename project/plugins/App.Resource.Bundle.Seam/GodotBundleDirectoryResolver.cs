using System;
using System.IO;
using System.Text.Json;
using FantaSim.App.Resource.Providers;
using Godot;
using GodotFileAccess = Godot.FileAccess;

namespace FantaSim.App.Resource.Bundle;

public sealed class GodotBundleDirectoryResolver : IDirectoryResolver
{
    private sealed class BundleDirectoriesConfig
    {
        public string[]? Directories { get; set; }
    }

    public string ResolveResourcesDirectory()
    {
        var configResolved = TryResolveFromConfig();
        if (configResolved is not null)
            return configResolved;

        if (TryGetExistingDirectory(ProjectSettings.GlobalizePath("res://system_bundles"), out var resDir))
            return resDir;

        var exeDir = OS.GetExecutablePath().GetBaseDir();
        return Path.Combine(exeDir, "bundles");
    }

    private static string? TryResolveFromConfig()
    {
        const string configPath = "res://config/bundle-directories.json";
        if (!GodotFileAccess.FileExists(configPath))
            return null;

        var json = GodotFileAccess.GetFileAsString(configPath);
        if (string.IsNullOrWhiteSpace(json))
            return null;

        BundleDirectoriesConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<BundleDirectoriesConfig>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }

        if (config?.Directories is not { Length: > 0 })
            return null;

        var projectDir = ProjectSettings.GlobalizePath("res://");
        foreach (var entry in config.Directories)
        {
            if (string.IsNullOrWhiteSpace(entry))
                continue;

            if (entry.StartsWith("res://", StringComparison.Ordinal))
            {
                if (TryGetExistingDirectory(ProjectSettings.GlobalizePath(entry), out var resolved))
                    return resolved;
            }
            else if (Path.IsPathRooted(entry))
            {
                if (TryGetExistingDirectory(entry, out var resolved))
                    return resolved;
            }
            else
            {
                var resolved = ResolveExistingRelativeDirectory(projectDir, entry);
                if (!string.IsNullOrWhiteSpace(resolved))
                    return resolved;

                var exeDir = OS.GetExecutablePath().GetBaseDir();
                resolved = ResolveExistingRelativeDirectory(exeDir, entry);
                if (!string.IsNullOrWhiteSpace(resolved))
                    return resolved;
            }
        }

        return null;
    }

    private static bool TryGetExistingDirectory(string? path, out string resolved)
    {
        resolved = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            path = path.Trim();
            if (!Path.IsPathRooted(path))
                return false;

            path = Path.GetFullPath(path);
            if (!Directory.Exists(path))
                return false;

            resolved = path;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolveExistingRelativeDirectory(string? baseDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
            return null;

        try
        {
            if (!Path.IsPathRooted(baseDirectory))
                return null;

            var resolved = Path.GetFullPath(Path.Combine(baseDirectory, relativePath));
            return Directory.Exists(resolved) ? resolved : null;
        }
        catch
        {
            return null;
        }
    }
}
