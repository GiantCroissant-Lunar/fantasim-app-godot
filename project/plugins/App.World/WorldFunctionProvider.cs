using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.NodeGraph;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Crust;
using FantaSim.App.World.GenerationGraph;
using FantaSim.App.World.Globe;
using FantaSim.Geosphere.Crust;
using FantaSim.Geosphere.Plate.Topology;
using FantaSim.World.Contracts.Quantities;
using FantaSim.World.Contracts.Units;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UnifyCell;

namespace FantaSim.App.World;

/// <summary>
/// The World axis as a node-function provider: claims the world / geosphere / crust function families
/// and runs them as pure C# over the FantaSim geosphere packages. This is how World plugs into the
/// general node-graph paradigm — it contributes node functions (not a graph engine); the shared
/// <see cref="GraphExecutor"/> resolves them by function id, exactly as
/// <see cref="T:FantaSim.App.Iii.IiiFunctionProvider"/> does for the iii axis.
///
/// <para>v1 implements <c>crust.generate</c>: build the plate topology over a geodesic tessellation,
/// run the crust evolution pipeline, and return a JSON summary (cell count, boundary types, feature
/// counts, peak orogenic pressure). No Godot — this compiles and runs under plain <c>dotnet test</c>.</para>
/// </summary>
public sealed class WorldFunctionProvider : INodeFunctionProvider
{
    /// <summary>Function id for a source node that packages authored world-generation options.</summary>
    public const string WorldOptions = "world.options";

    /// <summary>Function id for a source node that packages pre-sphere body-formation products.</summary>
    public const string BodyFormation = "world.body-formation";

    /// <summary>Function id for a source node that packages regime/layer graph metadata.</summary>
    public const string LayerScope = "world.layer-scope";

    /// <summary>Function id for a source candidate that can feed a world layer.</summary>
    public const string LayerSource = "world.layer-source";

    /// <summary>Function id for normalizing one selected source into a renderer-facing layer product.</summary>
    public const string LayerNormalize = "world.layer-normalize";

    /// <summary>Function id for the crust-evolution pipeline run.</summary>
    public const string CrustGenerate = "crust.generate";

    /// <summary>Function id for the magma-ocean regime layer field-generation node.
    /// Delegates to <see cref="GeosphereMagmaOceanLayer"/> via <see cref="FieldValueResolver"/>,
    /// over the same <see cref="WorldGlobeGeometry"/> the composition runtime builds — so the
    /// graph-generated layer product is equal to the composition-generated one (P4b parity).</summary>
    public const string MagmaOceanGenerate = "geosphere.magma-ocean.generate";

    /// <summary>Function id for the stagnant-lid regime layer field-generation node.
    /// Delegates to <see cref="GeosphereStagnantLidLayer"/> via <see cref="FieldValueResolver"/>,
    /// over the same <see cref="WorldGlobeGeometry"/> the composition runtime builds — so the
    /// graph-generated layer product is equal to the composition-generated one (P4b parity).</summary>
    public const string StagnantLidGenerate = "geosphere.stagnant-lid.generate";

    private const double DefaultFormationTotalMassKg = 5.972e24;
    private const double DefaultFormationSpecificHeatJPerKg = 1.0e7;
    private const double DefaultFormationVolatileMassFraction = 0.02;

    private readonly ILogger _logger;

    public WorldFunctionProvider(ILoggerFactory? loggerFactory = null)
    {
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<WorldFunctionProvider>();
    }

    public bool Supports(string functionId)
        => functionId.StartsWith("world.", StringComparison.Ordinal)
           || functionId.StartsWith("geosphere.", StringComparison.Ordinal)
           || functionId.StartsWith("crust.", StringComparison.Ordinal);

