using Akka.Actor;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Crust;
using FantaSim.App.World.Services;
using FantaSim.World.Contracts.Units;
using FantaSim.World.TruthStream;
using ServiceArchi.Core;
using UnifyMaths;
using Xunit;
using Xunit.Abstractions;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Opt-in process-boundary proof for the production SurrealDB truth-store path.
/// Run this same fact in two separate <c>dotnet test</c> processes, first with phase
/// <c>write</c> and then with phase <c>read</c>, against one externally hosted persistent
/// SurrealDB namespace/database. The normal suite deliberately does not require that service.
/// </summary>
public sealed class ExternalSurrealRotationRestartProofTests
{
    private const string PhaseEnvironmentVariable = "FANTASIM_ROTATION_RESTART_PROOF_PHASE";
    private const string ConnectionEnvironmentVariable =
        "FANTASIM_ROTATION_RESTART_PROOF_CONNECTION_STRING";
    private const string ReceiptEnvironmentVariable = "FANTASIM_ROTATION_RESTART_PROOF_RECEIPT";
    private const long OnsetTick = 42_000_000L;
    private const string WorldId = "durable-rotation-restart-proof";
    private const string BranchId = "main";
    // Minted via WorldStreamVocabulary so the proof reads the SAME streams production writes.
    // The previous hand-pinned L0 identities predated the L2 migration: every "stream empty"
    // precondition was checking a stream nothing writes to — vacuously true (found 2026-07-14).
    private static readonly TruthStreamIdentity SelectionStream =
        WorldStreamVocabulary.RotationSelection();
    private static readonly TruthStreamIdentity ControlStream =
        WorldStreamVocabulary.ImportsControl(WorldId, BranchId);
    private static readonly TruthStreamIdentity PlateStream =
        WorldStreamVocabulary.Plates(WorldId, BranchId);
    private const string RotationSource = """
        001 0 90 0 30 000
        001 4 90 0 36 000
        001 8 90 0 42 000
        002 0 0 90 0 000
        002 8 0 90 20 000
        """;

    private readonly ITestOutputHelper _output;

    public ExternalSurrealRotationRestartProofTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Imported_rotation_recovers_in_a_second_process_from_external_persistent_truth()
    {
        var phase = Environment.GetEnvironmentVariable(PhaseEnvironmentVariable)?.Trim();
        if (string.IsNullOrWhiteSpace(phase))
        {
            // xUnit.net v2 cannot report a runtime skip. Keep the normal suite dependency-free
            // while making the no-op explicit in detailed test output.
            _output.WriteLine(
                $"NO-OP: set {PhaseEnvironmentVariable}=write or read and "
                + $"{ConnectionEnvironmentVariable}=<external SurrealDB connection string> "
                + "to run the durable restart proof.");
            return;
        }

        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable)?.Trim();
        Assert.False(
            string.IsNullOrWhiteSpace(connectionString),
            $"{ConnectionEnvironmentVariable} is required when {PhaseEnvironmentVariable} is set.");
        Assert.DoesNotContain("mem://", connectionString, StringComparison.OrdinalIgnoreCase);

        var normalizedPhase = phase.ToLowerInvariant();
        if (normalizedPhase is not ("write" or "read"))
        {
            Assert.Fail(
                $"Unsupported {PhaseEnvironmentVariable} value '{phase}'. Expected 'write' or 'read'.");
        }

        using var storeHandle = WorldTruthEventStoreFactory.Create(new WorldTruthStoreOptions(
            WorldTruthStoreBackend.SurrealDb,
            connectionString));
        if (normalizedPhase == "write")
            await AssertInitialStreamsAreAbsentAsync(storeHandle.EventStore);

        var actorSystem = ActorSystem.Create($"rotation-restart-proof-{phase}-{Guid.NewGuid():N}");
        try
        {
            using var writer = ActorTruthEventWriter.Start(
                actorSystem,
                storeHandle.EventStore,
                actorName: $"truth-writer-{Guid.NewGuid():N}",
                askTimeout: TimeSpan.FromSeconds(30));
            using var history = new WorldHistoryCoordinator(
                new ServiceRegistry(),
                storeHandle.EventStore,
                writer);

            switch (normalizedPhase)
            {
                case "write":
                    history.ImportRotationSource(
                        WorldId,
                        BranchId,
                        new RotationSourceRecipe(
                            RotationSourceKind.Imported,
                            RotationSource,
                            "durable-rotation-restart-proof.rot"),
                        OnsetTick);
                    AssertRecoveredProvider(history);
                    _output.WriteLine("WRITE: canonical imported rotation and active selection committed.");
                    break;

                case "read":
                    // WorldHistoryCoordinator cold-restores the app-owned active selection in its
                    // constructor, then verifies and materializes the exact referenced event prefix.
                    AssertRecoveredProvider(history);
                    _output.WriteLine("READ: cold coordinator recovered the committed provider.");
                    break;
            }

            await WritePhaseReceiptAsync(normalizedPhase);
        }
        finally
        {
            await actorSystem.Terminate();
        }
    }

    private static async Task AssertInitialStreamsAreAbsentAsync(ITruthEventReader reader)
    {
        Assert.Null(await reader.GetHeadAsync(SelectionStream));
        Assert.Null(await reader.GetHeadAsync(ControlStream));
        Assert.Null(await reader.GetHeadAsync(PlateStream));
    }

    private static async Task WritePhaseReceiptAsync(string phase)
    {
        var path = Environment.GetEnvironmentVariable(ReceiptEnvironmentVariable)?.Trim();
        if (string.IsNullOrWhiteSpace(path))
            return;

        await File.WriteAllTextAsync(path, phase + Environment.NewLine);
    }

    private static void AssertRecoveredProvider(WorldHistoryCoordinator history)
    {
        var provider = Service.BuildRotationProvider(
            history.GetActiveRotationProjection(OnsetTick),
            Array.Empty<FantaSim.Geosphere.Plate.Topology.Plate>(),
            OnsetTick);

        var materialized = Assert.IsType<MaterializedRotationProvider>(provider);
        Assert.Equal(new[] { 1, 2 }, materialized.ServedPlateIds.OrderBy(id => id));
        AssertRotationOfUnitX(
            materialized.RotationFromOnsetTo(
                plateId: 1,
                tick: OnsetTick + UnitConverter.MegaAnnumToTickDelta(2.0)),
            expectedDegrees: 3.0);
    }

    private static void AssertRotationOfUnitX(Quaternion rotation, double expectedDegrees)
    {
        var actual = rotation.Rotate(new Vector3D(1.0, 0.0, 0.0));
        var radians = expectedDegrees * Math.PI / 180.0;
        Assert.InRange(actual.X, Math.Cos(radians) - 1e-9, Math.Cos(radians) + 1e-9);
        Assert.InRange(actual.Y, Math.Sin(radians) - 1e-9, Math.Sin(radians) + 1e-9);
        Assert.InRange(actual.Z, -1e-9, 1e-9);
    }
}
