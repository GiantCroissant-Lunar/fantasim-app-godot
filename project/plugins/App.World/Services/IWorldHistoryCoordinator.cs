using FantaSim.App.World.Dto;
using FantaSim.App.World.Crust;
using ServiceArchi.Contracts;

namespace FantaSim.App.World.Services;

/// <summary>
/// Internal seam between the <see cref="Service"/> shell and the world-lib composition. The
/// coordinator owns the field catalog, reducer registry, and truth-stream appends that back the
/// app-side DTOs; world-lib types never cross this boundary.
/// </summary>
internal interface IWorldHistoryCoordinator : IDisposable
{
    WorldOverview GetOverview();
    WorldFieldValues GetFieldValues(WorldFieldValuesRequest request);
    WorldScalarFieldValues GetScalarFieldValues(WorldScalarFieldValuesRequest request);
    WorldRenderSnapshot GetRenderSnapshot();
    WorldGenerationResult RunGeneration(WorldGenerationRequest request);
    void UseGeneratedRotationSource();
    void ImportRotationSource(
        string worldId,
        string branchId,
        RotationSourceRecipe recipe,
        long onsetTick);
    IPlateRotationProvider? GetActiveRotationProvider(long onsetTick);
    RotationAuthorityIdentity GetActiveRotationAuthority();
    RotationAuthorityProjection GetActiveRotationProjection(long onsetTick);
}

/// <summary>
/// App-owned cache identity for the canonical rotation authority. Imported identities are derived
/// from the exact committed bound cursor; generated authority has one stable identity.
/// </summary>
internal readonly record struct RotationAuthorityIdentity(string Digest)
{
    public static RotationAuthorityIdentity Generated { get; } = new("generated:v1");
}

internal readonly record struct RotationAuthorityProjection(
    RotationAuthorityIdentity Authority,
    IPlateRotationProvider? Provider);

/// <summary>
/// Selects the <see cref="IWorldHistoryCoordinator"/> implementation at construction. The real
/// <see cref="WorldHistoryCoordinator"/> composes the sibling fantasim-world field/catalog/reducer/
/// truth-stream primitives unconditionally; package and project-reference modes run the same
/// coordinator and test suite.
/// </summary>
internal static class WorldHistoryCoordinatorFactory
{
    public static IWorldHistoryCoordinator Create(
        IRegistry registry,
        FantaSim.World.TruthStream.ITruthEventReader truthReader,
        ITruthEventWriter truthWriter)
        => new WorldHistoryCoordinator(registry, descriptors: null, truthReader, truthWriter);
}
