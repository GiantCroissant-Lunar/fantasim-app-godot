using FantaSim.App.World.Dto;

namespace FantaSim.App.World.Services;

/// <summary>
/// No-op <see cref="IWorldRuntime"/> used when <c>UseProjectReferences</c> is false. The local
/// lunar-horse feed does not yet carry FantaSim.World.* packages, so the default build compiles
/// this stub to keep <c>dotnet build project/FantaSim.sln</c> green without the sibling repo.
/// All methods report an empty/zero world; <see cref="RunGeneration"/> returns a synthetic
/// success so the service surface is exercisable in tests that do not depend on world-lib.
/// </summary>
internal sealed class StubWorldRuntime : IWorldRuntime
{
    public WorldOverview GetOverview()
        => new(WorldId: "stub", Name: "StubWorld", EntityCount: 0, FieldCount: 0, IsDirty: false);

    public WorldFieldValues GetFieldValues(WorldFieldValuesRequest request)
        => new(FieldValues: new Dictionary<string, object>(0));

    public WorldScalarFieldValues GetScalarFieldValues(WorldScalarFieldValuesRequest request)
        => new(ScalarValues: new Dictionary<string, float>(0));

    public WorldRenderSnapshot GetRenderSnapshot()
        => new(FrameIndex: 0, Entities: Array.Empty<RenderEntityDto>());

    public WorldGenerationResult RunGeneration(WorldGenerationRequest request)
        => new(Success: true, Message: "stub", ResultWorldId: request.WorldId);

    public void Dispose() { }
}