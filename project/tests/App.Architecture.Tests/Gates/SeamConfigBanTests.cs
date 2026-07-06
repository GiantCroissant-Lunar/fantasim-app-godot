using System.Collections.Generic;
using System.Text.RegularExpressions;
using FantaSim.App.Architecture.Tests.Helpers;
using Xunit;

namespace FantaSim.App.Architecture.Tests.Gates;

/// <summary>
/// C5 — Config reads stay out of the seam (source scan).
/// Station-map contract 2: S5 never reads config directly. Configuration reaches the seam as plain
/// values plumbed by the host or as document fields. Banned: CrosscutFoundation.Config namespace usage
/// and _config. member access.
/// </summary>
public sealed class SeamConfigBanTests
{
    private static readonly IReadOnlyList<Regex> BannedPatterns = new[]
    {
        new Regex(@"CrosscutFoundation\.Config", RegexOptions.Compiled),
        new Regex(@"_config\.", RegexOptions.Compiled),
    };

    [Fact]
    public void AppPresentation_DoesNotReadConfig()
    {
        var root = RepoRootFinder.FindRepoRoot();
        var failures = new List<string>();

        foreach (var pattern in BannedPatterns)
        {
            foreach (var match in SourceScanner.ScanRelativeGlob(root, "project/plugins/App.Presentation/**/*.cs", pattern))
            {
                failures.Add($"{match.File}:{match.LineNumber}: banned config access '{pattern}'");
            }
        }

        Assert.Empty(failures);
    }
}
