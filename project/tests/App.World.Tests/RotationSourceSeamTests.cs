using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using FantaSim.App.World.Crust;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Dto;
using FantaSim.App.World.GenerationGraph;
using FantaSim.App.World.Globe;
using FantaSim.App.World.Services;
using FantaSim.Geosphere.Plate.Topology;
using FantaSim.World.TruthStream.Core;
using ServiceArchi.Core;
using UnifyCell;
using UnifyGeometry.Spherical;
using UnifyMaths;
using Xunit;

namespace FantaSim.App.World.Tests;

public sealed class RotationSourceSeamTests
{
    // PLATES4: MovingPlateId TimeMa PoleLatDeg PoleLonDeg AngleDeg FixedPlateId
    private const string FourPlateRotText = @"
1 0.0 90.0 0.0 0.0 0
1 4.0 90.0 0.0 6.0 0
1 8.0 90.0 0.0 12.0 0
2 0.0 0.0 90.0 0.0 0
2 4.0 0.0 90.0 10.0 0
2 8.0 0.0 90.0 20.0 0
3 0.0 -45.0 180.0 0.0 0
3 4.0 -45.0 180.0 -8.0 0
3 8.0 -45.0 180.0 -16.0 0
4 0.0 45.0 90.0 0.0 0
4 4.0 45.0 90.0 14.0 0
4 8.0 45.0 90.0 28.0 0
";

    private const string IdentityRosterRotText = @"
1 0.0 90.0 0.0 0.0 0
1 1000.0 90.0 0.0 0.0 0
2 0.0 90.0 0.0 0.0 0
2 1000.0 90.0 0.0 0.0 0
3 0.0 90.0 0.0 0.0 0
3 1000.0 90.0 0.0 0.0 0
4 0.0 90.0 0.0 0.0 0
4 1000.0 90.0 0.0 0.0 0
5 0.0 90.0 0.0 0.0 0
5 1000.0 90.0 0.0 0.0 0
6 0.0 90.0 0.0 0.0 0
6 1000.0 90.0 0.0 0.0 0
7 0.0 90.0 0.0 0.0 0
7 1000.0 90.0 0.0 0.0 0
8 0.0 90.0 0.0 0.0 0
8 1000.0 90.0 0.0 0.0 0
9 0.0 90.0 0.0 0.0 0
9 1000.0 90.0 0.0 0.0 0
";

    [Fact]
    public void ReadRotationSourceRecipe_absent_payload_yields_default_generated()
    {
        var recipe = WorldCrustRunSpec.ReadRotationSourceRecipe(new JsonObject());

        Assert.Equal(RotationSourceKind.Generated, recipe.Kind);
        Assert.Null(recipe.RotText);
    }

    [Fact]
    public void ReadRotationSourceRecipe_explicit_generated_kind_yields_default()
    {
        var payload = new JsonObject { ["rotationSource"] = new JsonObject { ["kind"] = "generated" } };

        var recipe = WorldCrustRunSpec.ReadRotationSourceRecipe(payload);

        Assert.Equal(RotationSourceKind.Generated, recipe.Kind);
    }

    [Fact]
    public void ReadRotationSourceRecipe_imported_kind_with_payload_selects_parser_path()
    {
        var payload = new JsonObject
        {
            ["rotationSource"] = new JsonObject
            {
                ["kind"] = "imported",
                ["payload"] = FourPlateRotText,
                ["name"] = "four-plate-fixture",
            },
        };

        var recipe = WorldCrustRunSpec.ReadRotationSourceRecipe(payload);

        Assert.Equal(RotationSourceKind.Imported, recipe.Kind);
        Assert.Equal(FourPlateRotText, recipe.RotText);
        Assert.Equal("four-plate-fixture", recipe.SourceName);
    }

