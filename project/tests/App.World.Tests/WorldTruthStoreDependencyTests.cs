#if USE_PROJECT_REFERENCES
using UnifyStorage.Abstractions;
using Xunit;

namespace FantaSim.App.World.Tests;

public sealed class WorldTruthStoreDependencyTests
{
    [Fact]
    public void ProjectReferenceBuild_LoadsUnifyStorageAbstractionsWithConditionalWriteContract()
    {
        var abstractionsAssembly = typeof(IKeyValueStore).Assembly;

        Assert.NotNull(abstractionsAssembly.GetType("UnifyStorage.Abstractions.KeyValueCondition"));
        Assert.NotNull(abstractionsAssembly.GetType("UnifyStorage.Abstractions.IConditionalKeyValueStore"));
    }
}
#endif
