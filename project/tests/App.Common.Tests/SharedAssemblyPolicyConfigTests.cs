using System;
using FantaSim.App.Common;
using Xunit;

namespace FantaSim.App.Common.Tests;

public class SharedAssemblyPolicyConfigTests
{
    [Fact]
    public void ParsesExactMatchesAndPrefixes()
    {
        var config = SharedAssemblyPolicyConfig.ParseJson(
            """{"comment":"x","exactMatches":["MessagePack","Arch"],"prefixes":["System.","FantaSim.App."]}""");
        Assert.Equal(new[] { "MessagePack", "Arch" }, config.ExactMatches);
        Assert.Equal(new[] { "System.", "FantaSim.App." }, config.Prefixes);
    }

    [Fact]
    public void MissingPrefixesArrayThrows()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => SharedAssemblyPolicyConfig.ParseJson("""{"exactMatches":[]}"""));
        Assert.Contains("prefixes", ex.Message);
    }

    [Fact]
    public void EmptyJsonThrows()
        => Assert.Throws<InvalidOperationException>(() => SharedAssemblyPolicyConfig.ParseJson(" "));

    [Fact]
    public void NonStringEntryThrows()
        => Assert.Throws<InvalidOperationException>(
            () => SharedAssemblyPolicyConfig.ParseJson("""{"exactMatches":[1],"prefixes":[]}"""));
}
