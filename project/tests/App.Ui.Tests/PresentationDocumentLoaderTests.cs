using System;
using System.IO;
using FantaSim.App.Ui.Activity;
using FantaSim.App.Ui.Presentation;
using Xunit;

namespace FantaSim.App.Ui.Tests;

public sealed class PresentationDocumentLoaderTests
{
    [Fact]
    public void LoadText_PrefersLooseFileNextToAssembly()
    {
        var assembly = typeof(PresentationDocumentLoaderTests).Assembly;
        var assemblyDirectory = Path.GetDirectoryName(assembly.Location)
            ?? throw new InvalidOperationException("Test assembly has no directory.");
        var fileName = $"presentation-loader-{Guid.NewGuid():N}.json";
        var path = Path.Combine(assemblyDirectory, fileName);

        try
        {
            File.WriteAllText(path, """{"source":"loose"}""");

            var text = PresentationDocumentLoader.LoadText(
                assembly,
                fileName,
                embeddedResourceSuffix: ".MissingEmbeddedPresentationDocument");

            Assert.Equal("""{"source":"loose"}""", text);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void LoadText_FallsBackToEmbeddedResourceWhenLooseFileIsAbsent()
    {
        var text = PresentationDocumentLoader.LoadText(
            typeof(ActivityViewSource).Assembly,
            fileName: $"missing-{Guid.NewGuid():N}.json",
            embeddedResourceSuffix: ".Presentation.activity.presentation.json");

        Assert.Contains("\"surfaceId\": \"activity\"", text, StringComparison.Ordinal);
        Assert.Contains("\"catalogId\": \"boomhud.runtime.basic.v1\"", text, StringComparison.Ordinal);
    }
}
