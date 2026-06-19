using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using FantaSim.App.Resource;

namespace FantaSim.App.Resource.Bundle;

public sealed class BundleManifest : IResourceManifest
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [JsonPropertyName("bundleId")]
    public string BundleId { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = "0.0.0";

    [JsonPropertyName("entryScene")]
    public string EntrySceneField { get; set; } = string.Empty;

    [JsonPropertyName("pluginAssembly")]
    public string? PluginAssemblyField { get; set; }

    [JsonPropertyName("scenes")]
    public List<string> Scenes { get; set; } = new();

    IReadOnlyList<string> IResourceManifest.Scenes => Scenes;

    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();

    IReadOnlyDictionary<string, string> IResourceManifest.Metadata => Metadata;

    [JsonPropertyName("residentScripts")]
    public List<ResidentScriptBinding> ResidentScripts { get; set; } = new();

    [JsonPropertyName("content")]
    public ContentPayload? Content { get; set; }

    IResourceContent? IResourceManifest.Content => Content;

    [JsonPropertyName("managed")]
    public ManagedPayload? Managed { get; set; }

    IResourceManaged? IResourceManifest.Managed => Managed;

    [JsonIgnore]
    public string EntryScene
    {
        get
        {
            if (Content?.Pcks is { Count: > 0 } pcks)
            {
                var firstScene = pcks[0].EntryScenes.FirstOrDefault();
                if (!string.IsNullOrEmpty(firstScene))
                    return firstScene;
            }

            return EntrySceneField;
        }
    }

    [JsonIgnore]
    public string? PluginAssembly
    {
        get
        {
            if (Managed?.Assemblies is { Count: > 0 } assemblies)
                return assemblies[0].Uri;
            return PluginAssemblyField;
        }
    }

    public static BundleManifest? FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        return JsonSerializer.Deserialize<BundleManifest>(json, ReadOptions);
    }
}

public sealed class ContentPck : IContentPck
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    [JsonPropertyName("entryScenes")]
    public List<string> EntryScenes { get; set; } = new();

    IReadOnlyList<string> IContentPck.EntryScenes => EntryScenes;

    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();

    IReadOnlyDictionary<string, string> IContentPck.Metadata => Metadata;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class ContentPayload : IResourceContent
{
    [JsonPropertyName("pcks")]
    public List<ContentPck> Pcks { get; set; } = new();

    IReadOnlyList<IContentPck> IResourceContent.Pcks => Pcks;
}

public sealed class ManagedAssembly : IManagedAssembly
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string KindField { get; set; } = "dll";

    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = new();

    IReadOnlyDictionary<string, string> IManagedAssembly.Metadata => Metadata;

    [JsonIgnore]
    public ManagedAssemblyKind Kind => KindField?.ToLowerInvariant() switch
    {
        "nupkg" => ManagedAssemblyKind.Nupkg,
        "zip" => ManagedAssemblyKind.Zip,
        _ => ManagedAssemblyKind.Dll,
    };
}

public sealed class ManagedPayload : IResourceManaged
{
    [JsonPropertyName("assemblies")]
    public List<ManagedAssembly> Assemblies { get; set; } = new();

    IReadOnlyList<IManagedAssembly> IResourceManaged.Assemblies => Assemblies;
}

public sealed class ResidentScriptBinding
{
    [JsonPropertyName("nodePath")]
    public string NodePath { get; set; } = string.Empty;

    [JsonPropertyName("scriptPath")]
    public string ScriptPath { get; set; } = string.Empty;

    [JsonPropertyName("residentType")]
    public string ResidentType { get; set; } = string.Empty;
}
