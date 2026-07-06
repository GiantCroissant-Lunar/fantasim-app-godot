using System;
using System.IO;
using System.Linq;

namespace FantaSim.App.Architecture.Tests.Helpers;

/// <summary>
/// Walks up from the test assembly's base directory until it finds the solution
/// file that anchors the repository. Source-scan tests need filesystem paths rooted
/// at the repo, not at the test bin.
/// </summary>
internal static class RepoRootFinder
{
    public static string FindRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine(current, "project", "FantaSim.sln");
            if (File.Exists(candidate))
            {
                return current;
            }

            var parent = Directory.GetParent(current);
            current = parent?.FullName ?? string.Empty;
        }

        throw new InvalidOperationException(
            "Could not locate project/FantaSim.sln by walking up from " + AppContext.BaseDirectory);
    }
}
