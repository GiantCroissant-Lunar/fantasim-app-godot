using System.Collections.Generic;
using System.Globalization;
using FantaSim.Mythosphere.Cosmology.Contracts;
using FantaSim.Mythosphere.Cosmology.Registry;

namespace FantaSim.App.World.GenerationGraph;

/// <summary>
/// Authors the app's current <c>default</c> world declaration version 1. Receives the pinned
/// <paramref name="scienceLawSet"/> from engine science authoring, reads the current
/// <see cref="WorldGenerationRenderOptions.Default"/> values, and builds a
/// <see cref="WorldDeclarationDraft"/> whose truth tuning contains exactly the 17 truth-affecting
/// option keys and whose presenter semantic descriptors carry the 5 presentation-only options.
///
/// <para>Classification (G-010 plan locked authoring table):</para>
/// <list type="bullet">
/// <item><see cref="WorldGenerationRenderOptions.Seed"/> → <see cref="WorldDeclarationDraft.Seed"/></item>
/// <item>TessellationFrequency, SpinRateRadiansPerMegaAnnum, HydrosphereMode, all 14
/// BoundaryProfiles values → truth <see cref="WorldDeclarationDraft.Tuning"/> (17 keys total)</item>
/// <item>VerticalExaggeration, SurfaceSubdivision, AdaptiveSubdivisionMaxDepth,
/// AdaptiveSubdivisionEdgeHeightDelta, AdaptiveSubdivisionFeatureWeightDelta → each layer's
/// presenter semantic descriptor (and therefore <see cref="WorldDeclaration.PresentationDigest"/>)</item>
/// </list>
///
/// <para><see cref="WorldDeclarationDraft.EnabledPacks"/>, <see cref="WorldDeclarationDraft.DomainAdditions"/>,
/// and <see cref="WorldDeclarationDraft.DomainExclusions"/> are empty: the host bundle is not an
/// EnabledPack (G-010 design decision 6). Enabled layer order is the order of
/// <see cref="WorldGenerationGraphDefaults.BuildFamily"/>.LayerGraphBindings.</para>
/// </summary>
public static class DefaultWorldDeclarationAuthoring
{
    public const string WorldName = "default";
    public const string RegistrantId = "app.world";
    public const string Version = "1";

    /// <summary>Style id for the default globe presentation contract (all layers share this).</summary>
    public const string DefaultStyleId = "app.world/default-globe-style/v1";

    /// <summary>Semantic version stamped into each presenter descriptor.</summary>
    private const string PresenterDescriptorVersion = "1";

    /// <summary>
    /// Claims <c>default</c> for <c>app.world</c>, creates the exact draft from
    /// <paramref name="options"/> (or <see cref="WorldGenerationRenderOptions.Default"/> when null),
    /// appends version 1, and returns the materialized <see cref="WorldDeclaration"/>.
    /// Numeric tuning values use invariant "R" round-trip formatting.
    /// </summary>
    public static WorldDeclaration AppendTo(
        IWorldDeclarationRegistry registry,
        LawSet scienceLawSet,
        WorldGenerationRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(scienceLawSet);

        var opts = options ?? WorldGenerationRenderOptions.Default;

        registry.ClaimWorldName(new RegistryNameClaim(
            WorldName, RegistrantId, RegistryAuthority.Consumer));

        var lawSetRef = LawSetReference.Exact(
            scienceLawSet.Definition.LawSetId,
            scienceLawSet.Digest);

        var enabledLayers = BuildEnabledLayers(scienceLawSet, opts);
        var tuning = BuildTruthTuning(opts);

        var draft = new WorldDeclarationDraft(
            worldName: WorldName,
            version: Version,
            lawSetRef: lawSetRef,
            enabledPacks: System.Array.Empty<PackReference>(),
            enabledLayers: enabledLayers,
            domainAdditions: System.Array.Empty<string>(),
            domainExclusions: System.Array.Empty<string>(),
            seed: opts.Seed,
            tuning: tuning);

        return registry.AppendWorldDeclarationVersion(RegistrantId, draft);
    }

