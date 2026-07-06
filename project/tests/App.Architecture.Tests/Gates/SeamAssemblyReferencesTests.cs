using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FantaSim.App.Architecture.Tests.Helpers;
using Xunit;

namespace FantaSim.App.Architecture.Tests.Gates;

/// <summary>
/// C1 — Seam references no engine assemblies.
/// Station-map contract 1: S5 (App.Presentation) never references engine assemblies or types
/// (FantaSim.Geosphere.*, engine FantaSim.World.*). It sees IService products and contracts-tier
/// DTOs only. Also contract 2: no CrosscutFoundation.Config reference.
/// </summary>
public sealed class SeamAssemblyReferencesTests
{
    [Fact]
    public void AppPresentation_DoesNotReferenceEngineAssembliesOrConfig()
    {
        var assembly = LoadPresentationAssembly();
        var refs = assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

        Assert.All(refs, name =>
        {
            Assert.False(
                name?.StartsWith("FantaSim.Geosphere", StringComparison.Ordinal) ?? false,
                $"FantaSim.App.Presentation references engine assembly '{name}'. See station-map contract 1.");

            Assert.False(
                name?.StartsWith("FantaSim.World.", StringComparison.Ordinal) ?? false,
                $"FantaSim.App.Presentation references engine assembly '{name}'. See station-map contract 1.");

            Assert.NotEqual(
                "CrosscutFoundation.Config",
                name,
                StringComparer.Ordinal);
        });
    }

    private static Assembly LoadPresentationAssembly()
    {
        var repoRoot = RepoRootFinder.FindRepoRoot();
        var searchDirectories = new[]
        {
            Path.Combine(repoRoot, "project", "plugins", "App.Presentation", "bin", "Debug", "net8.0"),
            Path.Combine(repoRoot, "project", "plugins", "App.Presentation", ".godot", "mono", "temp", "bin", "Debug"),
        };

        foreach (var dir in searchDirectories)
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            var candidates = Directory.GetFiles(dir, "FantaSim.App.Presentation.dll");
            if (candidates.Length > 0)
            {
                return Assembly.LoadFrom(candidates[0]);
            }
        }

        throw new FileNotFoundException(
            "Could not find FantaSim.App.Presentation.dll. Build the solution before running architecture tests.");
    }
}
