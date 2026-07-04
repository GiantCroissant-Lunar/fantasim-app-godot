using System;
using System.Collections.Generic;
using FantaSim.App.World.Dto;
using FantaSim.Cartography.Globe;
using FantaSim.Cartography.Globe.Core;
using FantaSim.Cartography.Shared;

namespace FantaSim.App.World.Globe;

/// <summary>
/// Render-only relief amplification that samples noise through the nearest crust-feature context.
/// Simulation truth remains the coarse elevation field; this only changes deterministic presentation
/// detail at fixed and adaptive globe vertices.
/// </summary>
public sealed class TectonicDetailSampler
{
    public const double DefaultInteriorAmplitudeMultiplier = 0.28;

    private const double TieEpsilon = 1e-12;

    private readonly CellContext[] _contexts;
    private readonly NoiseParams _baseNoise;
    private readonly double _interiorAmplitudeMultiplier;
    private readonly bool _ridgeActiveFeatures;
    private readonly double _activeAmplitudeMultiplier;

    public TectonicDetailSampler(
        WorldGlobeSnapshot snapshot,
        IReadOnlyList<CellCrustFeature>? features,
        NoiseParams baseNoise,
        double interiorAmplitudeMultiplier = DefaultInteriorAmplitudeMultiplier,
        bool ridgeActiveFeatures = true,
        double activeAmplitudeMultiplier = 1.0)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(baseNoise);
        if (interiorAmplitudeMultiplier < 0.0 || !double.IsFinite(interiorAmplitudeMultiplier))
        {
            throw new ArgumentOutOfRangeException(
                nameof(interiorAmplitudeMultiplier),
                interiorAmplitudeMultiplier,
                "Interior amplitude multiplier must be finite and non-negative.");
        }
        if (activeAmplitudeMultiplier < 0.0 || !double.IsFinite(activeAmplitudeMultiplier))
        {
            throw new ArgumentOutOfRangeException(
                nameof(activeAmplitudeMultiplier),
                activeAmplitudeMultiplier,
                "Active amplitude multiplier must be finite and non-negative.");
        }

        _baseNoise = baseNoise;
        _interiorAmplitudeMultiplier = interiorAmplitudeMultiplier;
        _ridgeActiveFeatures = ridgeActiveFeatures;
        _activeAmplitudeMultiplier = activeAmplitudeMultiplier;
        _contexts = new CellContext[snapshot.Cells.Count];
        for (int i = 0; i < snapshot.Cells.Count; i++)
        {
            var cell = snapshot.Cells[i];
            var feature = cell.CellId >= 0 && features is not null && cell.CellId < features.Count
                ? features[cell.CellId]
                : default;
            _contexts[i] = new CellContext(cell.CellId, CellCenter(cell), feature);
        }
    }

    public double Sample(CartesianPoint3 position)
        => NoiseRelief.Sample(position, ResolveProfile(position).Noise);

    public TectonicDetailProfile ResolveProfile(CartesianPoint3 position)
    {
        if (_baseNoise.Amplitude == 0.0 || _contexts.Length == 0)
            return new TectonicDetailProfile(0, 0.0, _baseNoise with { Amplitude = 0.0 });

        var context = FindNearest(position);
        double weight = FeatureWeight(context.Feature);
        var noise = BuildNoiseProfile(
            _baseNoise,
            context.Feature.Kind,
            weight,
            _interiorAmplitudeMultiplier,
            _ridgeActiveFeatures,
            _activeAmplitudeMultiplier);
        return new TectonicDetailProfile(context.Feature.Kind, weight, noise);
    }

    private CellContext FindNearest(CartesianPoint3 position)
    {
        var unit = NormalizeOr(position, new CartesianPoint3(1.0, 0.0, 0.0));
        var best = _contexts[0];
        double bestDot = Dot(unit, best.Center);

        for (int i = 1; i < _contexts.Length; i++)
        {
            var candidate = _contexts[i];
            double dot = Dot(unit, candidate.Center);
            if (dot > bestDot + TieEpsilon
                || (Math.Abs(dot - bestDot) <= TieEpsilon && candidate.CellId < best.CellId))
            {
                best = candidate;
                bestDot = dot;
            }
        }

        return best;
    }

    private static NoiseParams BuildNoiseProfile(
        NoiseParams baseNoise,
        byte featureKind,
        double featureWeight,
        double interiorAmplitudeMultiplier,
        bool ridgeActiveFeatures,
        double activeAmplitudeMultiplier)
    {
        double interiorAmplitude = baseNoise.Amplitude * interiorAmplitudeMultiplier;
        if (featureKind == 0 || featureWeight <= 0.0)
        {
            return baseNoise with
            {
                BaseFrequency = Math.Max(1.0, baseNoise.BaseFrequency * 0.80),
                Amplitude = interiorAmplitude,
                Ridged = false,
            };
        }

        double activeAmplitude = featureKind switch
        {
            1 => baseNoise.Amplitude * 1.45, // Mountain
            2 => baseNoise.Amplitude * 1.25, // Volcanic arc
            3 => baseNoise.Amplitude * 1.10, // Trench
            4 => baseNoise.Amplitude * 1.35, // Ridge
            5 => baseNoise.Amplitude * 0.72, // Fault / transform scarp
            _ => baseNoise.Amplitude * 0.55,
        } * activeAmplitudeMultiplier;

        double frequencyMultiplier = featureKind switch
        {
            1 => 0.72,
            2 => 0.88,
            3 => 0.82,
            4 => 1.10,
            5 => 1.35,
            _ => 1.0,
        };

        return baseNoise with
        {
            BaseFrequency = Math.Max(1.0, baseNoise.BaseFrequency * frequencyMultiplier),
            Amplitude = Lerp(interiorAmplitude, activeAmplitude, featureWeight),
            Ridged = ridgeActiveFeatures && featureKind is 1 or 2 or 3 or 4,
        };
    }

    private static double FeatureWeight(CellCrustFeature feature)
    {
        if (feature.Kind == 0)
            return 0.0;

        double magnitude = Math.Max(0.0, feature.Magnitude);
        return Math.Clamp(0.45 + (Math.Log10(1.0 + magnitude) / 2.5), 0.0, 1.0);
    }

    private static CartesianPoint3 CellCenter(GlobeCell cell)
    {
        var p = new CartesianPoint3(
            cell.C0.X + cell.C1.X + cell.C2.X,
            cell.C0.Y + cell.C1.Y + cell.C2.Y,
            cell.C0.Z + cell.C1.Z + cell.C2.Z);
        return NormalizeOr(p, new CartesianPoint3(cell.C0.X, cell.C0.Y, cell.C0.Z));
    }

    private static CartesianPoint3 NormalizeOr(CartesianPoint3 value, CartesianPoint3 fallback)
    {
        double len = Math.Sqrt((value.X * value.X) + (value.Y * value.Y) + (value.Z * value.Z));
        if (len <= 1e-12)
            return fallback;
        return new CartesianPoint3(value.X / len, value.Y / len, value.Z / len);
    }

    private static double Dot(CartesianPoint3 a, CartesianPoint3 b)
        => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    private static double Lerp(double a, double b, double t)
        => a + ((b - a) * Math.Clamp(t, 0.0, 1.0));

    private readonly record struct CellContext(
        int CellId,
        CartesianPoint3 Center,
        CellCrustFeature Feature);
}

/// <summary>
/// The resolved relief profile at a sampled position, exposed so tests and future diagnostics can
/// verify which crust context shaped the render-only detail.
/// </summary>
public sealed record TectonicDetailProfile(byte FeatureKind, double FeatureWeight, NoiseParams Noise);
