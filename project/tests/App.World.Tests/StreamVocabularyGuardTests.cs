using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FantaSim.App.World.Composition;
using FantaSim.World.TruthStream;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Architecture guard proving <see cref="WorldStreamVocabulary"/> is the ONLY production minting
/// point for five-axis stream identities under the App.World plugins. Mirrors the source-scan
/// technique in <see cref="RotationSourceSeamTests"/>.
///
/// Doctrine authority: fantasim-world/vault/architecture/planet-stack-model.md S2
/// (L3 stellar, L2 world, L1 regional, L0 local, negative micro; plate topology = L2).
/// </summary>
public sealed class StreamVocabularyGuardTests
{
    /// <summary>
    /// Catches target-typed identity constructions the literal scans miss:
    /// fields/properties/locals declared as the identity type and assigned `new(...)`
    /// (with `=` or `=>`), e.g. `TruthStreamIdentity Foo = new("a", "b", 2, "c", "d");`.
    /// </summary>
    private static readonly Regex TargetTypedConstruction = new(
        @"\b(?:TruthStreamIdentity|LayerTrackStreamId)\s+[A-Za-z_@]\w*\s*(?:=>|=)\s*new\s*\(",
        RegexOptions.Compiled);

    private static readonly string[] AllowedConstructionFiles =
    {
        "WorldStreamVocabulary.cs",
        "RotationSourceSelectionCodec.cs",
    };

    private static readonly string[] ScannedDirectories =
    {
        "project/plugins/App.World",
        "project/plugins/App.World.Composition",
    };

    [Fact]
    public void No_production_file_constructs_TruthStreamIdentity_or_LayerTrackStreamId_inline()
    {
        var violations = new List<string>();
        foreach (var dir in ScannedDirectories)
        {
            var fullDir = ProjectDir(dir);
            if (!Directory.Exists(fullDir))
                continue;

            foreach (var file in Directory.EnumerateFiles(fullDir, "*.cs", SearchOption.AllDirectories))
            {
                var fileName = Path.GetFileName(file);
                if (AllowedConstructionFiles.Contains(fileName))
                    continue;

                var source = File.ReadAllText(file);
                if (source.Contains("new TruthStreamIdentity(", StringComparison.Ordinal))
                    violations.Add($"{Path.GetRelativePath(RepoRoot(), file)}: new TruthStreamIdentity(");
                if (source.Contains("new LayerTrackStreamId(", StringComparison.Ordinal))
                    violations.Add($"{Path.GetRelativePath(RepoRoot(), file)}: new LayerTrackStreamId(");

                // Target-typed constructions bypass the literal scans above
                // (e.g. `TruthStreamIdentity Foo = new(...)` — found live in OnsetRoster 2026-07-14).
                foreach (Match match in TargetTypedConstruction.Matches(source))
                    violations.Add($"{Path.GetRelativePath(RepoRoot(), file)}: target-typed {match.Value.Trim()}");
            }
        }

        Assert.True(violations.Count == 0,
            "Production files must not construct stream identities inline; use WorldStreamVocabulary instead.\n" +
            "Violations:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void Allowed_construction_file_RotationSourceSelectionCodec_only_decodes()
    {
        var source = File.ReadAllText(ProjectFile(
            "project/plugins/App.World/History/RotationSourceSelectionCodec.cs"));

        // The one allowed `new TruthStreamIdentity(` is in DecodeImported — a deserialization path,
        // not a production minting point.
        Assert.Contains("new TruthStreamIdentity(", source, StringComparison.Ordinal);
        Assert.Contains("DecodeImported", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Plates_factory_is_doctrine_correct_L2()
    {
        var stream = WorldStreamVocabulary.Plates("earth", "main");
        Assert.Equal(2, stream.LLevel);
        Assert.Equal("earth:main:L2:geosphere:plates", stream.ToStreamKey());
    }

    [Fact]
    public void PlateTopologyTruth_factory_matches_engine_lockstep_identity()
    {
        var stream = WorldStreamVocabulary.PlateTopologyTruth();
        Assert.Equal(2, stream.LLevel);
        Assert.Equal("default:main:L2:geo.plates.topology:M0", stream.ToStreamKey());
    }

    [Fact]
    public void ImportsControl_factory_is_doctrine_correct_L2()
    {
        var stream = WorldStreamVocabulary.ImportsControl("earth", "main");
        Assert.Equal(2, stream.LLevel);
        Assert.Equal("earth:main:L2:world:imports", stream.ToStreamKey());
    }

    [Fact]
    public void RotationSelection_factory_is_doctrine_correct_L2()
    {
        var stream = WorldStreamVocabulary.RotationSelection();
        Assert.Equal(2, stream.LLevel);
        Assert.Equal("app:main:L2:world:rotation-bindings", stream.ToStreamKey());
    }

    [Fact]
    public void Generation_factory_is_doctrine_correct_L2()
    {
        var stream = WorldStreamVocabulary.Generation();
        Assert.Equal(2, stream.LLevel);
        Assert.Equal("app:main:L2:world:default", stream.ToStreamKey());
    }

    [Fact]
    public void TrackDefault_factory_is_doctrine_correct()
    {
        var track = WorldStreamVocabulary.TrackDefault();
        Assert.Equal("default", track.Variation);
        Assert.Equal("main", track.Branch);
        Assert.Equal("L2", track.L);
        Assert.Equal("world", track.Domain);
        Assert.Equal("default", track.Model);
    }

    [Fact]
    public void TruthEventsTrack_factory_is_doctrine_correct()
    {
        var track = WorldStreamVocabulary.TruthEventsTrack();
        Assert.Equal("app", track.Variation);
        Assert.Equal("main", track.Branch);
        Assert.Equal("L2", track.L);
        Assert.Equal("world", track.Domain);
        Assert.Equal("default", track.Model);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Plates_rejects_empty_worldId(string worldId)
    {
        Assert.Throws<ArgumentException>(() => WorldStreamVocabulary.Plates(worldId, "main"));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("10.0.0.1")]
    public void Plates_rejects_ip_address_worldId(string worldId)
    {
        Assert.Throws<ArgumentException>(() => WorldStreamVocabulary.Plates(worldId, "main"));
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "project", "plugins")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private static string ProjectDir(string relativePath)
        => Path.Combine(RepoRoot(), relativePath);

    private static string ProjectFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException($"Could not find project file '{relativePath}'.");
    }
}
