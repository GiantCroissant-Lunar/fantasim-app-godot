using System.Collections.Generic;
using System.Text.RegularExpressions;
using FantaSim.App.Architecture.Tests.Helpers;
using Xunit;

namespace FantaSim.App.Architecture.Tests.Gates;

/// <summary>
/// C3 — Tick-addressed products only (source scan).
/// Station-map contract 3: every product the presentation consumes is tick-addressed.
/// Parameterless getters such as GetPlanetPresentationAsync() are banned in S5; that is how the
/// frozen-onset-frame defect entered. The one live violation is fixed in this packet.
/// </summary>
public sealed class TickAddressedProductsTests
{
    // Match the parameterless overload call only: literal empty parentheses.
    // Tick-addressed calls (GetPlanetPresentationAsync(_timeline.Tick)) are not matched.
    private static readonly Regex ParameterlessGetPlanetPresentation = new(
        @"GetPlanetPresentationAsync\s*\(\s*\)",
        RegexOptions.Compiled);

    [Fact]
    public void AppPresentation_UsesOnlyTickAddressedPlanetPresentation()
    {
        var root = RepoRootFinder.FindRepoRoot();
        var failures = new List<string>();

        foreach (var match in SourceScanner.ScanRelativeGlob(
            root,
            "project/plugins/App.Presentation/**/*.cs",
            ParameterlessGetPlanetPresentation))
        {
            failures.Add(
                $"{match.File}:{match.LineNumber}: parameterless GetPlanetPresentationAsync() is banned. " +
                "Use the tick-addressed overload. See station-map contract 3 and P1 plan C3.");
        }

        Assert.Empty(failures);
    }
}
