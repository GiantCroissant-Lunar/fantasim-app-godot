using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FantaSim.App.Architecture.Tests.Helpers;

/// <summary>
/// Simple filesystem source scanner used by the architecture conformance rules.
/// All paths are rooted from the repo root discovered by <see cref="RepoRootFinder"/>.
/// </summary>
internal static class SourceScanner
{
    public static IEnumerable<SourceMatch> ScanRelativeGlob(string repoRoot, string relativeGlob, Regex pattern)
    {
        var normalized = relativeGlob.Replace('/', Path.DirectorySeparatorChar);

        // Support the 'dir/**/*.ext' pattern used by the P1 plan: the '**/' segment means
        // 'recursive under the directory to its left'. We split on '**/' and enumerate with
        // SearchOption.AllDirectories using the remaining file pattern.
        var doubleStarIndex = normalized.IndexOf("**" + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        string directory;
        string glob;
        SearchOption searchOption;
        if (doubleStarIndex >= 0)
        {
            directory = Path.GetFullPath(Path.Combine(repoRoot, normalized[..doubleStarIndex]));
            glob = normalized[(doubleStarIndex + 3)..];
            searchOption = SearchOption.AllDirectories;
        }
        else
        {
            var fullPath = Path.GetFullPath(Path.Combine(repoRoot, normalized));
            directory = Path.GetDirectoryName(fullPath)!;
            glob = Path.GetFileName(fullPath);
            searchOption = SearchOption.TopDirectoryOnly;
        }

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Scan target not found: {directory}");
        }

        foreach (var file in Directory.EnumerateFiles(directory, glob, searchOption)
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (pattern.IsMatch(lines[i]))
                {
                    yield return new SourceMatch(
                        RepoRelativePath(repoRoot, file),
                        LineNumber: i + 1,
                        Text: lines[i]);
                }
            }
        }
    }

    public static IEnumerable<SourceMatch> ScanMultipleRelativeGlobs(
        string repoRoot,
        IReadOnlyList<string> relativeGlobs,
        Regex pattern)
    {
        return relativeGlobs
            .SelectMany(g => ScanRelativeGlob(repoRoot, g, pattern))
            .OrderBy(m => m.File, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.LineNumber);
    }

    public static string RepoRelativePath(string repoRoot, string fullPath)
    {
        var rootUri = new Uri(Path.GetFullPath(repoRoot) + Path.DirectorySeparatorChar);
        var fileUri = new Uri(fullPath);
        return rootUri.MakeRelativeUri(fileUri).ToString().Replace('/', Path.DirectorySeparatorChar);
    }

    public sealed record SourceMatch(string File, int LineNumber, string Text);
}
