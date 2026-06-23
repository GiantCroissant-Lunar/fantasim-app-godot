using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

namespace FantaSim.App.World.GenerationGraph;

/// <summary>
/// The subset of authored world-generation options needed before the live globe can be built.
/// </summary>
public sealed record WorldGenerationRenderOptions(int Seed, int TessellationFrequency)
{
    public static WorldGenerationRenderOptions Default { get; } = new(Seed: 7, TessellationFrequency: 3);

    public static WorldGenerationRenderOptions Resolve(
        WorldGenerationGraphView graph,
        WorldGenerationRenderOptions? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var options = fallback ?? Default;
        var optionsNode = WorldGenerationGraphCompiler
            .Compile(graph, validateRequiredInputs: false)
            .Document
            .Nodes
            .FirstOrDefault(node => string.Equals(
                node.FunctionId,
                WorldFunctionProvider.WorldOptions,
                StringComparison.Ordinal));

        if (optionsNode is null)
            return options;

        var seed = ReadInt(optionsNode.Params, "seed", options.Seed);
        var frequency = ReadInt(optionsNode.Params, "frequency", options.TessellationFrequency);
        if (frequency <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(TessellationFrequency),
                frequency,
                "Tessellation frequency must be positive.");
        }

        return new WorldGenerationRenderOptions(seed, frequency);
    }

    private static int ReadInt(JsonObject payload, string key, int fallback)
    {
        if (!payload.TryGetPropertyValue(key, out var node) || node is not JsonValue value)
            return fallback;

        if (value.TryGetValue<int>(out var intValue))
            return intValue;
        if (value.TryGetValue<long>(out var longValue))
            return checked((int)longValue);
        if (value.TryGetValue<string>(out var text)
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return fallback;
    }
}
