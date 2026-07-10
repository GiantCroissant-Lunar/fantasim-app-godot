namespace FantaSim.App.Common;

/// <summary>
/// Resolves the app.json path for the layered config. In a Godot .NET export
/// AppContext.BaseDirectory is the per-arch data dir (Contents/Resources/data_*),
/// which carries no config/; the provisioned config/ lives next to the executable
/// (Contents/MacOS — same convention as the common-resident catalog and the
/// layer-track registry jsons, see G26). Probe both so editor/dev runs and exports
/// resolve the same shipped asset.
/// </summary>
public static class AppConfigLocator
{
    public static string ResolveAppJsonPath(string baseDirectory, string? exeDirectory)
    {
        foreach (var root in new[] { baseDirectory, exeDirectory })
        {
            if (string.IsNullOrEmpty(root))
                continue;
            var candidate = Path.Combine(root, "config", "app.json");
            if (File.Exists(candidate))
                return candidate;
        }

        return Path.Combine(baseDirectory, "config", "app.json");
    }
}
