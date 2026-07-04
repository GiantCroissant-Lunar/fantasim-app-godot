using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using FantaSim.App.Ecs.Systems;
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
    public static WorldGenerationRenderOptions Default { get; } = new(Seed: 7, TessellationFrequency: 4)
    {
        SurfaceSubdivision = SurfaceSubdivisionMode.Adaptive,
    };

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
    /// <para>Default 3e-5 is a focused dry-crust diagnostic scale: a +3500 m orogenic peak displaces
    /// ~10.5% of the unit-globe radius, while the projection profile also reports the non-amplified
    /// true scale from the planet radius. Absolute (not normalised per snapshot) so relief accumulates
    /// with crust age. The factor is a declared world parameter (not a buried constant): a fantasy world
    /// with a different radius or relief scale legitimately exaggerates more or less.</para>
    /// </summary>
    public double VerticalExaggeration { get; init; } = DefaultVerticalExaggeration;

    /// <summary>
    /// Hydrosphere/elevation interpretation for crust-derived terrain. Default is dry because the
    /// current world product is explicitly waterless; hydrosphere-present keeps the legacy oceanic
    /// sea-level and age-deepening formula available for diagnostics and future wet worlds.
    /// </summary>
    public CellElevationHydrosphereMode HydrosphereMode { get; init; } = CellElevationHydrosphereMode.Absent;

    /// <summary>
    /// Derived render-mesh subdivision mode. Fixed stays the default until adaptive has visual proof in
    /// the exported app; authored worlds can opt in through the options node.
    /// </summary>
    public SurfaceSubdivisionMode SurfaceSubdivision { get; init; } = SurfaceSubdivisionMode.Fixed;

    /// <summary>
    /// Maximum adaptive subdivision depth (recursion bound) for render-only globe caps.
    /// A round with no splits stops early.
    /// </summary>
    public int AdaptiveSubdivisionMaxDepth { get; init; } = 2;

    /// <summary>
    /// Height-delta threshold, in post-exaggeration unit-sphere displacement, that decides whether an
    /// edge is split by adaptive subdivision. Coupled to the lens parameters (height exponent, etc.)
    /// that map metres to unit radius.
    /// </summary>
    public double AdaptiveSubdivisionEdgeHeightDelta { get; init; } = 0.02;

    /// <summary>
    /// Feature-weight threshold, in normalized typed-crust-feature units, that decides whether an edge is
    /// split even when the height envelope is flat.
    /// </summary>
    public double AdaptiveSubdivisionFeatureWeightDelta { get; init; } = 0.25;

    /// <summary>
    /// Default vertical exaggeration. Elevation units are metres on the <c>CellElevationSystem</c>
    /// scale; the globe is a unit sphere (radius 1.0), so 3e-5 maps 1 m to 3e-5 of the radius
    /// (3500 m -> 10.5% of radius). See <see cref="VerticalExaggeration"/>.
    /// </summary>
    public const double DefaultVerticalExaggeration = 0.00003;

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

        var adaptiveDepth = ReadInt(optionsNode.Params, "adaptiveSubdivisionMaxDepth", options.AdaptiveSubdivisionMaxDepth);
        if (adaptiveDepth < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(AdaptiveSubdivisionMaxDepth),
                adaptiveDepth,
                "Adaptive subdivision max depth must be non-negative.");
        }

        var adaptiveThreshold = ReadDouble(optionsNode.Params, "adaptiveSubdivisionEdgeHeightDelta", options.AdaptiveSubdivisionEdgeHeightDelta);
        if (adaptiveThreshold < 0 || double.IsNaN(adaptiveThreshold) || double.IsInfinity(adaptiveThreshold))
        {
            throw new ArgumentOutOfRangeException(
                nameof(AdaptiveSubdivisionEdgeHeightDelta),
                adaptiveThreshold,
                "Adaptive subdivision edge-height delta must be non-negative and finite.");
        }

        var adaptiveFeatureThreshold = ReadDouble(
            optionsNode.Params,
            "adaptiveSubdivisionFeatureWeightDelta",
            options.AdaptiveSubdivisionFeatureWeightDelta);
        if (adaptiveFeatureThreshold < 0 || double.IsNaN(adaptiveFeatureThreshold) || double.IsInfinity(adaptiveFeatureThreshold))
        {
            throw new ArgumentOutOfRangeException(
                nameof(AdaptiveSubdivisionFeatureWeightDelta),
                adaptiveFeatureThreshold,
                "Adaptive subdivision feature-weight delta must be non-negative and finite.");
        }

        return new WorldGenerationRenderOptions(seed, frequency)
        {
            BoundaryProfiles = profiles,
            VerticalExaggeration = verticalExaggeration,
            HydrosphereMode = ReadHydrosphereMode(optionsNode.Params, options.HydrosphereMode),
            SurfaceSubdivision = ReadSurfaceSubdivisionMode(optionsNode.Params, options.SurfaceSubdivision),
            AdaptiveSubdivisionMaxDepth = adaptiveDepth,
            AdaptiveSubdivisionEdgeHeightDelta = adaptiveThreshold,
            AdaptiveSubdivisionFeatureWeightDelta = adaptiveFeatureThreshold,
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

    private static CellElevationHydrosphereMode ReadHydrosphereMode(
        JsonObject payload,
        CellElevationHydrosphereMode fallback)
    {
        if (!payload.TryGetPropertyValue("hydrosphereMode", out var node)
            || node is not JsonValue value
            || !value.TryGetValue<string>(out var text))
        {
            return fallback;
        }

        var normalized = text.Trim().ToLowerInvariant();
        return normalized switch
        {
            "dry" or "absent" or "none" or "no-hydrosphere" or "waterless" => CellElevationHydrosphereMode.Absent,
            "present" or "hydrosphere" or "oceanic" or "wet" => CellElevationHydrosphereMode.Present,
            _ => fallback,
        };
    }

    private static SurfaceSubdivisionMode ReadSurfaceSubdivisionMode(
        JsonObject payload,
        SurfaceSubdivisionMode fallback)
    {
        if (!payload.TryGetPropertyValue("surfaceSubdivision", out var node)
            || node is not JsonValue value
            || !value.TryGetValue<string>(out var text))
        {
            return fallback;
        }

        var normalized = text.Trim().ToLowerInvariant();
        return normalized switch
        {
            "fixed" or "none" or "off" => SurfaceSubdivisionMode.Fixed,
            "adaptive" or "adaptive-subdivision" or "lod" => SurfaceSubdivisionMode.Adaptive,
            _ => fallback,
        };
    }
}
