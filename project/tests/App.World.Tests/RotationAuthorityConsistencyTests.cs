using FantaSim.App.World.Crust;
using FantaSim.App.World.Dto;
using FantaSim.App.World.Services;
using FantaSim.Geosphere.Plate.Topology;
using FantaSim.World.TruthStream;
using ServiceArchi.Contracts;
using ServiceArchi.Core;
using UnifyMaths;
using Xunit;

namespace FantaSim.App.World.Tests;

public sealed class RotationAuthorityConsistencyTests
{
    [Fact]
    public async Task Concurrent_selection_change_cannot_mix_authorities_or_cache_entries()
    {
        const long evolvedTick = 200_000_000L;
        const int frequency = 2;
        PlanetPresentationDocument expectedGenerated;
        using (var baseline = new Service(new ServiceRegistry()))
            expectedGenerated = baseline.GetPlanetPresentationAsync(evolvedTick, frequency);

        BlockingHistoryCoordinator? blocking = null;
        using var service = new Service(
            new ServiceRegistry(),
            actorSystem: null,
            historyFactory: (registry, reader, writer) =>
            {
                var inner = WorldHistoryCoordinatorFactory.Create(registry, reader, writer);
                blocking = new BlockingHistoryCoordinator(inner, new IdentityRotationProvider());
                return blocking;
            });

        var inFlight = Task.Run(() => service.GetPlanetPresentationAsync(evolvedTick, frequency));
        Assert.NotNull(blocking);
        await blocking!.FirstProjectionCaptured.Task.WaitAsync(TimeSpan.FromSeconds(30));
        blocking.SelectImported();
        blocking.ReleaseFirstProjection();
        var oldDocument = await inFlight.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(1, blocking.ProjectionReadCount);
        Assert.Equal(Assignments(expectedGenerated), Assignments(oldDocument));
        Assert.Equal(expectedGenerated.CellElevations, oldDocument.CellElevations);
        Assert.Equal(1, service.CrustPipelineBuildCountForTests);

        var newDocument = service.GetPlanetPresentationAsync(evolvedTick, frequency);
        Assert.Equal(2, service.CrustPipelineBuildCountForTests);
        var newOnset = service.GetPlanetPresentationAsync(
            FantaSim.App.World.Composition.SphereRegimeScheduleDefaults.PlateOnsetTick,
            frequency);

        Assert.Equal(3, blocking.ProjectionReadCount);
        Assert.Equal(3, service.CrustPipelineBuildCountForTests);
        Assert.Equal(Assignments(newOnset), Assignments(newDocument));
        Assert.False(oldDocument.CellElevations!.SequenceEqual(newDocument.CellElevations!));
    }

    [Fact]
    public void Projection_type_rejects_mixed_authority_provider_combinations()
    {
        Assert.Throws<InvalidOperationException>(() => new RotationAuthorityProjection(
            RotationAuthorityIdentity.Generated,
            new IdentityRotationProvider()));
        Assert.Throws<InvalidOperationException>(() => new RotationAuthorityProjection(
            new RotationAuthorityIdentity("imported:test"),
            provider: null));
    }

    [Fact]
    public void Public_document_captures_one_rotation_projection_for_all_build_phases()
    {
        const long evolvedTick = 200_000_000L;
        const int frequency = 2;
        PlanetPresentationDocument expectedGenerated;
        using (var baseline = new Service(new ServiceRegistry()))
            expectedGenerated = baseline.GetPlanetPresentationAsync(evolvedTick, frequency);

        SwitchingHistoryCoordinator? switching = null;
        using var service = new Service(
            new ServiceRegistry(),
            actorSystem: null,
            historyFactory: (registry, reader, writer) =>
            {
                var inner = WorldHistoryCoordinatorFactory.Create(registry, reader, writer);
                switching = new SwitchingHistoryCoordinator(inner, new IdentityRotationProvider());
                return switching;
            });

        // The history seam switches from generated to imported immediately after the first
        // projection read. A document build must retain that first captured projection through
        // globe reconstruction, crust key/materialization, and per-tick material sampling.
        var first = service.GetPlanetPresentationAsync(evolvedTick, frequency);

        Assert.NotNull(switching);
        Assert.Equal(1, switching!.ProjectionReadCount);
        Assert.Equal(new[] { "generated:v1" }, switching.ProjectionDigests);
        Assert.Equal(Assignments(expectedGenerated), Assignments(first));
        Assert.Equal(BoundarySignatures(expectedGenerated), BoundarySignatures(first));
        Assert.Equal(expectedGenerated.CellElevations, first.CellElevations);
        Assert.Equal(expectedGenerated.CellFeatures, first.CellFeatures);

        // The following public build captures the now-active imported identity and uses it
        // consistently. This proves the first document did not merely cache a generated globe
        // while mixing imported crust into the same result.
        var imported = service.GetPlanetPresentationAsync(evolvedTick, frequency);
        var importedOnset = service.GetPlanetPresentationAsync(
            FantaSim.App.World.Composition.SphereRegimeScheduleDefaults.PlateOnsetTick,
            frequency);

        Assert.Equal(3, switching.ProjectionReadCount);
        Assert.Equal("imported:test-identity", switching.ProjectionDigests[1]);
        Assert.Equal("imported:test-identity", switching.ProjectionDigests[2]);
        Assert.Equal(Assignments(importedOnset), Assignments(imported));
        Assert.False(first.CellElevations!.SequenceEqual(imported.CellElevations!));
    }

