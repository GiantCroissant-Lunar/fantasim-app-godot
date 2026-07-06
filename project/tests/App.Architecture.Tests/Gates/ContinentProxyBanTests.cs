using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FantaSim.App.Architecture.Tests.Helpers;
using Xunit;

namespace FantaSim.App.Architecture.Tests.Gates;

/// <summary>
/// C4 — No new render-layer continent proxies (source scan).
/// Station-map contract 5: one canonical continent representation (per-cell ContinentalFraction
/// truth field → S3 → S4 → S5). Render-layer continent proxies (noise provinces, plate-membership
/// coloring, ad-hoc palettes from plate ids) are banned. The existing ProvinceTint is whitelisted as
/// a legacy lap-2 proxy scheduled for review in P3 — its usage must not spread.
/// </summary>
public sealed class ContinentProxyBanTests
{
    // Suffix-only enforcement: class declarations whose name ends in Palette, Tint, or Ramp.
    // A trailing word boundary is required so that VertexTintJitter (ends with "Jitter") and
    // PlateSurfaceTintFabric (ends with "Fabric") are not falsely flagged as rogue *Tint classes.
    // This preserves the spec's intent while matching only real palette/tint/ramp declarations.
    private static readonly Regex PaletteTintRampDeclaration = new(
        @"class\s+\w*(Palette|Tint|Ramp)\b",
        RegexOptions.Compiled);

    // Whitelist per the station map and the P1 plan. CrustAccentMapper is whitelisted but does not
    // match the suffix regex above (it is a *Mapper), so it is allowed implicitly.
    private static readonly HashSet<string> WhitelistedClassNames = new(StringComparer.Ordinal)
    {
        "HypsometricTint",
        "PlateIdentityPalette",
        "ContinentsPalette",
        "WorldTerrainRamp",
        "ProvinceTint",
    };

    private static readonly IReadOnlyList<string> ScanGlobs = new[]
    {
        "project/plugins/App.Presentation/**/*.cs",
        "project/contracts/App.World.Rendering/**/*.cs",
    };

    [Fact]
    public void NoNewPaletteTintRampProxies()
    {
        var root = RepoRootFinder.FindRepoRoot();
        var failures = new List<string>();

        foreach (var match in SourceScanner.ScanMultipleRelativeGlobs(root, ScanGlobs, PaletteTintRampDeclaration))
        {
            var className = ExtractClassName(match.Text);
            if (!WhitelistedClassNames.Contains(className))
            {
                failures.Add(
                    $"{match.File}:{match.LineNumber}: '{className}' is not in the continent-proxy whitelist. " +
                    "See station-map contract 5 and P1 plan C4.");
            }
        }

        Assert.Empty(failures);
    }

    /// <summary>
    /// ProvinceTint is the single legacy lap-2 proxy. It may appear only in:
    ///   - its own declaration file,
    ///   - its App.World.Tests test file,
    ///   - the PlanetPresentationBinder binder call site.
    /// Any other file is a violation of station-map contract 5.
    /// </summary>
    [Fact]
    public void ProvinceTint_UsageIsConfinedToWhitelist()
    {
        var root = RepoRootFinder.FindRepoRoot();
        var pattern = new Regex(@"\bProvinceTint\b", RegexOptions.Compiled);

        var allowedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            SourceScanner.RepoRelativePath(root, Path.Combine(root, "project", "contracts", "App.World.Rendering", "Rendering", "ProvinceTint.cs")),
            SourceScanner.RepoRelativePath(root, Path.Combine(root, "project", "tests", "App.World.Tests", "ProvinceTintTests.cs")),
            SourceScanner.RepoRelativePath(root, Path.Combine(root, "project", "plugins", "App.Presentation", "PlanetPresentationBinder.cs")),
        };

        var violations = new List<string>();
        foreach (var match in SourceScanner.ScanMultipleRelativeGlobs(root, ScanGlobs, pattern))
        {
            if (!allowedFiles.Contains(match.File))
            {
                violations.Add($"{match.File}:{match.LineNumber}: unexpected ProvinceTint reference");
            }
        }

        Assert.True(
            violations.Count == 0,
            "ProvinceTint usage is not confined to its whitelisted locations.\n" + string.Join("\n", violations));
    }

    private static string ExtractClassName(string line)
    {
        var match = PaletteTintRampDeclaration.Match(line);
        var classKeywordIndex = line.IndexOf("class ", System.StringComparison.Ordinal);
        if (classKeywordIndex < 0)
        {
            return string.Empty;
        }

        var afterClass = line[(classKeywordIndex + 6)..].TrimStart();
        var firstNonIdentifier = afterClass.IndexOfAny(new[] { ' ', ':', '<', '{', '(', ';' });
        return firstNonIdentifier > 0
            ? afterClass[..firstNonIdentifier]
            : afterClass;
    }
}
