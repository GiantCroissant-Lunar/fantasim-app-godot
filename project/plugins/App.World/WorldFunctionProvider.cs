using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FantaSim.App.NodeGraph;
using FantaSim.App.World.GenerationGraph;
using FantaSim.Geosphere.Crust;
using FantaSim.Geosphere.Plate.Topology;
using FantaSim.World.Contracts.Time;
using FantaSim.World.Contracts.Units;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UnifyCell;
using UnifyGeometry.Spherical;
using UnifyMaths;
using TopoPlate = global::FantaSim.Geosphere.Plate.Topology.Plate;

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

    /// <summary>Function id for the crust-evolution pipeline run.</summary>
    public const string CrustGenerate = "crust.generate";

    // The proven 3-plate setup from fantasim-world's crust fixtures: three seeds 120 deg apart on the
    // equator give three OPEN boundary arcs meeting at the poles, so plate 0's spin drives a NET
    // convergence across the 0|1 boundary (no antipodal cancellation). Plates 0 and 1 continental ⇒
    // that convergent boundary is continent-continent ⇒ orogeny ⇒ mountains.
    private const int DefaultFrequency = 3;     // 20 * 3^2 = 1280 triangular cells
    private const double DefaultDurationMegaAnnum = 8.0;
    private const double DefaultSpinRateRadiansPerMegaAnnum = 0.02; // about +Z
    private const double DefaultOrogenicPerMegaAnnum = 1.0;
    private const double DefaultArcVolcanismPerMegaAnnum = 0.6;
    private const double DefaultIslandArcVolcanismPerMegaAnnum = 0.4;
    private const double DefaultRidgeVolcanismPerMegaAnnum = 0.5;
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
            CrustGenerate => await GenerateCrustAsync(payload, cancellationToken).ConfigureAwait(false),
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

    // ---------------------------------------------------------------- crust.generate

    private static async Task<JsonObject> GenerateCrustAsync(JsonObject payload, CancellationToken ct)
    {
        var effectivePayload = MergeNestedOptions(payload);
        int frequency = ReadInt(effectivePayload, "frequency", DefaultFrequency);
        long targetTick = ReadTargetTick(effectivePayload);
        double durationMegaAnnum = UnitConverter.TickDeltaToMegaAnnum(targetTick);
        double rate = ReadDouble(effectivePayload, "spinRateRadiansPerMegaAnnum",
            ReadDouble(effectivePayload, "spinRate", DefaultSpinRateRadiansPerMegaAnnum));
        var rates = ReadRates(effectivePayload);

        var tessellation = new GeodesicSphereTessellation(frequency);
        var plates = ReadPlates(effectivePayload, rate);
        var recipe = ReadRecipe(effectivePayload);

        var topology = PlateTopologyBuilder.Build(tessellation, plates);

        var result = await CrustPipeline.RunAsync(
            tessellation,
            plates,
            recipe,
            startTick: 0,
            endTick: targetTick,
            snapshotTicks: new[] { targetTick },
            rates: rates,
            ct: ct).ConfigureAwait(false);

        return Summarize(
            functionId: CrustGenerate,
            frequency,
            targetTick,
            durationMegaAnnum,
            tessellation,
            topology,
            result,
            targetTick);
    }

    private static JsonObject MergeNestedOptions(JsonObject payload)
    {
        var result = new JsonObject();
        if (payload.TryGetPropertyValue("options", out var optionsNode) && optionsNode is JsonObject options)
        {
            foreach (var kv in options)
                result[kv.Key] = kv.Value?.DeepClone();
        }

        foreach (var kv in payload)
        {
            if (string.Equals(kv.Key, "options", StringComparison.Ordinal))
                continue;
            result[kv.Key] = kv.Value?.DeepClone();
        }

        return result;
    }

    private static JsonObject Summarize(
        string functionId,
        int frequency,
        long canonicalTick,
        double durationMegaAnnum,
        GeodesicSphereTessellation tessellation,
        PlateTopology topology,
        CrustEvolutionResult result,
        long snapshotTick)
    {
        // Boundary type counts (how many inter-plate boundaries classified each way).
        var boundaryTypes = new JsonObject();
        foreach (var grp in topology.Boundaries.GroupBy(b => b.Type).OrderBy(g => g.Key.ToString(), StringComparer.Ordinal))
            boundaryTypes[grp.Key.ToString()] = grp.Count();

        bool activeBoundaries = topology.Boundaries.Any(
            b => b.Type is BoundaryType.Convergent or BoundaryType.Divergent or BoundaryType.Transform);

        var features = result.FeaturesByTick.TryGetValue(snapshotTick, out var f)
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

        // Peak accumulated orogenic pressure at the snapshot tick.
        double peakOrogenic = 0.0;
        if (result.StateByTick.TryGetValue(snapshotTick, out var state) && state.Count > 0)
            peakOrogenic = state.Values.Max(s => s.OrogenicPressure);

        return new JsonObject
        {
            ["function"] = functionId,
            ["frequency"] = frequency,
            ["ticks"] = canonicalTick,
            ["canonicalTick"] = canonicalTick,
            ["durationMegaAnnum"] = durationMegaAnnum,
            ["ticksPerMegaAnnum"] = UnitConverter.TicksPerMegaAnnum,
            ["timeScale"] = new JsonObject
            {
                ["unit"] = "Ma",
                ["tickScaleNumerator"] = UnitConverter.TicksPerMegaAnnum,
                ["tickScaleDenominator"] = 1,
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

    // ---------------------------------------------------------------- payload decoding

    /// <summary>
    /// Decode the plate set. Shape (all optional): <c>"plates": [{ "id":0, "lat":0, "lon":0,
    /// "axis":[0,0,1], "rate":0.02 }, ...]</c>. When absent, the proven 3-plate default is used:
    /// plate 0 spins about +Z at <paramref name="defaultRate"/>; plates 1 and 2 are still.
    /// </summary>
    private static IReadOnlyList<TopoPlate> ReadPlates(JsonObject payload, double defaultRate)
    {
        if (payload.TryGetPropertyValue("plates", out var node) && node is JsonArray arr && arr.Count > 0)
        {
            var plates = new List<TopoPlate>(arr.Count);
            foreach (var item in arr)
            {
                if (item is not JsonObject po) continue;
                int id = ReadInt(po, "id", plates.Count);
                double lat = ReadDouble(po, "lat", 0.0);
                double lon = ReadDouble(po, "lon", 0.0);
                var axis = ReadAxis(po, "axis", new Vector3D(0, 0, 1));
                double ratePerMegaAnnum = ReadDouble(po, "rate", 0.0);
                double ratePerTick = UnitConverter.RadiansPerMegaAnnumToRadiansPerTick(ratePerMegaAnnum);
                plates.Add(new TopoPlate(id, SphericalPoint.FromDegrees(lat, lon), new EulerPole(axis, ratePerTick)));
            }

            if (plates.Count > 0) return plates;
        }

        return DefaultThreePlates(defaultRate);
    }

    /// <summary>Three equatorial plates 120 deg apart; plate 0 spins eastward (0|1 converges, 0|2 diverges).
    /// <paramref name="ratePerMegaAnnum"/> is the authored spin in rad/Ma; converted to the engine's
    /// rad/tick <c>EulerPole.AngularRate</c> at this boundary (engine main is tick-native).</summary>
    private static IReadOnlyList<TopoPlate> DefaultThreePlates(double ratePerMegaAnnum)
    {
        double ratePerTick = UnitConverter.RadiansPerMegaAnnumToRadiansPerTick(ratePerMegaAnnum);
        return new[]
        {
            new TopoPlate(0, SphericalPoint.FromDegrees(0, 0), new EulerPole(new Vector3D(0, 0, 1), +ratePerTick)),
            new TopoPlate(1, SphericalPoint.FromDegrees(0, 120), new EulerPole(new Vector3D(0, 0, 1), 0.0)),
            new TopoPlate(2, SphericalPoint.FromDegrees(0, -120), new EulerPole(new Vector3D(0, 0, 1), 0.0)),
        };
    }

    /// <summary>
    /// Decode the crust-init recipe. <c>"continentalPlates": [0, 1]</c> designates those plate ids
    /// continental; the default makes plates 0 and 1 continental so the convergent 0|1 boundary is
    /// continent-continent (orogeny).
    /// </summary>
    private static CrustInitRecipe ReadRecipe(JsonObject payload)
    {
        if (payload.TryGetPropertyValue("continentalPlates", out var node) && node is JsonArray arr)
        {
            var ids = new HashSet<int>();
            foreach (var item in arr)
                if (item is JsonValue v && v.TryGetValue<int>(out var id)) ids.Add(id);
            return new CrustInitRecipe(ids);
        }

        return CrustInitRecipe.Continental(0, 1);
    }

    private static long ReadTargetTick(JsonObject payload)
    {
        if (TryReadLong(payload, "canonicalTick", out var canonicalTick))
            return NonNegativeTick(canonicalTick, "canonicalTick");
        if (TryReadLong(payload, "targetTick", out var targetTick))
            return NonNegativeTick(targetTick, "targetTick");
        if (TryReadDouble(payload, "durationMegaAnnum", out var durationMegaAnnum))
            return NonNegativeTick(GeologicTimeScale.FromMegaAnnumDelta(durationMegaAnnum), "durationMegaAnnum");
        if (TryReadDouble(payload, "durationMa", out var durationMa))
            return NonNegativeTick(GeologicTimeScale.FromMegaAnnumDelta(durationMa), "durationMa");
        if (TryReadLong(payload, "ticks", out var legacyTicks))
            return NonNegativeTick(legacyTicks, "ticks");

        return GeologicTimeScale.FromMegaAnnumDelta(DefaultDurationMegaAnnum);
    }

    private static CrustEvolutionRates ReadRates(JsonObject payload) => new(
        OrogenicPerTick: ReadRatePerTick(payload, "orogenicPerMegaAnnum", "orogenicPerTick", DefaultOrogenicPerMegaAnnum),
        ArcVolcanismPerTick: ReadRatePerTick(payload, "arcVolcanismPerMegaAnnum", "arcVolcanismPerTick", DefaultArcVolcanismPerMegaAnnum),
        IslandArcVolcanismPerTick: ReadRatePerTick(payload, "islandArcVolcanismPerMegaAnnum", "islandArcVolcanismPerTick", DefaultIslandArcVolcanismPerMegaAnnum),
        RidgeVolcanismPerTick: ReadRatePerTick(payload, "ridgeVolcanismPerMegaAnnum", "ridgeVolcanismPerTick", DefaultRidgeVolcanismPerMegaAnnum));

    private static double ReadRatePerTick(JsonObject payload, string perMegaAnnumKey, string perTickKey, double defaultPerMegaAnnum)
    {
        if (TryReadDouble(payload, perTickKey, out var perTick))
            return perTick;

        var perMegaAnnum = ReadDouble(payload, perMegaAnnumKey, defaultPerMegaAnnum);
        return perMegaAnnum / UnitConverter.TicksPerMegaAnnum;
    }

    private static long NonNegativeTick(long tick, string fieldName)
    {
        if (tick < 0)
            throw new ArgumentOutOfRangeException(fieldName, "Canonical ticks must be non-negative.");
        return tick;
    }

    private static Vector3D ReadAxis(JsonObject po, string key, Vector3D fallback)
    {
        if (po.TryGetPropertyValue(key, out var node) && node is JsonArray arr && arr.Count == 3)
        {
            double x = ToDouble(arr[0], fallback.X);
            double y = ToDouble(arr[1], fallback.Y);
            double z = ToDouble(arr[2], fallback.Z);
            return new Vector3D(x, y, z);
        }

        return fallback;
    }

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

    private static string ReadString(JsonObject o, string key, string fallback)
        => o.TryGetPropertyValue(key, out var node) && node is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : fallback;

    private static double ReadDouble(JsonObject o, string key, double fallback)
        => o.TryGetPropertyValue(key, out var n) ? ToDouble(n, fallback) : fallback;

    private static bool TryReadDouble(JsonObject o, string key, out double value)
    {
        if (o.TryGetPropertyValue(key, out var n) && n is JsonValue)
        {
            value = ToDouble(n, double.NaN);
            return !double.IsNaN(value);
        }

        value = default;
        return false;
    }

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