    private static int[] Assignments(PlanetPresentationDocument document)
        => document.GlobeSnapshot!.Cells.Select(cell => cell.PlateId).ToArray();

    private static string[] BoundarySignatures(PlanetPresentationDocument document)
        => (document.BoundaryArcs ?? Array.Empty<PlateBoundaryArc>())
            .Select(arc => $"{arc.PlateA}:{arc.PlateB}:{arc.Kind}")
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToArray();

    private sealed class SwitchingHistoryCoordinator : IWorldHistoryCoordinator
    {
        private readonly IWorldHistoryCoordinator _inner;
        private readonly IPlateRotationProvider _importedProvider;

        public SwitchingHistoryCoordinator(
            IWorldHistoryCoordinator inner,
            IPlateRotationProvider importedProvider)
        {
            _inner = inner;
            _importedProvider = importedProvider;
        }

        public int ProjectionReadCount { get; private set; }
        public List<string> ProjectionDigests { get; } = new();

        public WorldOverview GetOverview() => _inner.GetOverview();
        public WorldFieldValues GetFieldValues(WorldFieldValuesRequest request) => _inner.GetFieldValues(request);
        public WorldScalarFieldValues GetScalarFieldValues(WorldScalarFieldValuesRequest request) => _inner.GetScalarFieldValues(request);
        public WorldRenderSnapshot GetRenderSnapshot() => _inner.GetRenderSnapshot();
        public WorldGenerationResult RunGeneration(WorldGenerationRequest request) => _inner.RunGeneration(request);
        public void UseGeneratedRotationSource() => _inner.UseGeneratedRotationSource();

        public void ImportRotationSource(
            string worldId,
            string branchId,
            RotationSourceRecipe recipe,
            long onsetTick)
            => _inner.ImportRotationSource(worldId, branchId, recipe, onsetTick);

        public RotationAuthorityProjection GetActiveRotationProjection(long onsetTick)
        {
            var projection = ProjectionReadCount++ == 0
                ? new RotationAuthorityProjection(RotationAuthorityIdentity.Generated, provider: null)
                : new RotationAuthorityProjection(
                    new RotationAuthorityIdentity("imported:test-identity"),
                    _importedProvider);
            ProjectionDigests.Add(projection.Authority.Digest);
            return projection;
        }

        public void Dispose() => _inner.Dispose();
    }

    private sealed class BlockingHistoryCoordinator : IWorldHistoryCoordinator
    {
        private readonly IWorldHistoryCoordinator _inner;
        private readonly IPlateRotationProvider _importedProvider;
        private readonly object _gate = new();
        private readonly TaskCompletionSource<bool> _releaseFirst =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _importedSelected;
        private bool _blockFirst = true;

        public BlockingHistoryCoordinator(
            IWorldHistoryCoordinator inner,
            IPlateRotationProvider importedProvider)
        {
            _inner = inner;
            _importedProvider = importedProvider;
        }

        public TaskCompletionSource<bool> FirstProjectionCaptured { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ProjectionReadCount { get; private set; }

        public void SelectImported()
        {
            lock (_gate)
                _importedSelected = true;
        }

        public void ReleaseFirstProjection() => _releaseFirst.TrySetResult(true);

        public WorldOverview GetOverview() => _inner.GetOverview();
        public WorldFieldValues GetFieldValues(WorldFieldValuesRequest request) => _inner.GetFieldValues(request);
        public WorldScalarFieldValues GetScalarFieldValues(WorldScalarFieldValuesRequest request) => _inner.GetScalarFieldValues(request);
        public WorldRenderSnapshot GetRenderSnapshot() => _inner.GetRenderSnapshot();
        public WorldGenerationResult RunGeneration(WorldGenerationRequest request) => _inner.RunGeneration(request);
        public void UseGeneratedRotationSource() => _inner.UseGeneratedRotationSource();

        public void ImportRotationSource(
            string worldId,
            string branchId,
            RotationSourceRecipe recipe,
            long onsetTick)
            => _inner.ImportRotationSource(worldId, branchId, recipe, onsetTick);

        public RotationAuthorityProjection GetActiveRotationProjection(long onsetTick)
        {
            RotationAuthorityProjection captured;
            bool shouldBlock;
            lock (_gate)
            {
                ProjectionReadCount++;
                captured = _importedSelected
                    ? new RotationAuthorityProjection(
                        new RotationAuthorityIdentity("imported:test-identity"),
                        _importedProvider)
                    : new RotationAuthorityProjection(RotationAuthorityIdentity.Generated, provider: null);
                shouldBlock = _blockFirst;
                _blockFirst = false;
            }

            if (shouldBlock)
            {
                FirstProjectionCaptured.TrySetResult(true);
                _releaseFirst.Task.GetAwaiter().GetResult();
            }

            return captured;
        }

        public void Dispose() => _inner.Dispose();
    }

    private sealed class IdentityRotationProvider : IPlateRotationProvider
    {
        public Quaternion RotationFromOnsetTo(int plateId, long tick) => Quaternion.Identity;

        public EulerPole InstantaneousPoleAt(int plateId, long tick)
            => new(new Vector3D(0.0, 0.0, 1.0), 0.0);
    }
}
