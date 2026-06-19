using System.Text.Json;
using Godot;

namespace FantaSim.App.Resource.Bundle;

public sealed class BundleVfs
{
    public bool LoadPck(string pckPath) => ProjectSettings.LoadResourcePack(pckPath);

    public string? ReadManifestJson(string bundleResPath)
    {
        var manifestPath = bundleResPath.PathJoin("manifest.json");
        if (!FileAccess.FileExists(manifestPath))
            return null;

        using var file = FileAccess.Open(manifestPath, FileAccess.ModeFlags.Read);
        return file?.GetAsText();
    }

    public BundleManifest? ReadManifest(string bundleResPath)
    {
        var json = ReadManifestJson(bundleResPath);
        return json is null ? null : JsonSerializer.Deserialize<BundleManifest>(json);
    }
}