    [Theory]
    [InlineData("imported", false)]
    [InlineData("imported", true)]
    public void ReadRotationSourceRecipe_imported_without_payload_throws_clear_error(string kind, bool emptyPayload)
    {
        var rs = new JsonObject { ["kind"] = kind };
        if (emptyPayload)
            rs["payload"] = "   ";

        var payload = new JsonObject { ["rotationSource"] = rs };

        var ex = Assert.Throws<ArgumentException>(() =>
            WorldCrustRunSpec.ReadRotationSourceRecipe(payload));

        Assert.Contains("payload", ex.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReadRotationSourceRecipe_unknown_kind_throws_clear_error()
    {
        var payload = new JsonObject
        {
            ["rotationSource"] = new JsonObject { ["kind"] = "fabricated" },
        };

        var ex = Assert.Throws<ArgumentException>(() =>
            WorldCrustRunSpec.ReadRotationSourceRecipe(payload));

        Assert.Contains("fabricated", ex.Message ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("generated", ex.Message ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void FromExecutionPayload_carries_rotation_source_through_to_spec()
    {
        var payload = new JsonObject
        {
            ["options"] = new JsonObject
            {
                ["rotationSource"] = new JsonObject
                {
                    ["kind"] = "imported",
                    ["payload"] = FourPlateRotText,
                },
            },
        };

        var spec = WorldCrustRunSpec.FromExecutionPayload(payload);

        Assert.NotNull(spec.RotationSource);
        Assert.Equal(RotationSourceKind.Imported, spec.RotationSource!.Kind);
    }

    [Fact]
    public void Imported_graph_request_with_malformed_source_fails_before_generation_append()
    {
        using var service = new Service(new ServiceRegistry());
        var request = new WorldGenerationRequest(
            WorldId: "bad-import-world",
            GenerationSpec: "world.generate",
            Parameters: new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["source"] = "world-generation.graph",
                ["rotationSourceKind"] = "imported",
                ["rotationSourcePayload"] = "1 0 90 0",
                ["rotationSourceName"] = "bad.rot",
            });

        var error = Assert.Throws<ArgumentException>(() => service.RunGenerationAsync(request));

        Assert.Contains("parse issue", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(service.GetOverviewAsync().IsDirty);
    }

    [Fact]
    public void Imported_graph_request_without_payload_fails_instead_of_using_generated_fallback()
    {
        using var service = new Service(new ServiceRegistry());
        var request = new WorldGenerationRequest(
            WorldId: "missing-import-world",
            GenerationSpec: "world.generate",
            Parameters: new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["source"] = "world-generation.graph",
                ["rotationSourceKind"] = "imported",
            });

        var error = Assert.Throws<ArgumentException>(() => service.RunGenerationAsync(request));

        Assert.Contains("rotationSourcePayload", error.Message, StringComparison.Ordinal);
        Assert.False(service.GetOverviewAsync().IsDirty);
    }

    [Fact]
    public void Valid_imported_graph_request_commits_before_generation()
    {
        using var service = new Service(new ServiceRegistry());
        var request = new WorldGenerationRequest(
            WorldId: "valid-import-world",
            GenerationSpec: "world.generate",
            Parameters: new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["source"] = "world-generation.graph",
                ["rotationSourceKind"] = "imported",
                ["rotationSourcePayload"] = FourPlateRotText,
                ["rotationSourceName"] = "four-plate-fixture.rot",
            });

        var result = service.RunGenerationAsync(request);

        Assert.True(result.Success);
        Assert.True(service.GetOverviewAsync().IsDirty);
    }

    [Theory]
    [InlineData("gplate")]
    [InlineData("import")]
    [InlineData("fabricated")]
    public void Unknown_rotationSourceKind_fails_closed_with_diagnostic_naming_kind_and_accepted(string kind)
    {
        // Defect 1 (fail-open): an unrecognized non-empty rotationSourceKind must throw, not
        // silently select the generated default (mirrors WorldCrustRunSpec.ReadRotationSourceRecipe).
        using var service = new Service(new ServiceRegistry());
        var request = new WorldGenerationRequest(
            WorldId: "unknown-kind-world",
            GenerationSpec: "world.generate",
            Parameters: new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["source"] = "world-generation.graph",
                ["rotationSourceKind"] = kind,
            });

        var error = Assert.Throws<ArgumentException>(() => service.RunGenerationAsync(request));

        Assert.Contains(kind, error.Message ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("imported", error.Message ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("generated", error.Message ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_rotationSourceKind_appends_no_generated_marker_and_leaves_prior_authority_active()
    {
        // Defect 1 (durable consequence): the throw must precede the durable generated-selection
        // CAS-append, so a bound imported authority stays active. Proven via visible topology: the
        // identity roster stays onset-identical at the evolved tick; a flip to generated would drift.
        long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        const long evolvedTick = 200_000_000L;
        using var service = new Service(new ServiceRegistry());

        var generatedAssignments = Assert.IsType<WorldGlobeSnapshot>(
                service.GetPlanetPresentationAsync(evolvedTick, tessellationFrequency: 2).GlobeSnapshot)
            .Cells.Select(cell => cell.PlateId).ToArray();

        var importResult = service.RunGenerationAsync(new WorldGenerationRequest(
            WorldId: "prior-import-world",
            GenerationSpec: "world.generate",
            Parameters: new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["source"] = "world-generation.graph",
                ["rotationSourceKind"] = "imported",
                ["rotationSourcePayload"] = IdentityRosterRotText,
                ["rotationSourceName"] = "identity-roster.rot",
            }));
        Assert.True(importResult.Success);

        var importedAssignments = Assert.IsType<WorldGlobeSnapshot>(
                service.GetPlanetPresentationAsync(evolvedTick, tessellationFrequency: 2).GlobeSnapshot)
            .Cells.Select(cell => cell.PlateId).ToArray();
        Assert.False(generatedAssignments.SequenceEqual(importedAssignments));

        var bad = Assert.Throws<ArgumentException>(() => service.RunGenerationAsync(new WorldGenerationRequest(
            WorldId: "prior-import-world",
            GenerationSpec: "world.generate",
            Parameters: new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["source"] = "world-generation.graph",
                ["rotationSourceKind"] = "gplate",
            })));
        Assert.Contains("gplate", bad.Message ?? string.Empty, StringComparison.Ordinal);

        var stillActiveAssignments = Assert.IsType<WorldGlobeSnapshot>(
                service.GetPlanetPresentationAsync(evolvedTick, tessellationFrequency: 2).GlobeSnapshot)
            .Cells.Select(cell => cell.PlateId).ToArray();
        Assert.Equal(importedAssignments, stillActiveAssignments);
        Assert.False(generatedAssignments.SequenceEqual(stillActiveAssignments));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Absent_or_empty_rotationSourceKind_selects_generated_default(string? kind)
    {
        // Defect 1 (preserve real default): absent/empty kind is the genuine generated-default path
        // and must not break.
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["source"] = "world-generation.graph",
        };
        if (kind is not null)
            parameters["rotationSourceKind"] = kind;

        using var service = new Service(new ServiceRegistry());
        var request = new WorldGenerationRequest(
            WorldId: "default-kind-world",
            GenerationSpec: "world.generate",
            Parameters: parameters);

        var result = service.RunGenerationAsync(request);

        Assert.True(result.Success);
        Assert.True(service.GetOverviewAsync().IsDirty);
    }

    [Fact]
    public void CommittedImportedAuthority_DrivesVisibleTopologyAndOwnsADistinctCrustCacheEntry()
    {
        long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        const long evolvedTick = 200_000_000L;
        using var service = new Service(new ServiceRegistry());

        var onset = service.GetPlanetPresentationAsync(onsetTick, tessellationFrequency: 2);
        var generated = service.GetPlanetPresentationAsync(evolvedTick, tessellationFrequency: 2);
        int generatedBuildCount = service.CrustPipelineBuildCountForTests;

        var result = service.RunGenerationAsync(new WorldGenerationRequest(
            WorldId: "canonical-import-world",
            GenerationSpec: "world.generate",
            Parameters: new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["source"] = "world-generation.graph",
                ["rotationSourceKind"] = "imported",
                ["rotationSourcePayload"] = IdentityRosterRotText,
                ["rotationSourceName"] = "identity-roster.rot",
                ["graphRevision"] = 0,
            }));
        var imported = service.GetPlanetPresentationAsync(evolvedTick, tessellationFrequency: 2);

        Assert.True(result.Success);
        var onsetAssignments = Assert.IsType<WorldGlobeSnapshot>(onset.GlobeSnapshot).Cells.Select(cell => cell.PlateId).ToArray();
        var generatedAssignments = Assert.IsType<WorldGlobeSnapshot>(generated.GlobeSnapshot).Cells.Select(cell => cell.PlateId).ToArray();
        var importedAssignments = Assert.IsType<WorldGlobeSnapshot>(imported.GlobeSnapshot).Cells.Select(cell => cell.PlateId).ToArray();
        Assert.False(onsetAssignments.SequenceEqual(generatedAssignments));
        Assert.Equal(onsetAssignments, importedAssignments);

        var generatedArcs = BoundarySignatures(generated.BoundaryArcs);
        var importedArcs = BoundarySignatures(imported.BoundaryArcs);
        Assert.False(generatedArcs.SequenceEqual(importedArcs));
        Assert.NotNull(generated.CellElevations);
        Assert.NotNull(imported.CellElevations);
        Assert.False(generated.CellElevations!.SequenceEqual(imported.CellElevations!));
        Assert.Equal(generatedBuildCount + 1, service.CrustPipelineBuildCountForTests);
    }

    [Fact]
    public void GeneratedProvider_GlobeBoundaryKindsMatchEulerianOwnershipOnFixedWorldCells()
    {
        const int seed = 7;
        const int frequency = 2;
        long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        long tick = onsetTick + 100_000_000L;
        var roster = OnsetRoster.Build(seed, onsetTick, frequency);
        var schedule = SphereRegimeScheduleDefaults.GeosphereFor(onsetTick);
        var plates = roster.SeedPlatesAt(onsetTick);
        var provider = new GeneratedEulerPoleRotationProvider(plates, onsetTick);
        var reconstructor = GlobeReconstructor.FromOnsetRoster(
            roster,
            onsetTick,
            schedule,
            frequency,
            provider);

        var globe = reconstructor.BuildGlobeAt(tick);
        var tessellation = new GeodesicSphereTessellation(frequency);
        var reassignedTopology = new PlateTopology(
            globe.Cells.ToDictionary(cell => cell.CellId, cell => cell.PlateId),
            Array.Empty<Boundary>(),
            Array.Empty<Junction>());
        var fixedWorldCenters = Enumerable.Range(0, tessellation.CellCount)
            .ToDictionary(
                cell => cell,
                cell => tessellation.GetCenter(new GeodesicCoord(cell, frequency)).ToVector3D());
        var currentPoles = plates.ToDictionary(
            plate => plate.PlateId,
            plate => provider.InstantaneousPoleAt(plate.PlateId, tick));

        var expected = PlateTopologyBuilder.ClassifyBoundariesFromKinematics(
                tessellation,
                reassignedTopology,
                fixedWorldCenters,
                currentPoles)
            .Select(boundary => (boundary.PlateA, boundary.PlateB, Kind: BoundaryArcSampler.MapKind(boundary.Type)))
            .OrderBy(boundary => boundary.PlateA)
            .ThenBy(boundary => boundary.PlateB)
            .ToArray();
        var actual = reconstructor.BuildBoundaryArcsAt(tick)
            .Select(arc => (arc.PlateA, arc.PlateB, arc.Kind))
            .Distinct()
            .OrderBy(boundary => boundary.PlateA)
            .ThenBy(boundary => boundary.PlateB)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IdentityProvider_KeepsEulerianGlobeAtOnsetAcrossTicks()
    {
        const int seed = 7;
        const int frequency = 2;
        long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        long tick = onsetTick + 100_000_000L;
        var roster = OnsetRoster.Build(seed, onsetTick, frequency);
        var reconstructor = GlobeReconstructor.FromOnsetRoster(
            roster,
            onsetTick,
            SphereRegimeScheduleDefaults.GeosphereFor(onsetTick),
            frequency,
            new IdentityRotationProvider());

        var onset = reconstructor.BuildGlobeAt(onsetTick);
        var evolved = reconstructor.BuildGlobeAt(tick);

        Assert.Equal(
            onset.Cells.Select(cell => cell.PlateId),
            evolved.Cells.Select(cell => cell.PlateId));
        Assert.Equal(
            BoundarySignatures(reconstructor.BuildBoundaryArcsAt(onsetTick)),
            BoundarySignatures(reconstructor.BuildBoundaryArcsAt(tick)));
    }

    [Fact]
    public async Task ReconstructedCoordinator_ReplaysSameBoundAuthorityAndExactCrustDeposition()
    {
        long onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        const long evolvedTick = 200_000_000L;
        var options = WorldGenerationRenderOptions.Default with { TessellationFrequency = 2 };
        var generated = await WorldCrustMaterializer.MaterializeAsync(
            WorldCrustRunSpec.ForPresentation(options, onsetTick, evolvedTick));
        var generatedSignature = CrustDepositionSignature(generated, evolvedTick);

        var store = new InMemoryTruthEventStore();
        string firstDigest;
        string[] firstImportedSignature;
        using (var firstWriter = new DirectTruthEventWriter(store))
        using (var first = new WorldHistoryCoordinator(new ServiceRegistry(), store, firstWriter))
        {
            first.ImportRotationSource(
                "canonical-reload-world",
                "main",
                new RotationSourceRecipe(
                    RotationSourceKind.Imported,
                    IdentityRosterRotText,
                    "identity-roster.rot"),
                onsetTick);
            var projection = first.GetActiveRotationProjection(onsetTick);
            Assert.NotNull(projection.Provider);
            firstDigest = projection.Authority.Digest;
            var imported = await WorldCrustMaterializer.MaterializeAsync(
                WorldCrustRunSpec.ForPresentation(options, onsetTick, evolvedTick, projection.Provider));
            firstImportedSignature = CrustDepositionSignature(imported, evolvedTick);
        }

        using var reloadWriter = new DirectTruthEventWriter(store);
        using var reloaded = new WorldHistoryCoordinator(new ServiceRegistry(), store, reloadWriter);
        var reloadedProjection = reloaded.GetActiveRotationProjection(onsetTick);
        Assert.NotNull(reloadedProjection.Provider);
        var reloadedImported = await WorldCrustMaterializer.MaterializeAsync(
            WorldCrustRunSpec.ForPresentation(options, onsetTick, evolvedTick, reloadedProjection.Provider));
        var reloadedSignature = CrustDepositionSignature(reloadedImported, evolvedTick);

        Assert.StartsWith("imported:v1:", firstDigest, StringComparison.Ordinal);
        Assert.Equal(firstDigest, reloadedProjection.Authority.Digest);
        Assert.False(generatedSignature.SequenceEqual(firstImportedSignature));
        Assert.Equal(firstImportedSignature, reloadedSignature);
    }

    [Fact]
    public void Production_service_uses_coordinator_materialization_without_direct_rot_reparse()
    {
        var serviceSource = File.ReadAllText(ProjectFile(
            "project/plugins/App.World/Services/Service.cs"));
        var coordinatorSource = File.ReadAllText(ProjectFile(
            "project/plugins/App.World/History/RotationImportCoordinator.cs"));

        Assert.DoesNotContain("new ImportedRotationProvider", serviceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new RotParser", serviceSource, StringComparison.Ordinal);
        Assert.Contains("RotationModelMaterializer.MaterializeAsync", coordinatorSource, StringComparison.Ordinal);
        Assert.Contains("state.Payload.PlateCursor", coordinatorSource, StringComparison.Ordinal);
    }

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

    private static string[] BoundarySignatures(IReadOnlyList<PlateBoundaryArc>? arcs)
        => (arcs ?? Array.Empty<PlateBoundaryArc>())
            .Select(arc =>
            {
                var first = arc.Points[0];
                var last = arc.Points[^1];
                return FormattableString.Invariant(
                    $"{Math.Min(arc.PlateA, arc.PlateB)}:{Math.Max(arc.PlateA, arc.PlateB)}:{arc.Kind}:{first.X:R},{first.Y:R},{first.Z:R}:{last.X:R},{last.Y:R},{last.Z:R}");
            })
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToArray();

    private static string[] CrustDepositionSignature(WorldCrustMaterialization materialization, long tick)
    {
        var state = materialization.Result.StateByTick[tick];
        var features = materialization.Result.FeaturesByTick[tick];
        return state.Values
            .OrderBy(cell => cell.CellId)
            .Select(cell =>
            {
                features.TryGetValue(cell.CellId, out var feature);
                return FormattableString.Invariant(
                    $"{cell.CellId}:{cell.OrogenicPressure:R}:{cell.VolcanicActivity:R}:{feature.Kind}:{feature.Magnitude:R}");
            })
            .ToArray();
    }

    private sealed class IdentityRotationProvider : IPlateRotationProvider
    {
        public Quaternion RotationFromOnsetTo(int plateId, long tick) => Quaternion.Identity;

        public EulerPole InstantaneousPoleAt(int plateId, long tick)
            => new(new Vector3D(0.0, 0.0, 1.0), 0.0);
    }
}
