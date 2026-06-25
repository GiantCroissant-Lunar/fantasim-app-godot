#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Runtime;
using BoomHud.Abstractions.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeQuality;

// Host-local copy of project/tests/App.Resource.Tests/TestViewSourceFactory.cs (a host
// cannot ProjectReference a test project). Verbatim behavior: emits a real collectible
// .dll containing a TestViewSource that implements IViewSource. Structure mirrors
// PluginArchi's TestAssemblyFactory (same GetReferences harvesting) but the emitted type
// is an IViewSource, not an IPlugin -- no PluginArchi dependency needed.
internal static class TestViewSourceFactory
{
    public static string EmitAssembly(string directory, string assemblyName, string viewId)
    {
        Directory.CreateDirectory(directory);

        var source = $$"""
            using System;
            using FantaSim.App.Ui;
            using BoomHud.Abstractions.Runtime;

            namespace GeneratedViews;

            public sealed class TestViewSource : IViewSource
            {
                public string ViewId => "{{viewId}}";

                public RuntimeSurfaceDocument BuildDocument() => throw new NotSupportedException();

                public event Action? Changed { add { } remove { } }

                public void Dispatch(string action, string? componentId) { }
            }
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var outputPath = Path.Combine(directory, assemblyName + ".dll");
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var result = compilation.Emit(outputPath);
        if (!result.Success)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
        }

        return outputPath;
    }

    public static IReadOnlyList<MetadataReference> GetReferences()
    {
        // Force the assemblies whose types appear in the emitted source: object/Enumerable
        // (System.Linq), IViewSource (App.Ui contract), RuntimeSurfaceDocument
        // (BoomHud.Abstractions), and GCSettings (System.Runtime).
        var forcedAssemblies = new[]
        {
            typeof(object).Assembly,
            typeof(Enumerable).Assembly,
            typeof(FantaSim.App.Ui.IViewSource).Assembly,
            typeof(RuntimeSurfaceDocument).Assembly,
            typeof(GCSettings).Assembly
        };

        // DIVERGES from the App.Resource.Tests copy: under the Godot .NET loader the game
        // assemblies (FantaSim.App.Ui.Contracts, BoomHud.Abstractions, ...) have an EMPTY
        // Assembly.Location, so the test's AppDomain+Location harvest silently drops them and the
        // emit fails with CS0246 'FantaSim'/'BoomHud'/'IViewSource'/'RuntimeSurfaceDocument'.
        // Resolve references by FILE PATH instead: (1) every loaded assembly that DOES expose a
        // real Location (covers the BCL), (2) each forced assembly via Location-or-BaseDirectory,
        // and (3) every *.dll shipped next to the host (the .godot/mono/temp/bin output dir).
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
                paths.Add(assembly.Location);
        }

        foreach (var assembly in forcedAssemblies)
        {
            if (!assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            {
                paths.Add(assembly.Location);
                continue;
            }

            var candidate = Path.Combine(AppContext.BaseDirectory, assembly.GetName().Name + ".dll");
            if (File.Exists(candidate))
                paths.Add(candidate);
        }

        try
        {
            foreach (var dll in Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll"))
                paths.Add(dll);
        }
        catch (IOException)
        {
            // Best effort -- the AppDomain + forced-assembly harvest above still covers the common case.
        }

        return paths
            .Select(TryCreateReferenceFromFile)
            .Where(reference => reference is not null)
            .Select(reference => reference!)
            .ToArray();
    }

    private static MetadataReference? TryCreateReferenceFromFile(string location)
    {
        if (!File.Exists(location))
        {
            return null;
        }

        try
        {
            return MetadataReference.CreateFromFile(location);
        }
        catch (IOException)
        {
            return null;
        }
    }
}