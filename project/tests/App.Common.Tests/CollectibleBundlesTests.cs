using FantaSim.App.Common;
using Xunit;

namespace FantaSim.App.Common.Tests;

public class CollectibleBundlesTests
{
    [Fact]
    public void ProjectsFieldIsToleratedAndIgnored()
    {
        var bundles = CollectibleBundles.ParseJson(
            """
            {"bundles":[{"bundleId":"stage","pluginAssembly":"FantaSim.App.Stage.dll",
              "projects":[{"csproj":"a.csproj","output":"bin","assembly":"FantaSim.App.Stage"}]}]}
            """);
        Assert.True(bundles.ContainsAssembly("FantaSim.App.Stage"));
    }
}
