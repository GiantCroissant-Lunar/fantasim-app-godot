using System.Text.Json;

namespace FantaSim.App.Common;

/// <summary>
/// Externalized share-lists for the plugin host's SharedAssemblyPolicy. Single source of truth
/// (config/shared-assembly-policy.json) consumed by BOTH Bootstrap.BuildPluginHost (runtime) AND
/// tools/bundles/stage_bundle.py (build-time bundle staging filter), so the two can never drift
/// (the MessagePack two-place-mirror lesson, 2026-07-03).
/// </summary>
public sealed class SharedAssemblyPolicyConfig
{
    private SharedAssemblyPolicyConfig(IReadOnlyList<string> exactMatches, IReadOnlyList<string> prefixes)
    {
        ExactMatches = exactMatches;
        Prefixes = prefixes;
    }

    public IReadOnlyList<string> ExactMatches { get; }

    public IReadOnlyList<string> Prefixes { get; }

    public static SharedAssemblyPolicyConfig ParseJson(string json)
    {
        const string source = "project/hosts/complete-app/config/shared-assembly-policy.json";

        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException($"Shared assembly policy config from {source} is empty.");

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse shared assembly policy config from {source}: {ex.Message}", ex);
        }

        using (doc)
        {
            return new SharedAssemblyPolicyConfig(
                ReadStringArray(doc.RootElement, "exactMatches", source),
                ReadStringArray(doc.RootElement, "prefixes", source));
        }
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string property, string source)
    {
        if (!root.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"Shared assembly policy config from {source} is missing a \"{property}\" array.");

        var values = new List<string>(array.GetArrayLength());
        foreach (var entry in array.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(entry.GetString()))
                throw new InvalidOperationException($"Shared assembly policy config from {source} has a non-string/empty \"{property}\" entry.");
            values.Add(entry.GetString()!);
        }

        return values;
    }
}
