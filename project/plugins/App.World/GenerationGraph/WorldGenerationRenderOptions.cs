using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using FantaSim.App.World.Topography;

namespace FantaSim.App.World.GenerationGraph;

/// <summary>
/// The subset of authored world-generation options needed before the live globe can be built, including
/// the boundary-profile topography shape numbers (<see cref="BoundaryProfiles"/>) that shape crust relief
/// at plate boundaries.
/// </summary>
public sealed record WorldGenerationRenderOptions(int Seed, int TessellationFrequency)
{
    // Frequency 4 (5120 cells) is the presentation-LOD default for P4 boundary-profile topography: at
    // frequency 3 (1280 cells) a trench+arc pair spans only ~2 cells, so the profiles under-resolve. At
    // frequency 4 the same angular parameters span ~4 cells. Measured one-shot pipeline cost (crust
    // evolution + topology + boundary-profile contribution): ~0.18s at freq 4 vs ~0.10s at freq 3 — no
    // explosion, and the parameter remains overridable per world. See BoundaryProfileLodTests.
    public static WorldGenerationRenderOptions Default { get; } = new(Seed: 7, TessellationFrequency: 4);

    /// <summary>
    /// Boundary-profile topography shape numbers (P4). Defaults to the Earth-like reference
    /// (<see cref="BoundaryProfileParameters.Default"/>); resolved from the <c>world.options</c> node so a
    /// different world can override any shape number.
    /// </summary>
    public BoundaryProfileParameters BoundaryProfiles { get; init; } = BoundaryProfileParameters.Default;

    /// <summary>
    /// Vertical exaggeration (scale rule S1): the factor that maps a crust elevation in metres (on the
    /// <c>CellElevationSystem</c> scale, where continental interior is ~+500 and old abyssal ocean
    /// ~-1500) to unit-globe radius displacement in the crust view. The crust view multiplies each
    /// vertex height by this factor, so <c>renderedRadiusFraction = elevationMetres * VerticalExaggeration</c>.
    ///
    /// <para>Default 1e-5 is calibrated so a +3500 m orogenic peak displaces ~3.5% of the unit-globe
    /// radius (mountains visibly grow across crust snapshots) and a -1500 m abyssal ocean stays above
    /// the mantle sphere at 0.96 of the cap radius. Absolute (not normalised per snapshot) so relief
    /// accumulates with crust age. The factor is a declared world parameter (not a buried constant): a
    /// fantasy world with a different radius or relief scale legitimately exaggerates more or less.</para>
    /// </summary>
    public double VerticalExaggeration { get; init; } = DefaultVerticalExaggeration;

    /// <summary>
    /// Default vertical exaggeration. Elevation units are metres on the <c>CellElevationSystem</c>
    /// scale; the globe is a unit sphere (radius 1.0), so 1e-5 maps 1 m to 1e-5 of the radius
    /// (3500 m -> 3.5% of radius). See <see cref="VerticalExaggeration"/>.
    /// </summary>
    public const double DefaultVerticalExaggeration = 0.00001;

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

        var profiles = ResolveBoundaryProfiles(optionsNode.Params, options.BoundaryProfiles);
        var verticalExaggeration = ReadDouble(optionsNode.Params, "verticalExaggeration", options.VerticalExaggeration);
        if (verticalExaggeration <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(VerticalExaggeration),
                verticalExaggeration,
                "Vertical exaggeration must be positive.");
        }

        return new WorldGenerationRenderOptions(seed, frequency)
        {
            BoundaryProfiles = profiles,
            VerticalExaggeration = verticalExaggeration,
        };
    }

    private static BoundaryProfileParameters ResolveBoundaryProfiles(
        JsonObject payload,
        BoundaryProfileParameters fallback)
    {
        var d = fallback;
        return new BoundaryProfileParameters(
            ConvergentTrenchDepth: ReadDouble(payload, "convergentTrenchDepth", d.ConvergentTrenchDepth),
            ConvergentTrenchHalfWidthRad: ReadDouble(payload, "convergentTrenchHalfWidthRad", d.ConvergentTrenchHalfWidthRad),
            ConvergentArcHeight: ReadDouble(payload, "convergentArcHeight", d.ConvergentArcHeight),
            ConvergentArcSetbackRad: ReadDouble(payload, "convergentArcSetbackRad", d.ConvergentArcSetbackRad),
            ConvergentArcHalfWidthRad: ReadDouble(payload, "convergentArcHalfWidthRad", d.ConvergentArcHalfWidthRad),
            ConvergentCollisionHeight: ReadDouble(payload, "convergentCollisionHeight", d.ConvergentCollisionHeight),
            ConvergentCollisionHalfWidthRad: ReadDouble(payload, "convergentCollisionHalfWidthRad", d.ConvergentCollisionHalfWidthRad),
            DivergentSwellHeight: ReadDouble(payload, "divergentSwellHeight", d.DivergentSwellHeight),
            DivergentSwellHalfWidthRad: ReadDouble(payload, "divergentSwellHalfWidthRad", d.DivergentSwellHalfWidthRad),
            DivergentRiftNotchDepth: ReadDouble(payload, "divergentRiftNotchDepth", d.DivergentRiftNotchDepth),
            DivergentRiftHalfWidthRad: ReadDouble(payload, "divergentRiftHalfWidthRad", d.DivergentRiftHalfWidthRad),
            TransformScarpAmplitude: ReadDouble(payload, "transformScarpAmplitude", d.TransformScarpAmplitude),
            TransformHalfWidthRad: ReadDouble(payload, "transformHalfWidthRad", d.TransformHalfWidthRad),
            TransformScarpPeriodPoints: ReadDouble(payload, "transformScarpPeriodPoints", d.TransformScarpPeriodPoints));
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

    private static double ReadDouble(JsonObject payload, string key, double fallback)
    {
        if (!payload.TryGetPropertyValue(key, out var node) || node is not JsonValue value)
            return fallback;

        if (value.TryGetValue<double>(out var doubleValue))
            return doubleValue;
        if (value.TryGetValue<int>(out var intValue))
            return intValue;
        if (value.TryGetValue<long>(out var longValue))
            return longValue;
        if (value.TryGetValue<string>(out var text)
            && double.TryParse(text, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return fallback;
    }
}
