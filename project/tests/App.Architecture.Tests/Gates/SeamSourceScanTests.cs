using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FantaSim.App.Architecture.Tests.Helpers;
using Xunit;

namespace FantaSim.App.Architecture.Tests.Gates;

/// <summary>
/// C2 — Seam constructs no engine/domain-runtime types (source scan).
/// Station-map contract 1: S5 never touches engine types. Banned patterns are the concrete engine
/// anchors named in the station map: GlobeReconstructor, OnsetRoster, WorldCrustRunSpec,
/// WorldCrustMaterializer, CrustInitRecipe, LidFractureAtOnset.
/// </summary>
public sealed class SeamSourceScanTests
{
    private static readonly IReadOnlyList<Regex> BannedPatterns = new[]
    {
        new Regex(@"new\s+GlobeReconstructor", RegexOptions.Compiled),
        new Regex(@"OnsetRoster\s*\.", RegexOptions.Compiled),
        new Regex(@"WorldCrustRunSpec", RegexOptions.Compiled),
        new Regex(@"WorldCrustMaterializer", RegexOptions.Compiled),
        new Regex(@"CrustInitRecipe", RegexOptions.Compiled),
        new Regex(@"LidFractureAtOnset", RegexOptions.Compiled),
    };

    [Fact]
    public void AppPresentation_SourceDoesNotConstructEngineTypes()
    {
        var root = RepoRootFinder.FindRepoRoot();
        var failures = new List<string>();

        foreach (var pattern in BannedPatterns)
        {
            foreach (var match in SourceScanner.ScanRelativeGlob(root, "project/plugins/App.Presentation/**/*.cs", pattern))
            {
                failures.Add($"{match.File}:{match.LineNumber}: banned pattern '{pattern}'");
            }
        }

        Assert.Empty(failures);
    }
}
