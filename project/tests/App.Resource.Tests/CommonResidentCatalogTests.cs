using System.Linq;
using FantaSim.App.Resource;
using Xunit;

namespace App.Resource.Tests;

public sealed class CommonResidentCatalogTests
{
    private const string ExpectedJson = """
    {
      "bundleId": "common",
      "assemblies": [
        { "assemblyName": "Arch", "sha256": "aa11" },
        { "assemblyName": "MessagePack", "sha256": "bb22" }
      ]
    }
    """;

    [Fact]
    public void ParseExpectedReadsIdentities()
    {
        var expected = CommonResidentCatalog.ParseExpected(ExpectedJson);
        Assert.Equal(2, expected.Count);
        Assert.Equal("Arch", expected[0].AssemblyName);
        Assert.Equal("aa11", expected[0].Sha256);
    }

    [Fact]
    public void ValidateAcceptsExactMatchAnyOrder()
    {
        var expected = CommonResidentCatalog.ParseExpected(ExpectedJson);
        var actual = expected.Reverse().ToList();
        Assert.Empty(CommonResidentCatalog.Validate(expected, actual));
    }

    [Fact]
    public void ValidateReportsMissingExtraAndHashMismatch()
    {
        var expected = CommonResidentCatalog.ParseExpected(ExpectedJson);
        var actual = new[]
        {
            new CommonAssemblyIdentity("Arch", "DIFFERENT"),
            new CommonAssemblyIdentity("Newtonsoft.Json", "cc33"),
        };
        var errors = CommonResidentCatalog.Validate(expected, actual);
        Assert.Contains(errors, e => e.Contains("hash mismatch") && e.Contains("Arch"));
        Assert.Contains(errors, e => e.Contains("missing") && e.Contains("MessagePack"));
        Assert.Contains(errors, e => e.Contains("unexpected") && e.Contains("Newtonsoft.Json"));
    }

    [Fact]
    public void ParseExpectedRejectsGarbage()
    {
        Assert.Throws<System.Text.Json.JsonException>(() => CommonResidentCatalog.ParseExpected("not json"));
    }
}
