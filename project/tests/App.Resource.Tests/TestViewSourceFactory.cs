#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Runtime;
using BoomHud.Abstractions.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace App.Resource.Tests;

// Emits a real collectible .dll containing a TestViewSource that implements IViewSource.
// Structure mirrors PluginArchi's TestAssemblyFactory (same GetReferences harvesting) but
// the emitted type is an IViewSource, not an IPlugin -- no PluginArchi dependency needed.
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
        // (BoomHud.Abstractions -- transitive via BoomHud.Foundation, may not yet be in the
        // AppDomain at emit time so we anchor on the type itself), and GCSettings (System.Runtime).
        var forcedAssemblies = new[]
        {
            typeof(object).Assembly,
            typeof(Enumerable).Assembly,
            typeof(FantaSim.App.Ui.IViewSource).Assembly,
            typeof(RuntimeSurfaceDocument).Assembly,
            typeof(GCSettings).Assembly
        };

        return AppDomain.CurrentDomain
            .GetAssemblies()
            .Concat(forcedAssemblies)
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
            .GroupBy(assembly => assembly.Location, StringComparer.OrdinalIgnoreCase)
            .Select(group => TryCreateReferenceFromFile(group.Key))
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