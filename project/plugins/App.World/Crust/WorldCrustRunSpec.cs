using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using FantaSim.App.Ecs.Systems;
using FantaSim.App.World.Composition;
using FantaSim.App.World.GenerationGraph;
using FantaSim.App.World.Topography;
using FantaSim.Geosphere.Crust;
using FantaSim.Geosphere.Plate.Topology;
using FantaSim.World.Contracts.Time;
using FantaSim.World.Contracts.Units;
using UnifyCell;
using UnifyGeometry.Spherical;
using UnifyMaths;
using TopoPlate = FantaSim.Geosphere.Plate.Topology.Plate;

namespace FantaSim.App.World.Crust;

internal sealed record WorldCrustRunSpec(
    int Seed,
    int TessellationFrequency,
    long StartTick,
    long EndTick,
    long RotationReferenceTick,
    IReadOnlyList<long> SnapshotTicks,
    IReadOnlyList<TopoPlate> Plates,
    CrustInitRecipe Recipe,
    CrustEvolutionRates Rates,
    BoundaryProfileParameters BoundaryProfiles,
    double VerticalExaggeration,
    CellElevationHydrosphereMode HydrosphereMode,
    CrustPatchRecipe? PatchRecipe = null,
    RotationSourceRecipe? RotationSource = null)
{
    private static readonly CrustInitRecipe DefaultContinentalRecipe = CrustInitRecipe.Continental(0, 1);
    private static readonly CrustPatchRecipe DefaultPatchRecipe = new(Seed: 0, PatchCount: 5);

    private const double DefaultDurationMegaAnnum = 8.0;
    // ONE spin-rate property end-to-end (user decision 2026-07-11): the default is the calibrated
    // OnsetRoster source of truth, not a local copy. Provenance:
    // tools/rates/2026-07-07-rate-calibration-report.md.
    private const double DefaultSpinRateRadiansPerMegaAnnum = OnsetRoster.DefaultAngularDriftPerMegaAnnum;
    private const double DefaultOrogenicPerMegaAnnum = 1.0;
    private const double DefaultArcVolcanismPerMegaAnnum = 0.6;
    private const double DefaultIslandArcVolcanismPerMegaAnnum = 0.4;
    private const double DefaultRidgeVolcanismPerMegaAnnum = 0.5;

    public double DurationMegaAnnum => UnitConverter.TickDeltaToMegaAnnum(EndTick);

    public GeodesicSphereTessellation CreateTessellation()
        => new(TessellationFrequency);

    public static WorldCrustRunSpec FromExecutionPayload(JsonObject payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var effectivePayload = MergeNestedOptions(payload);
        long targetTick = ReadTargetTick(effectivePayload);
        var snapshotTicks = ReadSnapshotTicks(effectivePayload, targetTick);
        long endTick = snapshotTicks.Count > 0 ? snapshotTicks.Max() : targetTick;
        int seed = ReadInt(effectivePayload, "seed", WorldGenerationRenderOptions.Default.Seed);
        int frequency = ReadInt(
            effectivePayload,
            "frequency",
            WorldGenerationRenderOptions.Default.TessellationFrequency);
        double rate = ReadDouble(
            effectivePayload,
            "spinRateRadiansPerMegaAnnum",
            ReadDouble(effectivePayload, "spinRate", DefaultSpinRateRadiansPerMegaAnnum));
        var plates = ReadPlates(effectivePayload, frequency, seed, rate);
        var patchRecipe = ReadPatchRecipe(effectivePayload, seed);
        var rotationSource = ReadRotationSourceRecipe(effectivePayload);

        return new WorldCrustRunSpec(
            Seed: seed,
            TessellationFrequency: frequency,
            StartTick: 0L,
            EndTick: endTick,
            RotationReferenceTick: ReadLong(effectivePayload, "rotationReferenceTick", 0L),
            SnapshotTicks: snapshotTicks,
            Plates: plates,
            Recipe: ReadRecipe(effectivePayload),
            Rates: ReadRates(effectivePayload),
            BoundaryProfiles: BoundaryProfileParameters.Default,
            VerticalExaggeration: WorldGenerationRenderOptions.DefaultVerticalExaggeration,
            HydrosphereMode: ReadHydrosphereMode(effectivePayload, WorldGenerationRenderOptions.Default.HydrosphereMode),
            PatchRecipe: patchRecipe,
            RotationSource: rotationSource);
    }

    /// <summary>
    /// The resolved continental-plate id set the presentation document surfaces (M0, spec D2).
    /// Mirrors the <see cref="CrustInitRecipe"/> used by <see cref="ForPresentation"/> so the Continents
    /// view and the crust pipeline share one truth. Default <c>CrustInitRecipe.Continental(0, 1)</c>.
    /// </summary>
    [Obsolete("Patch-based seeding makes land/ocean a per-cell fraction; keep only for compat until A3 removes the ContinentalPlateIds field.")]
    public static IReadOnlySet<int> ContinentalPlateIdsForPresentation(
        WorldGenerationRenderOptions renderOptions,
        long onsetTick)
    {
        ArgumentNullException.ThrowIfNull(renderOptions);
        if (onsetTick < 0) throw new ArgumentOutOfRangeException(nameof(onsetTick));
        return DefaultContinentalRecipe.ContinentalPlateIds;
    }

    public static WorldCrustRunSpec ForPresentation(
        WorldGenerationRenderOptions renderOptions,
        long onsetTick,
        long referenceTick)
    {
        ArgumentNullException.ThrowIfNull(renderOptions);
        if (onsetTick < 0) throw new ArgumentOutOfRangeException(nameof(onsetTick));
        if (referenceTick < 0) throw new ArgumentOutOfRangeException(nameof(referenceTick));

        var roster = OnsetRoster.Build(
            renderOptions.Seed,
            onsetTick,
            renderOptions.TessellationFrequency,
            renderOptions.SpinRateRadiansPerMegaAnnum);
        var plates = roster.SeedPlatesAt(onsetTick);
        if (plates.Count == 0)
            throw new ArgumentException("Presentation crust run spec requires seed plates at onset.", nameof(onsetTick));

        return new WorldCrustRunSpec(
            Seed: renderOptions.Seed,
            TessellationFrequency: renderOptions.TessellationFrequency,
            StartTick: 0L,
            EndTick: referenceTick,
            RotationReferenceTick: onsetTick,
            SnapshotTicks: new[] { referenceTick },
            Plates: plates,
            Recipe: DefaultContinentalRecipe,
            Rates: CreateDefaultRates(),
            BoundaryProfiles: renderOptions.BoundaryProfiles,
            VerticalExaggeration: renderOptions.VerticalExaggeration,
            HydrosphereMode: renderOptions.HydrosphereMode);
    }

    public static WorldCrustRunSpec ForReconstructor(
        int tessellationFrequency,
        long rotationReferenceTick,
        IReadOnlyList<long> snapshotTicks,
        IReadOnlyList<TopoPlate> plates)
    {
        ArgumentNullException.ThrowIfNull(snapshotTicks);
        ArgumentNullException.ThrowIfNull(plates);
        if (tessellationFrequency < 0) throw new ArgumentOutOfRangeException(nameof(tessellationFrequency));
        if (rotationReferenceTick < 0) throw new ArgumentOutOfRangeException(nameof(rotationReferenceTick));
        if (snapshotTicks.Count == 0)
            throw new ArgumentException("Reconstructor crust run spec requires at least one snapshot tick.", nameof(snapshotTicks));

        // Reconstructor callers already own the plates; Seed is not used to regenerate them on this path.
        return new WorldCrustRunSpec(
            Seed: 0,
            TessellationFrequency: tessellationFrequency,
            StartTick: 0L,
            EndTick: snapshotTicks.Max(),
            RotationReferenceTick: rotationReferenceTick,
            SnapshotTicks: snapshotTicks,
            Plates: plates,
            Recipe: DefaultContinentalRecipe,
            Rates: CreateDefaultRates(),
            BoundaryProfiles: BoundaryProfileParameters.Default,
            VerticalExaggeration: WorldGenerationRenderOptions.DefaultVerticalExaggeration,
            HydrosphereMode: WorldGenerationRenderOptions.Default.HydrosphereMode);
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

    private static IReadOnlyList<TopoPlate> ReadPlates(
        JsonObject payload,
        int frequency,
        int seed,
        double defaultRate)
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

        return DefaultOnsetPlates(seed, frequency, defaultRate);
    }

    private static IReadOnlyList<TopoPlate> DefaultOnsetPlates(
        int seed,
        int frequency,
        double spinRateRadiansPerMegaAnnum)
    {
        var onsetTick = SphereRegimeScheduleDefaults.PlateOnsetTick;
        var roster = OnsetRoster.Build(seed, onsetTick, frequency, spinRateRadiansPerMegaAnnum);
        var plates = roster.SeedPlatesAt(onsetTick);
        if (plates.Count > 0) return plates;

        double ratePerTick = UnitConverter.RadiansPerMegaAnnumToRadiansPerTick(spinRateRadiansPerMegaAnnum);
        return new[]
        {
            new TopoPlate(0, SphericalPoint.FromDegrees(0, 0), new EulerPole(new Vector3D(0, 0, 1), +ratePerTick)),
            new TopoPlate(1, SphericalPoint.FromDegrees(0, 120), new EulerPole(new Vector3D(0, 0, 1), 0.0)),
            new TopoPlate(2, SphericalPoint.FromDegrees(0, -120), new EulerPole(new Vector3D(0, 0, 1), 0.0)),
        };
    }

    private static CrustInitRecipe ReadRecipe(JsonObject payload)
    {
        if (payload.TryGetPropertyValue("continentalPlates", out var platesNode) && platesNode is JsonArray arr)
        {
            var ids = new HashSet<int>();
            foreach (var item in arr)
                if (item is JsonValue v && v.TryGetValue<int>(out var id)) ids.Add(id);
            return new CrustInitRecipe(ids);
        }

        return DefaultContinentalRecipe;
    }

    /// <summary>
    /// Null when <c>continentalPatches</c> is not authored: the spec's PatchRecipe gates which
    /// init the crust pipeline runs (null &rarr; recipe-based, non-null &rarr; patch-based), so
    /// only an explicitly authored object may switch the default path off recipe seeding.
    /// </summary>
    public static CrustPatchRecipe? ReadPatchRecipe(JsonObject payload, int worldSeed)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (payload.TryGetPropertyValue("continentalPatches", out var patchesNode) && patchesNode is JsonObject patchObj)
        {
            int seed = ReadInt(patchObj, "seed", worldSeed);
            int count = ReadInt(patchObj, "count", DefaultPatchRecipe.PatchCount);
            double meanRadiusDeg = ReadDouble(patchObj, "meanRadiusDeg", DefaultPatchRecipe.MeanAngularRadiusRad * 180.0 / Math.PI);
            double radiusJitter = ReadDouble(patchObj, "radiusJitter", DefaultPatchRecipe.RadiusJitter);
            double edgeNoise = ReadDouble(patchObj, "edgeNoise", DefaultPatchRecipe.EdgeNoiseAmplitude);

            return new CrustPatchRecipe(
                seed,
                count,
                meanRadiusDeg * Math.PI / 180.0,
                radiusJitter,
                edgeNoise);
        }

        return null;
    }

    /// <summary>
    /// Reads the rotation-source selection from the <c>rotationSource</c> option, mirroring
    /// <see cref="ReadPatchRecipe(JsonObject, int)"/>. Absent or <c>"generated"</c> &rarr; default
    /// (unchanged generated Euler poles). <c>"imported"</c> requires an inline <c>payload</c>
    /// carrying a PLATES4 <c>.rot</c> model; a missing/empty payload throws a clear
    /// <see cref="ArgumentException"/> rather than crashing downstream.
    /// </summary>
    public static RotationSourceRecipe ReadRotationSourceRecipe(JsonObject payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (!payload.TryGetPropertyValue("rotationSource", out var node) || node is not JsonObject rs)
            return RotationSourceRecipe.Default;

        string kind = ReadString(rs, "kind", "generated").Trim().ToLowerInvariant();
        if (kind is "generated" or "gen" or "default")
            return RotationSourceRecipe.Default;

        if (kind is not ("imported" or "rot" or "gplates"))
        {
            throw new ArgumentException(
                $"rotationSource.kind '{kind}' is not recognized. Expected 'generated' or 'imported'.",
                nameof(payload));
        }

        string? rotText = ReadOptionalString(rs, "payload");
        string nameField = ReadString(rs, "name", "imported");
        string sourceName = string.IsNullOrWhiteSpace(nameField) ? "imported" : nameField;

        if (string.IsNullOrWhiteSpace(rotText))
        {
            throw new ArgumentException(
                "rotationSource.kind 'imported' requires a non-empty 'payload' carrying a PLATES4 .rot model.",
                nameof(payload));
        }

        return new RotationSourceRecipe(RotationSourceKind.Imported, rotText, sourceName);
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

    private static CrustEvolutionRates CreateDefaultRates()
        => new(
            OrogenicPerTick: DefaultOrogenicPerMegaAnnum / UnitConverter.TicksPerMegaAnnum,
            ArcVolcanismPerTick: DefaultArcVolcanismPerMegaAnnum / UnitConverter.TicksPerMegaAnnum,
            IslandArcVolcanismPerTick: DefaultIslandArcVolcanismPerMegaAnnum / UnitConverter.TicksPerMegaAnnum,
            RidgeVolcanismPerTick: DefaultRidgeVolcanismPerMegaAnnum / UnitConverter.TicksPerMegaAnnum);

    private static double ReadRatePerTick(
        JsonObject payload,
        string perMegaAnnumKey,
        string perTickKey,
        double defaultPerMegaAnnum)
    {
        if (TryReadDouble(payload, perTickKey, out var perTick))
            return perTick;

        var perMegaAnnum = ReadDouble(payload, perMegaAnnumKey, defaultPerMegaAnnum);
        return perMegaAnnum / UnitConverter.TicksPerMegaAnnum;
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

    private static IReadOnlyList<long> ReadSnapshotTicks(JsonObject payload, long targetTick)
    {
        if (payload.TryGetPropertyValue("snapshotTicks", out var node) && node is JsonArray array)
        {
            var ticks = new List<long>(array.Count);
            foreach (var item in array)
            {
                if (item is JsonValue value)
                {
                    if (value.TryGetValue<long>(out var l))
                        ticks.Add(l);
                    else if (value.TryGetValue<int>(out var i))
                        ticks.Add(i);
                }
            }

            if (ticks.Count > 0)
                return ticks;
        }

        return new[] { targetTick };
    }

    private static int ReadInt(JsonObject o, string key, int fallback)
        => o.TryGetPropertyValue(key, out var n) && n is JsonValue v && v.TryGetValue<int>(out var i) ? i : fallback;

    private static string ReadString(JsonObject o, string key, string fallback)
        => o.TryGetPropertyValue(key, out var n) && n is JsonValue v && v.TryGetValue<string>(out var s)
            ? s
            : fallback;

    private static string? ReadOptionalString(JsonObject o, string key)
        => o.TryGetPropertyValue(key, out var n) && n is JsonValue v && v.TryGetValue<string>(out var s)
            ? s
            : null;

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
        if (node is not JsonValue value)
            return fallback;
        if (value.TryGetValue<double>(out var d))
            return d;
        if (value.TryGetValue<float>(out var f))
            return f;
        if (value.TryGetValue<int>(out var i))
            return i;
        if (value.TryGetValue<long>(out var l))
            return l;
        if (value.TryGetValue<string>(out var s) &&
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return fallback;
    }
}
