using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace App.ExternalTools.CodeGen;

public sealed record VplanetDtoGeneratorResult(string OutputPath, string Source);

public sealed class VplanetDtoGenerator
{
    private const string DefaultFileName = "VplanetDtos.g.cs";
    private const string GeneratedNamespace = "FantaSim.App.NodeGraph.ExternalTools.Vplanet";

    public VplanetDtoGeneratorResult Generate(string schemaDirectory, string outputDirectory, string fileName = DefaultFileName)
    {
        if (string.IsNullOrWhiteSpace(schemaDirectory))
        {
            throw new ArgumentException("Schema directory is required.", nameof(schemaDirectory));
        }

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
        }

        if (!Directory.Exists(schemaDirectory))
        {
            throw new DirectoryNotFoundException($"VPLanet schema directory does not exist: {schemaDirectory}");
        }

        var schemas = LoadSchemas(schemaDirectory);
        var classes = BuildClasses(schemas);
        var source = RenderSource(classes);

        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, fileName);
        File.WriteAllText(outputPath, source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return new VplanetDtoGeneratorResult(outputPath, source);
    }

    private static IReadOnlyDictionary<string, JsonObject> LoadSchemas(string schemaDirectory)
    {
        var schemas = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);

        foreach (var path in Directory.EnumerateFiles(schemaDirectory, "*.json").OrderBy(path => path, StringComparer.Ordinal))
        {
            var root = JsonNode.Parse(File.ReadAllText(path))?.AsObject()
                ?? throw new InvalidOperationException($"Schema '{path}' is not a JSON object.");
            schemas.Add(Path.GetFileName(path), root);
        }

        return schemas;
    }

    private static IReadOnlyList<GeneratedClass> BuildClasses(IReadOnlyDictionary<string, JsonObject> schemas)
    {
        var classes = new List<GeneratedClass>();
        var generatedNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var schema in schemas.Values)
        {
            AddClass(classes, generatedNames, schemas, ClassNameFromSchema(schema), schema);
        }

        return classes
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddClass(
        List<GeneratedClass> classes,
        HashSet<string> generatedNames,
        IReadOnlyDictionary<string, JsonObject> schemas,
        string className,
        JsonObject schema)
    {
        if (!generatedNames.Add(className))
        {
            return;
        }

        var properties = schema["properties"]?.AsObject()
            ?? throw new InvalidOperationException($"Schema for '{className}' has no properties object.");
        var required = schema["required"]?.AsArray()
            .Select(node => node!.GetValue<string>())
            .ToHashSet(StringComparer.Ordinal)
            ?? new HashSet<string>(StringComparer.Ordinal);

        var generatedProperties = new List<GeneratedProperty>();
        foreach (var (jsonName, propertySchemaNode) in properties)
        {
            var propertySchema = propertySchemaNode?.AsObject()
                ?? throw new InvalidOperationException($"Property '{jsonName}' on '{className}' is not an object.");
            var typeName = ResolveType(classes, generatedNames, schemas, className, jsonName, propertySchema);
            var isRequired = required.Contains(jsonName);

            generatedProperties.Add(new GeneratedProperty(
                JsonName: jsonName,
                Name: PascalCase(jsonName),
                TypeName: ApplyOptionality(typeName, isRequired),
                Initializer: InitializerFor(typeName, isRequired)));
        }

        classes.Add(new GeneratedClass(className, generatedProperties));
    }

    private static string ResolveType(
        List<GeneratedClass> classes,
        HashSet<string> generatedNames,
        IReadOnlyDictionary<string, JsonObject> schemas,
        string parentClassName,
        string propertyName,
        JsonObject propertySchema)
    {
        if (propertySchema.TryGetPropertyValue("$ref", out var refNode))
        {
            var refName = refNode!.GetValue<string>();
            if (!schemas.TryGetValue(refName, out var referencedSchema))
            {
                throw new InvalidOperationException($"Could not resolve local schema reference '{refName}'.");
            }

            var referencedClassName = ClassNameFromSchema(referencedSchema);
            AddClass(classes, generatedNames, schemas, referencedClassName, referencedSchema);
            return referencedClassName;
        }

        if (propertySchema.TryGetPropertyValue("oneOf", out var oneOfNode))
        {
            var oneOfTypes = oneOfNode!.AsArray()
                .Select(node => node?["type"]?.GetValue<string>())
                .Where(type => type is not null)
                .OrderBy(type => type, StringComparer.Ordinal)
                .ToArray();

            if (oneOfTypes.SequenceEqual(new[] { "number", "string" }, StringComparer.Ordinal))
            {
                return "System.Text.Json.JsonElement";
            }
        }

        var typeNode = propertySchema["type"];
        if (typeNode is JsonArray typeArray)
        {
            var types = typeArray.Select(node => node!.GetValue<string>()).ToArray();
            var allowsNull = types.Any(type => string.Equals(type, "null", StringComparison.Ordinal));
            var nonNull = types.FirstOrDefault(type => !string.Equals(type, "null", StringComparison.Ordinal));
            if (nonNull is null)
            {
                return "System.Text.Json.JsonElement";
            }

            var primitiveType = PrimitiveType(nonNull);
            return allowsNull ? primitiveType + "?" : primitiveType;
        }

        var typeName = typeNode?.GetValue<string>()
            ?? throw new InvalidOperationException($"Property '{propertyName}' on '{parentClassName}' has no supported type.");

        return typeName switch
        {
            "array" => $"System.Collections.Generic.List<{ResolveArrayItemType(classes, generatedNames, schemas, parentClassName, propertyName, propertySchema)}>",
            "object" => ResolveObjectType(classes, generatedNames, schemas, parentClassName, propertyName, propertySchema),
            _ => PrimitiveType(typeName),
        };
    }

    private static string ResolveArrayItemType(
        List<GeneratedClass> classes,
        HashSet<string> generatedNames,
        IReadOnlyDictionary<string, JsonObject> schemas,
        string parentClassName,
        string propertyName,
        JsonObject propertySchema)
    {
        var items = propertySchema["items"]?.AsObject()
            ?? throw new InvalidOperationException($"Array property '{propertyName}' on '{parentClassName}' has no items schema.");

        return ResolveType(classes, generatedNames, schemas, parentClassName, propertyName, items);
    }

    private static string ResolveObjectType(
        List<GeneratedClass> classes,
        HashSet<string> generatedNames,
        IReadOnlyDictionary<string, JsonObject> schemas,
        string parentClassName,
        string propertyName,
        JsonObject propertySchema)
    {
        if (propertySchema.TryGetPropertyValue("additionalProperties", out var additionalPropertiesNode)
            && additionalPropertiesNode is JsonObject additionalProperties)
        {
            var valueType = ResolveType(classes, generatedNames, schemas, parentClassName, propertyName, additionalProperties);
            return $"System.Collections.Generic.Dictionary<string, {valueType}>";
        }

        if (propertySchema["properties"] is JsonObject)
        {
            var nestedClassName = parentClassName + PascalCase(propertyName);
            AddClass(classes, generatedNames, schemas, nestedClassName, propertySchema);
            return nestedClassName;
        }

        return "System.Text.Json.JsonElement";
    }

    private static string PrimitiveType(string schemaType) =>
        schemaType switch
        {
            "string" => "string",
            "number" => "double",
            "integer" => "int",
            "boolean" => "bool",
            _ => "System.Text.Json.JsonElement",
        };

    private static string ApplyOptionality(string typeName, bool isRequired)
    {
        if (isRequired || IsNullable(typeName))
        {
            return typeName;
        }

        return typeName + "?";
    }

    private static string? InitializerFor(string typeName, bool isRequired)
    {
        if (IsValueType(typeName) || IsNullable(typeName))
        {
            return null;
        }

        if (typeName == "string")
        {
            return isRequired ? "string.Empty" : null;
        }

        if (typeName.StartsWith("System.Collections.Generic.List<", StringComparison.Ordinal)
            || typeName.StartsWith("System.Collections.Generic.Dictionary<", StringComparison.Ordinal)
            || typeName.StartsWith("Vplanet", StringComparison.Ordinal))
        {
            return "new()";
        }

        return null;
    }

    private static bool IsValueType(string typeName) =>
        typeName is "double" or "int" or "bool" or "System.Text.Json.JsonElement";

    private static bool IsNullable(string typeName) =>
        typeName.EndsWith("?", StringComparison.Ordinal);

    private static string RenderSource(IReadOnlyList<GeneratedClass> classes)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("#nullable enable");
        builder.AppendLine("using System.Text.Json.Serialization;");
        builder.AppendLine();
        builder.AppendLine($"namespace {GeneratedNamespace};");
        builder.AppendLine();

        foreach (var generatedClass in classes)
        {
            builder.AppendLine($"public sealed class {generatedClass.Name}");
            builder.AppendLine("{");

            foreach (var property in generatedClass.Properties)
            {
                builder.AppendLine($"    [JsonPropertyName(\"{property.JsonName}\")]");
                var initializer = property.Initializer is null ? string.Empty : $" = {property.Initializer};";
                builder.AppendLine($"    public {property.TypeName} {property.Name} {{ get; set; }}{initializer}");
                builder.AppendLine();
            }

            builder.AppendLine("}");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string ClassNameFromSchema(JsonObject schema)
    {
        var title = schema["title"]?.GetValue<string>()
            ?? throw new InvalidOperationException("Schema is missing a title.");
        return PascalCase(title.Replace("VPLanet", "Vplanet", StringComparison.Ordinal));
    }

    private static string PascalCase(string value)
    {
        var builder = new StringBuilder();
        var uppercaseNext = true;

        foreach (var character in value)
        {
            if (!char.IsLetterOrDigit(character))
            {
                uppercaseNext = true;
                continue;
            }

            if (builder.Length == 0 || uppercaseNext)
            {
                builder.Append(char.ToUpperInvariant(character));
                uppercaseNext = false;
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private sealed record GeneratedClass(string Name, IReadOnlyList<GeneratedProperty> Properties);

    private sealed record GeneratedProperty(string JsonName, string Name, string TypeName, string? Initializer);
}
