using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FantaSim.App.Resource;

public sealed record CommonAssemblyIdentity(string AssemblyName, string Sha256);

/// <summary>
/// Pure identity model for the common resident-layer gate: the stripped exe's generated
/// expectation (config/common-resident-expected.json) vs what common.pck's manifest declares.
/// Identity is {assemblyName, sha256}; mismatch of any kind is boot-fatal in the caller.
/// </summary>
public static class CommonResidentCatalog
{
    private sealed class ExpectedFile
    {
        [JsonPropertyName("bundleId")]
        public string BundleId { get; set; } = string.Empty;

        [JsonPropertyName("assemblies")]
        public List<ExpectedEntry> Assemblies { get; set; } = new();
    }

    private sealed class ExpectedEntry
    {
        [JsonPropertyName("assemblyName")]
        public string AssemblyName { get; set; } = string.Empty;

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = string.Empty;
    }

    public static IReadOnlyList<CommonAssemblyIdentity> ParseExpected(string json)
    {
        var file = JsonSerializer.Deserialize<ExpectedFile>(json)
            ?? throw new JsonException("expected-catalog json deserialized to null");
        var result = new List<CommonAssemblyIdentity>(file.Assemblies.Count);
        foreach (var entry in file.Assemblies)
        {
            if (string.IsNullOrWhiteSpace(entry.AssemblyName) || string.IsNullOrWhiteSpace(entry.Sha256))
                throw new JsonException("expected-catalog entry missing assemblyName or sha256");
            result.Add(new CommonAssemblyIdentity(entry.AssemblyName, entry.Sha256));
        }
        return result;
    }

    public static IReadOnlyList<string> Validate(
        IReadOnlyList<CommonAssemblyIdentity> expected,
        IReadOnlyList<CommonAssemblyIdentity> actual)
    {
        var errors = new List<string>();
        var actualByName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var a in actual)
            actualByName[a.AssemblyName] = a.Sha256;

        var expectedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in expected)
        {
            expectedNames.Add(e.AssemblyName);
            if (!actualByName.TryGetValue(e.AssemblyName, out var actualSha))
                errors.Add($"missing from common.pck: {e.AssemblyName}");
            else if (!string.Equals(e.Sha256, actualSha, StringComparison.OrdinalIgnoreCase))
                errors.Add($"hash mismatch for {e.AssemblyName}: expected {e.Sha256}, pck has {actualSha}");
        }

        foreach (var a in actual)
        {
            if (!expectedNames.Contains(a.AssemblyName))
                errors.Add($"unexpected assembly in common.pck: {a.AssemblyName}");
        }

        return errors;
    }
}
