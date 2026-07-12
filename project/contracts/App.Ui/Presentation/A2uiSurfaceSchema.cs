using System;
using System.IO;
using System.Reflection;

namespace FantaSim.App.Ui.Presentation;

/// <summary>
/// Accessor for the published <b>A2UI surface JSON Schema</b> (draft 2020-12) — the contract an agent's
/// UI payload must satisfy to normalize (<see cref="A2uiPresentationNormalizer"/>) and render against the
/// BoomHud <c>basic</c> catalog. The schema is embedded in this contract assembly so a host can hand it to
/// an agent (e.g. answer a "give me the emit-detail schema" request) without a file dependency.
/// </summary>
public static class A2uiSurfaceSchema
{
    /// <summary>Stable manifest-resource / <c>$id</c> filename of the embedded schema.</summary>
    public const string ResourceName = "FantaSim.App.Ui.a2ui-surface.schema.json";

    private static readonly Lazy<string> LazyJson = new(ReadEmbedded);

    /// <summary>The schema document as a JSON string.</summary>
    public static string Json => LazyJson.Value;

    private static string ReadEmbedded()
    {
        var assembly = typeof(A2uiSurfaceSchema).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded A2UI schema resource '{ResourceName}' was not found in {assembly.GetName().Name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