    private static List<LayerSelection> BuildEnabledLayers(
        LawSet scienceLawSet,
        WorldGenerationRenderOptions opts)
    {
        var graphBindings = WorldGenerationGraphDefaults.BuildFamily().LayerGraphBindings
            ?? throw new System.InvalidOperationException(
                "The default world generation graph must declare layer bindings.");
        var scienceLayers = scienceLawSet.Definition.Layers;
        var presenterScalars = BuildPresenterScalarProperties(opts);

        var layers = new List<LayerSelection>(graphBindings.Count);
        for (var i = 0; i < graphBindings.Count; i++)
        {
            var layerId = graphBindings[i].LayerId;
            var scienceLayer = scienceLayers[layerId];
            var presenterId = ResolvePresenterContract(layerId);
            var presenterDigest = ComputePresenterDigest(presenterId, presenterScalars);

            var presentation = new PresentationBinding(
                presenterId: presenterId,
                presenterDigest: presenterDigest,
                styleId: DefaultStyleId,
                laneOrder: i,
                visible: true);

            layers.Add(new LayerSelection(
                layerId: layerId,
                contentDigest: scienceLayer.ContentDigest,
                producedDomains: scienceLayer.ProducedDomains,
                parameterValues: new Dictionary<string, string>(),
                presentation: presentation));
        }

        return layers;
    }

    private static Dictionary<string, string> BuildTruthTuning(WorldGenerationRenderOptions opts)
    {
        var bps = opts.BoundaryProfiles;
        var inv = CultureInfo.InvariantCulture;
        return new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            ["tessellationFrequency"] = opts.TessellationFrequency.ToString(inv),
            ["spinRateRadiansPerMegaAnnum"] = opts.SpinRateRadiansPerMegaAnnum.ToString("R", inv),
            ["hydrosphereMode"] = opts.HydrosphereMode.ToString(),
            ["convergentTrenchDepth"] = bps.ConvergentTrenchDepth.ToString("R", inv),
            ["convergentTrenchHalfWidthRad"] = bps.ConvergentTrenchHalfWidthRad.ToString("R", inv),
            ["convergentArcHeight"] = bps.ConvergentArcHeight.ToString("R", inv),
            ["convergentArcSetbackRad"] = bps.ConvergentArcSetbackRad.ToString("R", inv),
            ["convergentArcHalfWidthRad"] = bps.ConvergentArcHalfWidthRad.ToString("R", inv),
            ["convergentCollisionHeight"] = bps.ConvergentCollisionHeight.ToString("R", inv),
            ["convergentCollisionHalfWidthRad"] = bps.ConvergentCollisionHalfWidthRad.ToString("R", inv),
            ["divergentSwellHeight"] = bps.DivergentSwellHeight.ToString("R", inv),
            ["divergentSwellHalfWidthRad"] = bps.DivergentSwellHalfWidthRad.ToString("R", inv),
            ["divergentRiftNotchDepth"] = bps.DivergentRiftNotchDepth.ToString("R", inv),
            ["divergentRiftHalfWidthRad"] = bps.DivergentRiftHalfWidthRad.ToString("R", inv),
            ["transformScarpAmplitude"] = bps.TransformScarpAmplitude.ToString("R", inv),
            ["transformHalfWidthRad"] = bps.TransformHalfWidthRad.ToString("R", inv),
            ["transformScarpPeriodPoints"] = bps.TransformScarpPeriodPoints.ToString("R", inv),
        };
    }

    private static Dictionary<string, string> BuildPresenterScalarProperties(WorldGenerationRenderOptions opts)
    {
        var inv = CultureInfo.InvariantCulture;
        return new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            ["styleId"] = DefaultStyleId,
            ["verticalExaggeration"] = opts.VerticalExaggeration.ToString("R", inv),
            ["surfaceSubdivision"] = opts.SurfaceSubdivision.ToString(),
            ["adaptiveSubdivisionMaxDepth"] = opts.AdaptiveSubdivisionMaxDepth.ToString(inv),
            ["adaptiveSubdivisionEdgeHeightDelta"] = opts.AdaptiveSubdivisionEdgeHeightDelta.ToString("R", inv),
            ["adaptiveSubdivisionFeatureWeightDelta"] = opts.AdaptiveSubdivisionFeatureWeightDelta.ToString("R", inv),
        };
    }

    private static ContentDigest ComputePresenterDigest(
        string presenterId,
        IReadOnlyDictionary<string, string> scalarProperties)
    {
        var descriptor = new SemanticContentDescriptor(
            kind: "presenter",
            id: presenterId,
            version: PresenterDescriptorVersion,
            scalarProperties: scalarProperties,
            setProperties: new Dictionary<string, IReadOnlyList<string>>());
        return CanonicalDeclarationCodec.ComputeSemanticContentDigest(descriptor);
    }

    private static string ResolvePresenterContract(string layerId) => layerId switch
    {
        "geosphere.plate" => WorldGenerationGraphDefaults.PlateLayerRendererContract,
        "geosphere.crust" => WorldGenerationGraphDefaults.CrustLayerRendererContract,
        _ => WorldGenerationGraphDefaults.DefaultLayerRendererContract,
    };
}
