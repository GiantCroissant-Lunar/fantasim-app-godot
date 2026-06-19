using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Godot;
using GodotFileAccess = Godot.FileAccess;

namespace FantaSim.App.Resource.Bundle;

public sealed class DllExtractor
{
    private static readonly string SessionTempRoot = Path.Combine(
        Path.GetTempPath(),
        "fantasim_bundles",
        Process.GetCurrentProcess().Id.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"));

    private static int _loadSeq;

    public string? ExtractAllToTemp(string bundleResPath, string pluginDllName)
    {
        var pluginResPath = bundleResPath + "/" + pluginDllName;
        if (!GodotFileAccess.FileExists(pluginResPath))
            return null;

        var bundleTempDir = NewBundleTempDir(bundleResPath);
        string? pluginTempPath = null;

        using (var dir = DirAccess.Open(bundleResPath))
        {
            if (dir is not null)
            {
                foreach (var fileName in dir.GetFiles())
                {
                    var name = fileName.EndsWith(".remap", StringComparison.OrdinalIgnoreCase)
                        ? fileName[..^".remap".Length]
                        : fileName;
                    if (!name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var extracted = ExtractDll(bundleResPath + "/" + name, bundleTempDir, name);
                    if (extracted is not null && string.Equals(name, pluginDllName, StringComparison.OrdinalIgnoreCase))
                        pluginTempPath = extracted;
                }
            }
        }

        pluginTempPath ??= ExtractDll(pluginResPath, bundleTempDir, pluginDllName);
        return pluginTempPath;
    }

    private static string NewBundleTempDir(string bundleResPath)
    {
        var loadId = Interlocked.Increment(ref _loadSeq);
        var bundleTempDir = Path.Combine(
            SessionTempRoot,
            BundleTempName(bundleResPath),
            loadId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(bundleTempDir);
        return bundleTempDir;
    }

    private static string? ExtractDll(string resPath, string destDir, string dllName)
    {
        using var file = GodotFileAccess.Open(resPath, GodotFileAccess.ModeFlags.Read);
        if (file is null)
            return null;

        var bytes = file.GetBuffer((long)file.GetLength());
        var tempPath = Path.Combine(destDir, dllName);
        File.WriteAllBytes(tempPath, bytes);
        return tempPath;
    }

    private static string BundleTempName(string bundleResPath)
    {
        var normalized = bundleResPath.Replace('\\', '/').TrimEnd('/');
        var slash = normalized.LastIndexOf('/');
        var name = slash >= 0 ? normalized[(slash + 1)..] : normalized;
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "bundle" : name;
    }
}
