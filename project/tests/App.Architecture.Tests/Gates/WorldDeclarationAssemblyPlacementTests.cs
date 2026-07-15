using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
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
    private static readonly Version MinimumImmutableCollectionsVersion = new(10, 0);

    private static string ConfigPath(string fileName) =>
        Path.Combine(RepoRootFinder.FindRepoRoot(), "project", "hosts", "complete-app", "config", fileName);

    private static string HostCsprojPath() =>
        Path.Combine(RepoRootFinder.FindRepoRoot(), "project", "hosts", "complete-app", "complete-app.csproj");

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

    /// <summary>
    /// F1 fix (2026-07-15): the world bundle's SurrealDb stack (SurrealDb.Net.dll,
    /// SurrealDb.Embedded.InMemory.dll, both staged collectible in world.pck) carries an
    /// AssemblyRef of System.Collections.Immutable Version=10.0.0.0. The "System." prefix in
    /// shared-assembly-policy.json routes that assembly name to the PARENT (host) ALC and
    /// HierarchicalPluginLoadContext binds by simple name (no version check), so whatever the
    /// host serves is what those bundle assemblies get. If the host only pins/ships the net8.0
    /// runtime pack's 8.0.x copy, first use of a 9.0+/10.0 member throws
    /// MissingMethodException/TypeLoadException. The host must therefore declare its own
    /// System.Collections.Immutable reference at >= the version those bundle assemblies require,
    /// AND the world bundle must keep omitting it from assemblyNames (a collectible override here
    /// would reintroduce a second copy alongside the shared one it needs to match).
    /// </summary>
    [Fact]
    public void World_bundle_omits_immutable_collections_from_assemblyNames()
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
    public void Host_pins_immutable_collections_at_or_above_the_version_the_world_bundle_requires()
    {
        var hostCsprojPath = HostCsprojPath();
        Assert.True(File.Exists(hostCsprojPath), $"Host csproj not found: {hostCsprojPath}");

        var hostCsproj = XDocument.Load(hostCsprojPath);
        var immutableRef = hostCsproj
            .Descendants("PackageReference")
            .FirstOrDefault(e => string.Equals((string?)e.Attribute("Include"), ImmutableCollections, StringComparison.Ordinal));

        Assert.True(
            immutableRef is not null,
            $"{hostCsprojPath} must declare a PackageReference for {ImmutableCollections} so the " +
            "parent ALC serves a version compatible with what the collectible world bundle's " +
            "SurrealDb.Net/SurrealDb.Embedded.InMemory assemblies reference (AssemblyRef Version=10.0.0.0).");

        var versionText = (string?)immutableRef!.Attribute("Version");
        var parsed = Version.TryParse(versionText, out var version);
        Assert.True(
            parsed,
            $"{ImmutableCollections} PackageReference in {hostCsprojPath} has no parseable Version (was '{versionText}').");

        Assert.True(
            version! >= MinimumImmutableCollectionsVersion,
            $"{ImmutableCollections} must be pinned to >= {MinimumImmutableCollectionsVersion} in " +
            $"{hostCsprojPath} (was {version}); the world bundle's SurrealDb.Net and " +
            "SurrealDb.Embedded.InMemory assemblies carry an AssemblyRef of Version=10.0.0.0 for this assembly.");
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
