using System.Text.Json;

namespace FantaSim.App.Common;

/// <summary>
/// Runtime registry of bundle plugin assemblies that must remain collectible.
/// </summary>
public sealed class CollectibleBundles
{
    private readonly HashSet<string> _assemblyNamesSet;

    private CollectibleBundles(IReadOnlyList<string> assemblyNames)
    {
        AssemblyNames = assemblyNames;
        _assemblyNamesSet = new HashSet<string>(assemblyNames, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> AssemblyNames { get; }

    public bool ContainsAssembly(string assemblyName) => _assemblyNamesSet.Contains(assemblyName);

    public static CollectibleBundles Empty { get; } = new(Array.Empty<string>());

    public static CollectibleBundles ParseJson(string json)
    {
        const string source = "project/hosts/complete-app/config/collectible-bundles.json";

        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException($"Collectible bundle config from {source} is empty.");

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse collectible bundle config from {source}: {ex.Message}", ex);
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("bundles", out var bundles)
                || bundles.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException($"Collectible bundle config from {source} is missing a \"bundles\" array.");
            }

            var assemblyNames = new List<string>(bundles.GetArrayLength());
            foreach (var entry in bundles.EnumerateArray())
            {
                if (!entry.TryGetProperty("bundleId", out var bundleIdProp)
                    || bundleIdProp.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(bundleIdProp.GetString()))
                {
                    throw new InvalidOperationException($"Collectible bundle config from {source} has an entry with missing bundleId.");
                }

                if (!entry.TryGetProperty("pluginAssembly", out var pluginAssemblyProp)
                    || pluginAssemblyProp.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(pluginAssemblyProp.GetString()))
                {
                    throw new InvalidOperationException($"Collectible bundle config from {source} has an entry with missing pluginAssembly.");
                }

                var pluginAssembly = pluginAssemblyProp.GetString()!;
                assemblyNames.Add(StripDll(pluginAssembly));

                // Optional companion impl assemblies that must also load into the bundle's collectible
                // ALC (e.g. a multi-assembly domain bundle like "world": the plugin assembly carries the
                // [Plugin], but its sibling impl assemblies must be excluded from the shared parent too).
                if (entry.TryGetProperty("assemblyNames", out var extraProp)
                    && extraProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var extra in extraProp.EnumerateArray())
                    {
                        if (extra.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(extra.GetString()))
                        {
                            throw new InvalidOperationException($"Collectible bundle config from {source} has a non-string/empty assemblyNames entry for bundle '{bundleIdProp.GetString()}'.");
                        }

                        assemblyNames.Add(StripDll(extra.GetString()!));
                    }
                }
            }

            return new CollectibleBundles(assemblyNames);
        }
    }

    private static string StripDll(string assembly)
        => assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? assembly[..^4] : assembly;
}
