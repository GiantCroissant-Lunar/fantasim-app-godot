using System;
using System.Collections.Generic;
using System.Globalization;

namespace FantaSim.App.World.Composition;

/// <summary>
/// Pure, Godot-free helper for the cutaway stratum bands on the cut faces (§5c, W3a). The cut faces
/// render the planet as flat strata: an outer CRUST band whose per-cell thickness comes from the
/// world's <c>crust-thickness-m</c> field (the one honest band — TRUTH), a LITHOSPHERE lid below it
/// (plain, clearly-not-data warm-dark), and a MANTLE interior (plain, darker). Stratum thickness uses
/// a DECLARED exaggeration parameter (S1) — separate from the world view's sqrt height lens (§5c-i):
/// the cut faces have their own labeled exaggeration; the two do not entangle.
///
/// <para>The S2 indicator (<see cref="FormatExaggerationIndicator"/>) names this exaggeration so the
/// on-screen scale ladder shows it honestly (mirrors <see cref="VerticalScaleLabel"/>).</para>
///
/// <para>Colors follow the no-sphere-costume rule: strata must not read as another sphere's subject.
/// No blue. The crust is a warm earthy tone (the one truth band); lithosphere and mantle are warm
/// dark, clearly-not-data tones so the eye distinguishes the single honest band from the schematic
/// interior.</para>
/// </summary>
public static class CutawayStratumProfile
{
    public const double DefaultCrustThicknessMetres = 30_000.0;
    public const double DefaultLithosphereLidThicknessMetres = 90_000.0;

    // Warm earthy crust tone — the one truth band (not data; it IS the data band).
    public static readonly StratumColor CrustColor = new(0.42, 0.28, 0.16);

    // Warm dark lithosphere — clearly-not-data schematic lid (no truth field yet).
    public static readonly StratumColor LithosphereColor = new(0.18, 0.10, 0.06);

    // Warm dark mantle — clearly-not-data schematic interior (no truth field yet).
    public static readonly StratumColor MantleColor = new(0.08, 0.04, 0.03);

    /// <summary>
    /// Computes the three stratum bands (crust, lithosphere, mantle) as concentric radii on a
    /// unit globe, given the crust thickness, lithosphere lid thickness, and the declared
    /// exaggeration. The crust band's outer radius is 1.0 (the surface); the mantle extends to 0.0
    /// (the planet center). Bands are contiguous: each band's inner radius is the next band's outer.
    /// </summary>
    public static IReadOnlyList<StratumBand> ComputeBands(
        double crustThicknessMetres,
        double lithosphereLidThicknessMetres,
        double exaggeration,
        double planetRadiusMetres)
    {
        if (exaggeration <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(exaggeration), exaggeration, "Cutaway exaggeration must be positive.");
        if (planetRadiusMetres <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(planetRadiusMetres), planetRadiusMetres, "Planet radius must be positive.");

        double surfaceRadius = 1.0;
        double crustInner = surfaceRadius - (crustThicknessMetres * exaggeration / planetRadiusMetres);
        double lithoInner = crustInner - (lithosphereLidThicknessMetres * exaggeration / planetRadiusMetres);
        double mantleInner = 0.0;

        return new[]
        {
            new StratumBand("crust", CrustColor, OuterRadius: surfaceRadius, InnerRadius: crustInner),
            new StratumBand("lithosphere", LithosphereColor, OuterRadius: crustInner, InnerRadius: Math.Max(0.0, lithoInner)),
            new StratumBand("mantle", MantleColor, OuterRadius: Math.Max(0.0, lithoInner), InnerRadius: mantleInner),
        };
    }

    /// <summary>
    /// Per-cell crust band inner radii from per-cell crust thickness values (the truth field).
    /// Each cell's crust inner radius = 1.0 - (thickness[cell] * exaggeration / planetRadius).
    /// </summary>
    public static IReadOnlyList<double> ComputePerCellCrustInnerRadii(
        IReadOnlyList<double> crustThicknessMetresByCell,
        double exaggeration,
        double planetRadiusMetres)
    {
        if (exaggeration <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(exaggeration), exaggeration, "Cutaway exaggeration must be positive.");
        if (planetRadiusMetres <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(planetRadiusMetres), planetRadiusMetres, "Planet radius must be positive.");

        var result = new double[crustThicknessMetresByCell.Count];
        for (int i = 0; i < crustThicknessMetresByCell.Count; i++)
            result[i] = 1.0 - (crustThicknessMetresByCell[i] * exaggeration / planetRadiusMetres);
        return result;
    }

    /// <summary>
    /// Formats the S2 indicator suffix for the cutaway stratum exaggeration, appended to the status
    /// label when the cutaway is active. Mirrors <see cref="VerticalScaleLabel.BuildIndicatorSuffix"/>
    /// but names the cutaway so the two exaggerations (surface lens vs cutaway strata) are distinct.
    /// </summary>
    public static string FormatExaggerationIndicator(double exaggeration)
    {
        if (exaggeration <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(exaggeration), exaggeration, "Cutaway exaggeration must be positive.");

        return $"  |  cutaway x{HumanizeFactor(exaggeration)} units";
    }

    private static string HumanizeFactor(double factor)
    {
        if (factor >= 1.0)
            return factor.ToString("0.########", CultureInfo.InvariantCulture);

        var mantissa = factor;
        var exponent = 0;
        while (mantissa < 1.0)
        {
            mantissa *= 10.0;
            exponent--;
        }

        var mantissaText = mantissa.ToString("0.########", CultureInfo.InvariantCulture);
        return $"{mantissaText}e{exponent}";
    }
}

public readonly record struct StratumColor(double R, double G, double B);

public readonly record struct StratumBand(string Label, StratumColor Color, double OuterRadius, double InnerRadius);