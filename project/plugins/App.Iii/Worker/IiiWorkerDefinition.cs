using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FantaSim.App.Iii;

/// <summary>
/// Metadata for one iii worker capability surface: the function families it owns
/// and runtime defaults needed by callers. Process launch/supervision is owned by
/// a separate lifecycle surface, not by this routing catalog.
/// </summary>
public sealed class IiiWorkerDefinition
{
    [JsonPropertyName("workerId")]
    public string WorkerId { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Function-family prefixes this worker owns (e.g. "comfy.", "vplanet.").
    /// The <see cref="IiiFunctionProvider"/> claims any function id starting with one of these prefixes.
    /// </summary>
    [JsonPropertyName("functionFamilies")]
    public List<string> FunctionFamilies { get; set; } = new();

    /// <summary>
    /// Explicit function ids this worker owns in addition to the families.
    /// </summary>
    [JsonPropertyName("functions")]
    public List<string> Functions { get; set; } = new();

    [JsonPropertyName("environmentDefaults")]
    public Dictionary<string, string> EnvironmentDefaults { get; set; } = new();
}
