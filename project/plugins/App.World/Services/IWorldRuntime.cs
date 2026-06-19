using FantaSim.App.World.Dto;
using ServiceArchi.Contracts;

namespace FantaSim.App.World.Services;

/// <summary>
/// Internal seam between the <see cref="Service"/> shell and the world-lib composition.
/// Keeping this interface in the always-compiled file lets <see cref="Service"/> stay
/// buildable whether or not the sibling fantasim-world projects are wired in.
/// </summary>
internal interface IWorldRuntime : IDisposable
{
    WorldOverview GetOverview();
    WorldFieldValues GetFieldValues(WorldFieldValuesRequest request);
    WorldScalarFieldValues GetScalarFieldValues(WorldScalarFieldValuesRequest request);
    WorldRenderSnapshot GetRenderSnapshot();
    WorldGenerationResult RunGeneration(WorldGenerationRequest request);
}

/// <summary>
/// Selects the <see cref="IWorldRuntime"/> implementation at compile time. The real
/// <see cref="WorldRuntime"/> (composing fantasim-world field/catalog/reducer/truth-stream
/// primitives) is compiled only when <c>USE_PROJECT_REFERENCES</c> is defined; otherwise a
/// minimal stub keeps the solution green without the sibling repo.
/// </summary>
internal static class WorldRuntimeFactory
{
    public static IWorldRuntime Create(IRegistry registry)
    {
#if USE_PROJECT_REFERENCES
        return new WorldRuntime(registry);
#else
        return new StubWorldRuntime();
#endif
    }
}