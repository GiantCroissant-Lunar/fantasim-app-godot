using System.IO;
using System.Linq;
using System.Text.Json;
using FantaSim.App.Architecture.Tests.Helpers;
using Xunit;

namespace FantaSim.App.Architecture.Tests.Gates;

/// <summary>
/// Assembly placement gate for the cosmology declaration registry stack (G-010):
/// the cosmology contracts assembly is resident-shared (parent ALC), while the
/// registry and science implementation assemblies are world-bundle collectible.
/// This prevents dual copies and ALC-pinning across the resident/bundle boundary.
/// </summary>
public sealed class WorldDeclarationAssemblyPlacementTests
{
    private const string Contracts = "FantaSim.Mythosphere.Cosmology.Contracts";
    private const string Registry = "FantaSim.Mythosphere.Cosmology.Registry";
    private const string Science = "FantaSim.Mythosphere.Cosmology.Science";
    private const string ImmutableCollections = "System.Collections.Immutable";
    private const string WorldExportContracts = "FantaSim.World.Export.Contracts";

    private static string ConfigPath(string fileName) =>
        Path.Combine(RepoRootFinder.FindRepoRoot(), "project", "hosts", "complete-app", "config", fileName);

    private static JsonDocument LoadConfig(string fileName)
    {
        var path = ConfigPath(fileName);
        Assert.True(File.Exists(path), $"Config file not found: {path}");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    [Fact]
    public void Contract_assembly_is_in_shared_policy_exactMatches()
    {
        var doc = LoadConfig("shared-assembly-policy.json");
        var matches = doc.RootElement
            .GetProperty("exactMatches")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToArray();
        Assert.Contains(Contracts, matches);
    }

    [Fact]
    public void Contract_assembly_is_in_common_exactMatches()
    {
        var doc = LoadConfig("shared-assembly-policy.json");
        var matches = doc.RootElement
            .GetProperty("common")
            .GetProperty("exactMatches")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToArray();
        Assert.Contains(Contracts, matches);
    }

    [Fact]
    public void Registry_assembly_is_not_in_shared_policy_exactMatches()
    {
        var doc = LoadConfig("shared-assembly-policy.json");
        var matches = doc.RootElement
            .GetProperty("exactMatches")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToArray();
        Assert.DoesNotContain(Registry, matches);
    }

    [Fact]
    public void Science_assembly_is_not_in_shared_policy_exactMatches()
    {
        var doc = LoadConfig("shared-assembly-policy.json");
        var matches = doc.RootElement
            .GetProperty("exactMatches")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToArray();
        Assert.DoesNotContain(Science, matches);
    }

    [Fact]
    public void Registry_assembly_is_in_world_bundle_assemblyNames()
    {
        var worldBundle = GetWorldBundle();
        var names = worldBundle
            .GetProperty("assemblyNames")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToArray();
        Assert.Contains(Registry, names);
    }

    [Fact]
    public void Science_assembly_is_in_world_bundle_assemblyNames()
    {
        var worldBundle = GetWorldBundle();
        var names = worldBundle
            .GetProperty("assemblyNames")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToArray();
        Assert.Contains(Science, names);
    }

    [Fact]
    public void Immutable_collections_used_by_shared_contract_stay_parent_shared()
    {
        var worldBundle = GetWorldBundle();
        var names = worldBundle
            .GetProperty("assemblyNames")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToArray();

        Assert.DoesNotContain(ImmutableCollections, names);
    }

    [Fact]
    public void World_export_contract_dependency_is_in_shared_policy_and_common_layer()
    {
        var doc = LoadConfig("shared-assembly-policy.json");
        var shared = doc.RootElement
            .GetProperty("exactMatches")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToArray();
        var common = doc.RootElement
            .GetProperty("common")
            .GetProperty("exactMatches")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToArray();

        Assert.Contains(WorldExportContracts, shared);
        Assert.Contains(WorldExportContracts, common);
    }

    [Fact]
    public void World_export_contract_dependency_is_not_collectible()
    {
        var worldBundle = GetWorldBundle();
        var names = worldBundle
            .GetProperty("assemblyNames")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToArray();

        Assert.DoesNotContain(WorldExportContracts, names);
    }

    [Fact]
    public void Contract_assembly_is_not_in_any_bundle_assemblyNames()
    {
        var doc = LoadConfig("collectible-bundles.json");
        foreach (var bundle in doc.RootElement.GetProperty("bundles").EnumerateArray())
        {
            if (!bundle.TryGetProperty("assemblyNames", out var names))
                continue;
            var assemblyNames = names.EnumerateArray().Select(e => e.GetString()).ToArray();
            Assert.DoesNotContain(Contracts, assemblyNames);
        }
    }

    private static JsonElement GetWorldBundle()
    {
        var doc = LoadConfig("collectible-bundles.json");
        foreach (var bundle in doc.RootElement.GetProperty("bundles").EnumerateArray())
        {
            if (bundle.GetProperty("bundleId").GetString() == "world")
                return bundle;
        }
        throw new Xunit.Sdk.XunitException("Bundle 'world' not found in collectible-bundles.json");
    }
}
