#nullable enable
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading.Tasks;
using FantaSim.App.Resource;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace App.Resource.Tests;

// Plain-xUnit proof of the FOURTH ALC pin class (dump-verified 2026-07-10 on the world bundle):
// System.Text.Json pools CachingContexts across VALUE-EQUAL JsonSerializerOptions
// (JsonSerializerOptions.TrackedCachingContexts). A bundle-local static options is value-equal
// across bundle generations, so generation N+1 adopts the pooled context that still caches
// generation N's JsonTypeInfo/RuntimeType entries -> old LoaderAllocator -> old ALC never
// collects. SharedStjCachePurge.ClearReflectionCaches must sever exactly that.
public class SharedStjCachePurgeTests
{
    [Fact]
    public async Task PooledCachingContext_PinsOldAlc_UntilPurge()
    {
        var tempDir = NewTempDir(nameof(PooledCachingContext_PinsOldAlc_UntilPurge));
        try
        {
            // One helper frame: old-generation serialize + unload + new-generation adoption of
            // the pooled context, with no GC in between (mirrors the app's reload sequence,
            // where the new bundle deserializes its assets before the collection probe runs).
            var (weak, nextGenOptions) = PinViaPooledContext(tempDir);

            await FullGcAsync(() => !weak.IsAlive);
            Assert.True(
                weak.IsAlive,
                "Premise: the value-equal next-generation options should pin the old ALC via the "
                + "pooled STJ CachingContext. If STJ stopped pooling, the purge is obsolete.");

            SharedStjCachePurge.ClearReflectionCaches("test", NullLogger.Instance);

            await FullGcAsync(() => !weak.IsAlive);
            Assert.False(
                weak.IsAlive,
                "The old ALC should collect once the shared STJ reflection caches are cleared.");

            GC.KeepAlive(nextGenOptions);
        }
        finally
        {
            BestEffortDelete(tempDir);
        }
    }

    // All ALC-typed locals stay inside this [NoInlining] helper so they do not linger on the
    // test method's stack frame (same discipline as ReloadCollectionTests).
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference alcRef, JsonSerializerOptions nextGenOptions) PinViaPooledContext(string dir)
    {
        var dll = EmitPocoAssembly(dir, "GenPoco_" + Guid.NewGuid().ToString("N"));

        var alc = new AssemblyLoadContext("stj-purge", isCollectible: true);
        var asm = alc.LoadFromAssemblyPath(dll);
        var pocoType = asm.GetType("GeneratedPocos.TestPoco")!;

        // Old generation: a bundle-local options serializes a bundle-local type, seeding the
        // pooled CachingContext with a collectible RuntimeType.
        var oldGenOptions = NewBundleLikeOptions();
        JsonSerializer.Serialize(Activator.CreateInstance(pocoType), pocoType, oldGenOptions);

        var weak = new WeakReference(alc);
        alc.Unload();

        // New generation: a VALUE-EQUAL options adopts the same pooled context (strongly),
        // which still caches the old generation's types.
        var nextGenOptions = NewBundleLikeOptions();
        JsonSerializer.Serialize(42, nextGenOptions);

        return (weak, nextGenOptions);
    }

    // Distinctive value-equal shape so no other test's options accidentally matches this pool slot.
    private static JsonSerializerOptions NewBundleLikeOptions() => new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        MaxDepth = 47,
    };

    private static async Task FullGcAsync(Func<bool> done)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            if (done())
            {
                return;
            }

            await Task.Delay(25);
        }
    }

    private static string EmitPocoAssembly(string directory, string assemblyName)
    {
        Directory.CreateDirectory(directory);

        var source = """
            namespace GeneratedPocos;

            public sealed class TestPoco
            {
                public int Value { get; set; }
                public string? Name { get; set; }
            }
            """;

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source) },
            TestViewSourceFactory.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var outputPath = Path.Combine(directory, assemblyName + ".dll");
        var result = compilation.Emit(outputPath);
        if (!result.Success)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
        }

        return outputPath;
    }

    private static string NewTempDir(string testName)
    {
        var dir = Path.Combine(Path.GetTempPath(), "app-resource-tests", testName + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void BestEffortDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
            // Best effort -- the ALC may still be tearing down. Not a test failure.
        }
    }
}
