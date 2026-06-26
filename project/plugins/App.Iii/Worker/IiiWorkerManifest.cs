using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FantaSim.App.Iii;

/// <summary>
/// Root document for the iii worker metadata bundle (bundles/iii/workers.json).
/// Data-only bundle: no pluginAssembly, no collectible ALC.
/// </summary>
public sealed class IiiWorkerManifest
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [JsonPropertyName("workers")]
    public List<IiiWorkerDefinition> Workers { get; set; } = new();

    public static IiiWorkerManifest? FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        return JsonSerializer.Deserialize<IiiWorkerManifest>(json, ReadOptions);
    }

    /// <summary>
    /// Built-in fallback that matches the historic hard-coded function families in
    /// <see cref="IiiFunctionProvider"/>. Used when no worker bundle is loaded.
    /// </summary>
    public static IiiWorkerManifest Default { get; } = new()
    {
        Workers = new List<IiiWorkerDefinition>
        {
            new()
            {
                WorkerId = "comfy",
                DisplayName = "ComfyUI text-to-mesh worker",
                FunctionFamilies = new List<string> { "comfy." },
            },
            new()
            {
                WorkerId = "blender",
                DisplayName = "Blender mesh-refinement worker",
                FunctionFamilies = new List<string> { "blender.", "asset." },
            },
            new()
            {
                WorkerId = "vplanet",
                DisplayName = "VPLanet simulation worker",
                FunctionFamilies = new List<string> { "vplanet." },
            },
        },
    };
}