    public async Task<JsonObject> InvokeAsync(
        string functionId,
        JsonObject payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        _logger.LogInformation("world invoke {FunctionId}", functionId);

        return functionId switch
        {
            WorldOptions => PackageWorldOptions(payload),
            BodyFormation => PackageBodyFormation(payload),
            LayerScope => PackageLayerScope(payload),
            LayerSource => PackageLayerSource(payload),
            LayerNormalize => PackageLayerNormalization(payload),
            CrustGenerate => await GenerateCrustAsync(payload, cancellationToken).ConfigureAwait(false),
            MagmaOceanGenerate => await GenerateRegimeLayerAsync(
                RegimeLayerKind.MagmaOcean, payload, cancellationToken).ConfigureAwait(false),
            StagnantLidGenerate => await GenerateRegimeLayerAsync(
                RegimeLayerKind.StagnantLid, payload, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException(
                $"WorldFunctionProvider has no handler for function '{functionId}'."),
        };
    }

    private static JsonObject PackageWorldOptions(JsonObject payload)
        => new()
        {
            ["options"] = payload.DeepClone(),
        };

    private static JsonObject PackageBodyFormation(JsonObject payload)
    {
        var regimeId = ReadString(payload, "regimeId", "planetesimal-swarm");
        var tick = ReadLong(payload, "canonicalTick", ReadLong(payload, "tick", 0));
        var seed = ReadInt(payload, "seed", 7);
        var totalMassKg = ReadDouble(payload, "totalMassKg", DefaultFormationTotalMassKg);
        var heatPerKg = ReadDouble(payload, "specificAccretionHeatJPerKg", DefaultFormationSpecificHeatJPerKg);
        var retainedHeatJ = ReadDouble(payload, "retainedHeatJ", totalMassKg * heatPerKg);
        var volatileMassFraction = ReadDouble(payload, "volatileMassFraction", DefaultFormationVolatileMassFraction);
        var retainedVolatileMassKg = ReadDouble(payload, "retainedVolatileMassKg", totalMassKg * volatileMassFraction);
        var shapeState = FormationShapeState(regimeId);
        var bodyCount = string.Equals(shapeState, "hydrostatic-spheroid", StringComparison.Ordinal) ? 1 : 10;
        var handoff = BuildSphereHandoffJson(
            tick,
            totalMassKg,
            retainedHeatJ,
            retainedVolatileMassKg,
            seed);

        // Right-sized body-formation bridge: mirror the ref constants that magma-ocean already inlines,
        // while deferring material-cell/body-scene contracts until the current app has that product model.
        // Sources: ref-projects/fantasim-app-godot/vault/architecture/body-formation-regime-graphs.md
        //          ref-projects/fantasim-app-godot/project/plugins/App.World/Composition/BodyFormationProducer.cs
        return new JsonObject
        {
            ["function"] = BodyFormation,
            ["tick"] = tick,
            ["phaseStartTick"] = 0,
            ["handoffTick"] = tick,
            ["scheduleKind"] = WorldRegimeScheduleKinds.BodyFormation,
            ["regimeId"] = regimeId,
            ["shapeState"] = shapeState,
            ["canonicalTick"] = tick,
            ["seed"] = seed,
            ["specificAccretionHeatJPerKg"] = heatPerKg,
            ["productAddress"] = new WorldGenerationProductAddress(
                Variant: "base",
                Branch: "main",
                Domain: "formation",
                Product: "body-set",
                Tick: tick).ToPath(),
            ["bodySet"] = new JsonObject
            {
                ["tick"] = tick,
                ["bodyCount"] = bodyCount,
                ["totalMassKg"] = totalMassKg,
                ["shapeState"] = shapeState,
            },
            ["handoff"] = handoff.DeepClone(),
            ["sphereHandoff"] = handoff,
        };
    }

    private static JsonObject BuildSphereHandoffJson(
        long tick,
        double totalMassKg,
        double retainedHeatJ,
        double retainedVolatileMassKg,
        int seed)
        => new()
        {
            ["tick"] = tick,
            ["sourceBodyId"] = "protoplanet",
            ["totalMassKg"] = totalMassKg,
            ["bulkCompositionFractions"] = new JsonArray
            {
                new JsonObject { ["componentId"] = "silicate", ["fraction"] = 0.68 },
                new JsonObject { ["componentId"] = "iron", ["fraction"] = 0.30 },
                new JsonObject { ["componentId"] = "volatile", ["fraction"] = 0.02 },
            },
            ["retainedHeatJ"] = retainedHeatJ,
            ["retainedVolatileMassKg"] = retainedVolatileMassKg,
            ["angularMomentum"] = new JsonArray(
                JsonValue.Create(0.0),
                JsonValue.Create(0.0),
                JsonValue.Create(0.0)),
            ["latentSubstrateSeed"] = $"geosphere/seed-{seed}",
        };

    private static JsonObject PackageLayerScope(JsonObject payload)
    {
        var sphereId = ReadString(payload, "sphereId", WorldGenerationGraphDefaults.GeosphereSphereId);
        var regimeId = ReadString(payload, "regimeId", "mobile-plate");
        var layerId = ReadString(payload, "layerId", "geosphere.crust");
        var role = ReadString(payload, "role", "layer");
        var tick = ReadLong(payload, "canonicalTick", ReadLong(payload, "tick", 0));
        var layer = new JsonObject
        {
            ["sphereId"] = sphereId,
            ["regimeId"] = regimeId,
            ["layerId"] = layerId,
            ["role"] = role,
            ["canonicalTick"] = tick,
        };

        return new JsonObject
        {
            ["function"] = LayerScope,
            ["scheduleKind"] = WorldRegimeScheduleKinds.Sphere,
            ["sphereId"] = sphereId,
            ["regimeId"] = regimeId,
            ["layerId"] = layerId,
            ["role"] = role,
            ["canonicalTick"] = tick,
            ["productAddress"] = new WorldGenerationProductAddress(
                Variant: "base",
                Branch: "main",
                Domain: sphereId,
                Product: $"{regimeId}.{layerId}",
                Tick: tick).ToPath(),
            ["layer"] = layer,
        };
    }

    private static JsonObject PackageLayerSource(JsonObject payload)
    {
        var sphereId = ReadString(payload, "sphereId", WorldGenerationGraphDefaults.GeosphereSphereId);
        var regimeId = ReadString(payload, "regimeId", "mobile-plate");
        var layerId = ReadString(payload, "layerId", "geosphere.crust");
        var sourceId = ReadString(payload, "sourceId", $"{layerId}.pcg");
        var sourceKind = ReadString(payload, "sourceKind", WorldLayerSourceKinds.Procedural);
        var normalizedProductKind = ReadString(payload, "normalizedProductKind", "world/layer");
        var rendererContract = ReadString(payload, "rendererContract", "world.globe.layer.v1");
        var availability = ReadString(payload, "availability", WorldLayerSourceAvailability.Available);
        var tick = ReadLong(payload, "canonicalTick", ReadLong(payload, "tick", 0));

        var source = new JsonObject
        {
            ["sourceId"] = sourceId,
            ["sourceKind"] = sourceKind,
            ["sphereId"] = sphereId,
            ["regimeId"] = regimeId,
            ["layerId"] = layerId,
            ["availability"] = availability,
            ["normalizedProductKind"] = normalizedProductKind,
            ["rendererContract"] = rendererContract,
            ["canonicalTick"] = tick,
        };

        AddOptionalString(source, payload, "bodyId");
        AddOptionalString(source, payload, "datasetId");
        AddOptionalString(source, payload, "providerId");
        AddOptionalString(source, payload, "importFormat");

        return new JsonObject
        {
            ["function"] = LayerSource,
            ["sourceId"] = sourceId,
            ["sourceKind"] = sourceKind,
            ["sphereId"] = sphereId,
            ["regimeId"] = regimeId,
            ["layerId"] = layerId,
            ["availability"] = availability,
            ["normalizedProductKind"] = normalizedProductKind,
            ["rendererContract"] = rendererContract,
            ["canonicalTick"] = tick,
            ["productAddress"] = new WorldGenerationProductAddress(
                Variant: "base",
                Branch: "main",
                Domain: sphereId,
                Product: $"{regimeId}.{layerId}.{sourceId}",
                Tick: tick).ToPath(),
            ["source"] = source,
        };
    }

    private static JsonObject PackageLayerNormalization(JsonObject payload)
    {
        var source = ReadObject(payload, "primarySource") ?? ReadObject(payload, "source");
        var sphereId = ReadString(payload, "sphereId", ReadStringOrDefault(source, "sphereId", WorldGenerationGraphDefaults.GeosphereSphereId));
        var regimeId = ReadString(payload, "regimeId", ReadStringOrDefault(source, "regimeId", "mobile-plate"));
        var layerId = ReadString(payload, "layerId", ReadStringOrDefault(source, "layerId", "geosphere.crust"));
        var sourceId = ReadString(payload, "selectedSourceId", ReadStringOrDefault(source, "sourceId", $"{layerId}.pcg"));
        var sourceKind = ReadString(payload, "selectedSourceKind", ReadStringOrDefault(source, "sourceKind", WorldLayerSourceKinds.Procedural));
        var normalizedProductKind = ReadString(payload, "normalizedProductKind", ReadStringOrDefault(source, "normalizedProductKind", "world/layer"));
        var rendererContract = ReadString(payload, "rendererContract", ReadStringOrDefault(source, "rendererContract", "world.globe.layer.v1"));
        var tick = ReadLong(payload, "canonicalTick", ReadLongOrDefault(source, "canonicalTick", ReadLong(payload, "tick", 0)));
        var productAddress = new WorldGenerationProductAddress(
            Variant: "base",
            Branch: "main",
            Domain: sphereId,
            Product: $"{regimeId}.{layerId}",
            Tick: tick).ToPath();

        return new JsonObject
        {
            ["function"] = LayerNormalize,
            ["sourceId"] = sourceId,
            ["sourceKind"] = sourceKind,
            ["sphereId"] = sphereId,
            ["regimeId"] = regimeId,
            ["layerId"] = layerId,
            ["normalizedProductKind"] = normalizedProductKind,
            ["rendererContract"] = rendererContract,
            ["canonicalTick"] = tick,
            ["productAddress"] = productAddress,
            ["normalizedLayer"] = new JsonObject
            {
                ["sourceId"] = sourceId,
                ["sourceKind"] = sourceKind,
                ["sphereId"] = sphereId,
                ["regimeId"] = regimeId,
                ["layerId"] = layerId,
                ["productKind"] = normalizedProductKind,
                ["rendererContract"] = rendererContract,
                ["productAddress"] = productAddress,
                ["canonicalTick"] = tick,
            },
        };
    }

    // ---------------------------------------------------------------- crust.generate

    private static async Task<JsonObject> GenerateCrustAsync(JsonObject payload, CancellationToken ct)
    {
        var spec = WorldCrustRunSpec.FromExecutionPayload(payload);
        var materialization = await WorldCrustMaterializer.MaterializeAsync(spec, ct).ConfigureAwait(false);

        return Summarize(
            functionId: CrustGenerate,
            spec.TessellationFrequency,
            spec.EndTick,
            spec.EndTick,
            materialization.Tessellation,
            materialization.Topology,
            materialization.Result,
            spec.SnapshotTicks);
    }

    private static JsonObject Summarize(
        string functionId,
        int frequency,
        long canonicalTick,
        long durationTicks,
        GeodesicSphereTessellation tessellation,
        PlateTopology topology,
        CrustEvolutionResult result,
        IReadOnlyList<long> snapshotTicks)
    {
        // Boundary type counts (how many inter-plate boundaries classified each way).
        var boundaryTypes = new JsonObject();
        foreach (var grp in topology.Boundaries.GroupBy(b => b.Type).OrderBy(g => g.Key.ToString(), StringComparer.Ordinal))
            boundaryTypes[grp.Key.ToString()] = grp.Count();

        bool activeBoundaries = topology.Boundaries.Any(
            b => b.Type is BoundaryType.Convergent or BoundaryType.Divergent or BoundaryType.Transform);

        // Use the latest requested snapshot for the top-line summary metrics.
        long summaryTick = snapshotTicks.Count > 0 ? snapshotTicks[^1] : canonicalTick;
        var features = result.FeaturesByTick.TryGetValue(summaryTick, out var f)
            ? f
            : new Dictionary<int, CrustFeature>();

        // Feature counts by kind (mountains / volcanic arcs / trenches / ridges / faults).
        var featureCounts = new JsonObject();
        foreach (var grp in features.Values
                     .GroupBy(x => x.Kind)
                     .OrderBy(g => g.Key.ToString(), StringComparer.Ordinal))
            featureCounts[grp.Key.ToString()] = grp.Count();

        int mountainCount = features.Values.Count(x => x.Kind == CrustFeatureKind.Mountain);
        int volcanoCount = features.Values.Count(x => x.Kind == CrustFeatureKind.VolcanicArc);

        // Peak accumulated orogenic pressure at the summary snapshot tick.
        double peakOrogenic = 0.0;
        if (result.StateByTick.TryGetValue(summaryTick, out var state) && state.Count > 0)
            peakOrogenic = state.Values.Max(s => s.OrogenicPressure);

        var productAddress = new WorldGenerationProductAddress(
            Variant: "base",
            Branch: "main",
            Domain: "geosphere",
            Product: "crust",
            Tick: canonicalTick).ToPath();

        var snapshotTickArray = new JsonArray();
        foreach (var t in snapshotTicks)
            snapshotTickArray.Add(t);

        var snapshotProducts = new JsonArray();
        foreach (var t in snapshotTicks)
        {
            snapshotProducts.Add(new WorldGenerationProductAddress(
                Variant: "base",
                Branch: "main",
                Domain: "geosphere",
                Product: "crust",
                Tick: t).ToPath());
        }

        return new JsonObject
        {
            ["function"] = functionId,
            ["productAddress"] = productAddress,
            ["snapshotTicks"] = snapshotTickArray,
            ["snapshotProducts"] = snapshotProducts,
            ["frequency"] = frequency,
            ["ticks"] = canonicalTick,
            ["canonicalTick"] = canonicalTick,
            // D4.1: canonical vocabulary only (ticks + odometer label + anchor rung), never Ma/MegaAnnum.
            // The anchor rung "ka" is 100,000 canonical ticks (numerically equal to the former 1 Ma anchor
            // at 100k ticks/Ma), so consumers dividing by ticksPerRung keep the same scale factor.
            ["durationTicks"] = durationTicks,
            ["durationLabel"] = CanonicalTimeLabel.ForTick(durationTicks, UnitConverter.TicksPerMegaAnnum),
            ["timeScale"] = new JsonObject
            {
                ["rung"] = BaselineScaleProfiles.GeospherePlateTime.AnchorScaleSymbol,
                ["ticksPerRung"] = UnitConverter.TicksPerMegaAnnum,
            },
            ["cellCount"] = tessellation.CellCount,
            ["plateCount"] = result.Plates.Count,
            ["boundaryCount"] = topology.Boundaries.Count,
            ["junctionCount"] = topology.Junctions.Count,
            ["activeBoundaries"] = activeBoundaries,
            ["boundaryTypes"] = boundaryTypes,
            ["featureCount"] = features.Count,
            ["featureCounts"] = featureCounts,
            ["mountainCount"] = mountainCount,
            ["volcanoCount"] = volcanoCount,
            ["peakOrogenicPressure"] = peakOrogenic,
        };
    }

    // ---------------------------------------------------------------- regime layer generation (P4b)

    private enum RegimeLayerKind { MagmaOcean, StagnantLid }

    private static async Task<JsonObject> GenerateRegimeLayerAsync(
        RegimeLayerKind kind, JsonObject payload, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var seed = ReadInt(payload, "seed", WorldGenerationRenderOptions.Default.Seed);
        var frequency = ReadInt(payload, "frequency", WorldGenerationRenderOptions.Default.TessellationFrequency);
        var tick = ReadLong(payload, "canonicalTick", ReadLong(payload, "tick", 0L));
        var onsetTick = ReadLong(
            payload, "plateOnsetTick", SphereRegimeScheduleDefaults.PlateOnsetTick);
        var retainedHeatJ = ReadDouble(payload, "retainedHeatJ", DefaultReferenceHeatJ);

        var handoff = new SphereHandoff(
            Tick: tick,
            SourceBodyId: "protoplanet",
            TotalMassKg: DefaultFormationTotalMassKg,
            BulkCompositionFractions: DefaultCompositionFractions,
            RetainedHeatJ: retainedHeatJ,
            RetainedVolatileMassKg: DefaultFormationTotalMassKg * DefaultFormationVolatileMassFraction,
            AngularMomentum: new UnifyMaths.Vector3D(0, 0, 0),
            LatentSubstrateSeed: $"geosphere/seed-{seed}");

        IFieldProducer layer = kind == RegimeLayerKind.MagmaOcean
            ? new GeosphereMagmaOceanLayer()
            : new GeosphereStagnantLidLayer(plateOnsetTick: onsetTick);

        var geometry = BuildRegimeLayerGeometry(seed, frequency, tick, onsetTick);
        var values = ResolveRegimeLayerFields(layer, geometry, tick, handoff);

        var regimeId = kind == RegimeLayerKind.MagmaOcean ? "magma-ocean" : "stagnant-lid";
        var layerId = kind == RegimeLayerKind.MagmaOcean
            ? "geosphere.magma-ocean"
            : "geosphere.stagnant-lid";
        var productAddress = new WorldGenerationProductAddress(
            Variant: "base",
            Branch: "main",
            Domain: WorldGenerationGraphDefaults.GeosphereSphereId,
            Product: $"{regimeId}.{layerId}",
            Tick: tick).ToPath();
        var functionId = kind == RegimeLayerKind.MagmaOcean ? MagmaOceanGenerate : StagnantLidGenerate;

        var fields = new JsonObject();
        foreach (var scalar in values.Scalars)
        {
            var arr = new JsonArray();
            foreach (var v in scalar.Values)
                arr.Add(v);
            fields[scalar.Field.Value] = arr;
        }

        return new JsonObject
        {
            ["function"] = functionId,
            ["regimeId"] = regimeId,
            ["layerId"] = layerId,
            ["sphereId"] = WorldGenerationGraphDefaults.GeosphereSphereId,
            ["canonicalTick"] = tick,
            ["seed"] = seed,
            ["frequency"] = frequency,
            ["plateOnsetTick"] = onsetTick,
            ["productAddress"] = productAddress,
            ["cellCount"] = values.Scalars.Count > 0 ? values.Scalars[0].Values.Count : 0,
            ["fields"] = fields,
        };
    }

    internal static WorldFieldValues ResolveRegimeLayerFields(
        IFieldProducer layer, WorldGlobeGeometry geometry, long tick, SphereHandoff? handoff)
    {
        var composer = new FieldComposer();
        GeosphereFieldCatalog.DeclareInto(composer);
        composer.AddLayer(layer.Fields);
        var composition = composer.Compose();
        if (!composition.IsValid)
        {
            var problems = string.Join("; ", composition.Errors.Select(e => e.Message));
            throw new InvalidOperationException(
                $"Regime layer '{layer.Id}' field composition is invalid: {problems}");
        }

        return new FieldValueResolver().Resolve(
            composition, new ILayer[] { layer }, geometry, tick, handoff);
    }

    internal static WorldGlobeGeometry BuildRegimeLayerGeometry(
        int seed, int frequency, long tick, long onsetTick)
    {
        var roster = OnsetRoster.Build(seed, onsetTick, frequency);
        var reconstructor = GlobeReconstructor.FromOnsetRoster(
            roster, onsetTick, SphereRegimeScheduleDefaults.GeosphereDefault, frequency);
        var snapshot = reconstructor.BuildGlobeAt(tick);
        return WorldCrustMaterializer.BuildGlobeGeometryFromSnapshot(snapshot);
    }

    private static readonly IReadOnlyList<FantaSim.App.World.Composition.MaterialCompositionFraction> DefaultCompositionFractions =
        new[]
        {
            new FantaSim.App.World.Composition.MaterialCompositionFraction("silicate", 0.68),
            new FantaSim.App.World.Composition.MaterialCompositionFraction("iron", 0.30),
            new FantaSim.App.World.Composition.MaterialCompositionFraction("volatile", 0.02),
        };

    private const double DefaultReferenceHeatJ = 5.972e31;

    // ---------------------------------------------------------------- payload decoding

    private static int ReadInt(JsonObject o, string key, int fallback)
        => o.TryGetPropertyValue(key, out var n) && n is JsonValue v && v.TryGetValue<int>(out var i) ? i : fallback;

    private static bool TryReadLong(JsonObject o, string key, out long value)
    {
        if (o.TryGetPropertyValue(key, out var n) && n is JsonValue v)
        {
            if (v.TryGetValue<long>(out value)) return true;
            if (v.TryGetValue<int>(out var i))
            {
                value = i;
                return true;
            }
            if (v.TryGetValue<string>(out var s) &&
                long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return true;
        }

        value = default;
        return false;
    }

    private static long ReadLong(JsonObject o, string key, long fallback)
    {
        if (TryReadLong(o, key, out var value)) return value;

        return fallback;
    }

    private static long ReadLongOrDefault(JsonObject? o, string key, long fallback)
        => o is null ? fallback : ReadLong(o, key, fallback);

    private static string ReadString(JsonObject o, string key, string fallback)
        => o.TryGetPropertyValue(key, out var node) && node is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : fallback;

    private static string ReadStringOrDefault(JsonObject? o, string key, string fallback)
        => o is null ? fallback : ReadString(o, key, fallback);

    private static JsonObject? ReadObject(JsonObject o, string key)
        => o.TryGetPropertyValue(key, out var node) && node is JsonObject value ? value : null;

    private static void AddOptionalString(JsonObject target, JsonObject source, string key)
    {
        var value = ReadString(source, key, string.Empty);
        if (!string.IsNullOrWhiteSpace(value))
            target[key] = value;
    }

    private static double ReadDouble(JsonObject o, string key, double fallback)
        => o.TryGetPropertyValue(key, out var n) ? ToDouble(n, fallback) : fallback;

    private static double ToDouble(JsonNode? node, double fallback)
    {
        if (node is JsonValue v)
        {
            if (v.TryGetValue<double>(out var d)) return d;
            if (v.TryGetValue<int>(out var i)) return i;
            if (v.TryGetValue<long>(out var l)) return l;
            if (v.TryGetValue<string>(out var s) &&
                double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var ds)) return ds;
        }

        return fallback;
    }

    private static string FormationShapeState(string regimeId) => regimeId switch
    {
        "dispersed-material" => "dispersed-material",
        "planetesimal-swarm" => "planetesimal-swarm",
        "multi-body-accretion" => "multi-body-accretion",
        "single-irregular-protobody" => "single-irregular-protobody",
        "hydrostatic-spheroid" => "hydrostatic-spheroid",
        _ => "planetesimal-swarm",
    };

}
