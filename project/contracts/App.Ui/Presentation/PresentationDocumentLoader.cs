using System;
using System.IO;
using System.Reflection;

namespace FantaSim.App.Ui.Presentation;

/// <summary>
/// Generic presentation-document loader for hot-reloadable UI bundles. Domain-neutral: no
/// Activity, NodeGraph, Timeline, Godot, or bundle-specific types. A bundle plugin calls this
/// from its bootstrap code and hands the returned template text to its view source; the view
/// source itself stays pure and knows nothing about where templates come from.
///
/// Resolution order mirrors the bundle hot-reload contract: prefer a loose file sitting next to
/// the loaded assembly (the unpacked bundle directory), then fall back to an embedded resource
/// whose manifest name ends with <paramref name="embeddedResourceSuffix"/> (the packed-in
/// template shipped inside the assembly).
/// </summary>
public static class PresentationDocumentLoader
{
    /// <summary>
    /// Load presentation-document text. Returns the first non-null hit in resolution order;
    /// throws <see cref="InvalidOperationException"/> if neither source is available.
    /// </summary>
    /// <param name="assembly">The bundle assembly whose directory/resources are searched.</param>
    /// <param name="fileName">Loose-file name to look for beside <paramref name="assembly"/>.</param>
    /// <param name="embeddedResourceSuffix">
    /// Manifest resource name suffix (e.g. <c>.Presentation.activity.presentation.json</c>) used
    /// to locate the packed-in fallback resource.
    /// </param>
    /// <exception cref="ArgumentNullException">Any argument is null/whitespace.</exception>
    /// <exception cref="InvalidOperationException">Neither the loose file nor a matching embedded resource is available, or the resource cannot be opened.</exception>
    public static string LoadText(Assembly assembly, string fileName, string embeddedResourceSuffix)
    {
        if (assembly is null) throw new ArgumentNullException(nameof(assembly));
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentNullException(nameof(fileName));
        if (string.IsNullOrWhiteSpace(embeddedResourceSuffix)) throw new ArgumentNullException(nameof(embeddedResourceSuffix));

        return LoadFromBundleFile(assembly, fileName)
            ?? LoadFromEmbeddedResource(assembly, embeddedResourceSuffix);
    }

    private static string? LoadFromBundleFile(Assembly assembly, string fileName)
    {
        var assemblyPath = assembly.Location;
        if (string.IsNullOrWhiteSpace(assemblyPath))
            return null;

        var assemblyDirectory = Path.GetDirectoryName(assemblyPath);
        if (string.IsNullOrWhiteSpace(assemblyDirectory))
            return null;

        var path = Path.Combine(assemblyDirectory, fileName);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static string LoadFromEmbeddedResource(Assembly assembly, string embeddedResourceSuffix)
    {
        var resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(embeddedResourceSuffix, StringComparison.Ordinal));

        if (resourceName is null)
            throw new InvalidOperationException(
                $"Presentation document resource ending '{embeddedResourceSuffix}' was not found in assembly '{assembly.FullName}'.");

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Presentation document resource '{resourceName}' could not be opened.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}