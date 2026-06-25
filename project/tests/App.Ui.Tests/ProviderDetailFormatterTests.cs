using Xunit;
using FantaSim.App.NodeGraph;

namespace FantaSim.App.Ui.Tests;

public sealed class ProviderDetailFormatterTests
{
    [Fact]
    public void Format_IiiVplanetMetadata_ProducesExpectedLines()
    {
        var metadata = new FunctionProviderMetadata(
            ProviderKind: FunctionProviderKinds.Iii,
            ProviderId: "vplanet-worker",
            RuntimeRequirement: "python3:vplanet>=2.5",
            Determinism: "versioned",
            TrustLevel: "external-service"
        );

        var traits = new FunctionExecutionTraits(
            RequiresExternalProcess: true,
            SupportsCancellation: true,
            DefaultTimeoutSeconds: 300,
            ArtifactShape: "vplanet/run-result",
            CommitEligibility: "adapter-gated"
        );

        var lines = FunctionProviderDetailFormatter.Format(metadata, traits);

        Assert.Equal(4, lines.Count);
        Assert.Equal("provider: iii / vplanet-worker", lines[0]);
        Assert.Equal("runtime: python3:vplanet>=2.5", lines[1]);
        Assert.Equal("traits: external-process, cancellable, timeout 300s", lines[2]);
        Assert.Equal("artifact: vplanet/run-result", lines[3]);
    }

    [Fact]
    public void Format_NativeCSharpMetadata_ProducesCompactProviderLine()
    {
        var metadata = new FunctionProviderMetadata(
            ProviderKind: FunctionProviderKinds.CSharp
        );

        var traits = new FunctionExecutionTraits(
            RequiresExternalProcess: false,
            RequiresNetwork: false,
            IsDeterministic: true
        );

        var lines = FunctionProviderDetailFormatter.Format(metadata, traits);

        var line = Assert.Single(lines);
        Assert.Equal("provider: csharp", line);
    }

    [Fact]
    public void Format_NoMetadataOrTraits_ProducesNoLines()
    {
        var lines = FunctionProviderDetailFormatter.Format(null, null);

        Assert.Empty(lines);
    }
}
