using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FantaSim.App.Architecture.Tests.Helpers;
using FantaSim.App.World;
using Xunit;

namespace FantaSim.App.Architecture.Tests.Gates;

/// <summary>
/// C2 — World service contract assembly stays shared across the ALC boundary.
/// <para>
/// The resident <c>PlanetPresentationBinder</c> and the collectible world bundle both resolve
/// <see cref="IService"/> from the registry; if the contract assembly loaded into BOTH the resident
/// ALC and the collectible ALC, <c>System.Type</c> identity would split and
/// <c>IRegistry.TryGet&lt;IService&gt;()</c> would silently return null on the resident side. That is
/// the MessagePack / rendering-contracts ALC-pin incident class (see Bootstrap.cs SharedAssemblyPolicy
/// comments + the complete-app.csproj host-slim note). This gate fails if the <c>IService</c>-declaring
/// assembly is ever promoted into the collectible bundle list or renamed out of the shared-prefix closure.
/// </para>
/// <para>
/// The dedicated-contract assertion is load-bearing: the resident host references
/// <c>contracts\App.World</c> (assembly <c>FantaSim.App.World.Contracts</c>), NOT the collectible
/// <c>plugins\App.World</c> (assembly <c>FantaSim.App.World</c>). If <c>IService</c> ever moved into the
/// plugin assembly, this test fails before the export regresses.
/// </para>
/// </summary>
public sealed class WorldContractSharedClosureTests
{
    private const string SharedPrefix = "FantaSim.App.World.";
    private const string ExpectedContractAssembly = "FantaSim.App.World.Contracts";

    [Fact]
    public void WorldServiceContractAssembly_IsTheDedicatedContractsAssembly_NotTheCollectiblePlugin()
    {
        var contractAssembly = typeof(IService).Assembly.GetName().Name;
        Assert.Equal(ExpectedContractAssembly, contractAssembly);
    }

    [Fact]
    public void WorldServiceContractAssembly_IsCoveredByTheSharedPrefixClosure()
    {
        var contractAssembly = typeof(IService).Assembly.GetName().Name;
        Assert.StartsWith(SharedPrefix, contractAssembly);
    }

    [Fact]
    public void WorldServiceContractAssembly_IsNotACollectibleBundleAssembly()
    {
        var contractAssembly = typeof(IService).Assembly.GetName().Name;
        var collectible = ReadCollectibleAssemblyNames();
        Assert.DoesNotContain(contractAssembly, collectible);
    }

    private static IReadOnlyCollection<string> ReadCollectibleAssemblyNames()
    {
        var repoRoot = RepoRootFinder.FindRepoRoot();
        var configPath = Path.Combine(
            repoRoot, "project", "hosts", "complete-app", "config", "collectible-bundles.json");

        var names = new HashSet<string>(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
        foreach (var bundle in doc.RootElement.GetProperty("bundles").EnumerateArray())
        {
            if (bundle.TryGetProperty("pluginAssembly", out var plugin)
                && plugin.ValueKind == JsonValueKind.String)
            {
                names.Add(Path.GetFileNameWithoutExtension(plugin.GetString()!));
            }

            if (bundle.TryGetProperty("assemblyNames", out var arr)
                && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                        names.Add(item.GetString()!);
                }
            }
        }

        return names;
    }
}
