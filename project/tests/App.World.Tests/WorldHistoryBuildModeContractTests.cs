using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using FantaSim.App.World.Dto;
using ServiceArchi.Contracts;
using ServiceArchi.Core;
using Xunit;

namespace FantaSim.App.World.Tests;

/// <summary>
/// Runtime proof for the T1/T3 boundary in addition to compile-time DTO assertions. The plugin is
/// loaded into a real collectible context, its returned graph is recursively ownership-checked,
/// and the context must collect after IService disposal.
/// </summary>
public sealed class WorldHistoryBuildModeContractTests
{
    [Fact]
    public void World_service_returns_resident_owned_graph_and_collectible_context_unloads()
    {
        var weakContext = ExerciseWorldServiceInCollectibleContext();

        // Microsoft unloadability guidance: keep ALC-typed locals in a no-inline helper, initiate
        // unload, then force multiple collection/finalizer cycles because unloading is cooperative.
        // https://learn.microsoft.com/en-us/dotnet/standard/assembly/unloadability
        for (var attempt = 0; attempt < 20 && weakContext.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Thread.Sleep(10);
        }

        Assert.False(weakContext.IsAlive,
            "App.World collectible context stayed rooted after IService.Dispose; inspect returned DTOs/static caches.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ExerciseWorldServiceInCollectibleContext()
    {
        var pluginPath = Path.Combine(AppContext.BaseDirectory, "FantaSim.App.World.dll");
        Assert.True(File.Exists(pluginPath), $"Missing test plugin binary: {pluginPath}");

        var sharedAssemblies = new[]
        {
            typeof(FantaSim.App.World.IService).Assembly,
            typeof(IRegistry).Assembly,
        };
        var context = new TestPluginLoadContext(pluginPath, sharedAssemblies);
        var pluginAssembly = context.LoadFromAssemblyPath(pluginPath);
        var serviceType = pluginAssembly.GetType("FantaSim.App.World.Services.Service", throwOnError: true)!;
        var service = (FantaSim.App.World.IService)Activator.CreateInstance(
            serviceType,
            new object?[] { new ServiceRegistry(), null })!;

        try
        {
            var values = service.GetFieldValuesAsync(new WorldFieldValuesRequest(
                "app:test:L0:world:default",
                new[] { "app.elevation-m" }));
            Assert.Single(values.FieldValues);
            AssertBoundaryOwnership(values, context);
            Assert.Throws<InvalidOperationException>(() => AssertBoundaryOwnership(
                new Dictionary<string, object> { ["collectible-value"] = service },
                context));
        }
        finally
        {
            ((IDisposable)service).Dispose();
        }

        var weak = new WeakReference(context);
        context.Unload();
        return weak;
    }

    private static void AssertBoundaryOwnership(object root, AssemblyLoadContext collectibleContext)
    {
        var pending = new Stack<(object Value, string Path)>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        pending.Push((root, "$"));

        while (pending.Count > 0)
        {
            var (value, path) = pending.Pop();
            var type = value.GetType();
            AssertTypeOwnership(type, collectibleContext, path);

            if (!type.IsValueType && !visited.Add(value))
                continue;
            if (value is string || type.IsPrimitive || type.IsEnum || value is decimal)
                continue;

            if (value is DictionaryEntry entry)
            {
                if (entry.Key is not null)
                    pending.Push((entry.Key, path + ".Key"));
                if (entry.Value is not null)
                    pending.Push((entry.Value, path + ".Value"));
                continue;
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
            {
                var key = type.GetProperty("Key")!.GetValue(value);
                var itemValue = type.GetProperty("Value")!.GetValue(value);
                if (key is not null)
                    pending.Push((key, path + ".Key"));
                if (itemValue is not null)
                    pending.Push((itemValue, path + ".Value"));
                continue;
            }

            if (value is IEnumerable enumerable)
            {
                var index = 0;
                foreach (var item in enumerable)
                {
                    if (item is not null)
                        pending.Push((item, $"{path}[{index}]"));
                    index++;
                }
                continue;
            }

            // BCL scalar wrappers cannot introduce plugin-owned payload types once their generic /
            // element types have passed AssertTypeOwnership.
            if (type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
                continue;

            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0)
                    continue;
                AssertTypeOwnership(property.PropertyType, collectibleContext, $"{path}.{property.Name}:type");
                var child = property.GetValue(value);
                if (child is not null)
                    pending.Push((child, $"{path}.{property.Name}"));
            }
        }
    }

    private static void AssertTypeOwnership(Type type, AssemblyLoadContext collectibleContext, string path)
    {
        if (type.IsArray || type.IsByRef || type.IsPointer)
        {
            if (type.GetElementType() is { } element)
                AssertTypeOwnership(element, collectibleContext, path + ":element");
            return;
        }

        var owner = AssemblyLoadContext.GetLoadContext(type.Assembly);
        if (ReferenceEquals(owner, collectibleContext))
        {
            throw new InvalidOperationException(
                $"Plugin-owned type escaped the IService boundary at {path}: {type.AssemblyQualifiedName}");
        }

        foreach (var argument in type.GetGenericArguments())
            AssertTypeOwnership(argument, collectibleContext, path + ":generic");
    }

    private sealed class TestPluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;
        private readonly string _baseDirectory;
        private readonly IReadOnlyDictionary<string, Assembly> _shared;

        public TestPluginLoadContext(string pluginPath, IEnumerable<Assembly> sharedAssemblies)
            : base("app-world-contract-gate-" + Guid.NewGuid().ToString("N"), isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
            _baseDirectory = Path.GetDirectoryName(pluginPath)!;
            _shared = sharedAssemblies.ToDictionary(
                assembly => assembly.GetName().Name!,
                StringComparer.OrdinalIgnoreCase);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name is { } name && _shared.TryGetValue(name, out var shared))
                return shared;

            var resolved = _resolver.ResolveAssemblyToPath(assemblyName);
            if (resolved is null && assemblyName.Name is { } simpleName)
            {
                var adjacent = Path.Combine(_baseDirectory, simpleName + ".dll");
                if (File.Exists(adjacent))
                    resolved = adjacent;
            }
            return resolved is null ? null : LoadFromAssemblyPath(resolved);
        }
    }
}
