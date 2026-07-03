using System.Globalization;
using FantaSim.App.World.Composition;

namespace FantaSim.App.World.Globe;

/// <summary>
/// Godot-free helper for the on-screen vertical-scale indicator (scale rule S2). Mirrors the
/// <see cref="CanonicalTimeLabel"/> pattern: a pure formatter the T4 seam consumes as a plain string.
///
/// <para>The factor shown is the raw <c>verticalExaggeration</c> world parameter (the multiplier
/// mapping crust elevation metres to unit-globe displacement), humanized for display. An honest
/// "xN" ratio of rendered relief to true-scale relief would need the world's radius in metres
/// (rendered/true = exaggeration x worldRadiusMetres), but the presentation assumes a unit globe
/// with no declared real-world radius — the S3 spatial ladder anchor (world radius parameter) is
/// roadmap, not yet built. So the indicator reports the raw factor in unit-globe terms
/// ("relief x1e-5 units") rather than inventing a fake xN. When the spatial ladder lands, this
/// helper gains the radius-parameter argument and switches to an honest xN.</para>
/// </summary>
public static class VerticalScaleLabel
{
    private const string IndicatorSeparator = "  |  ";

    /// <summary>
    /// Formats the raw exaggeration factor for the scale indicator. Small factors use scientific
    /// notation (e.g. <c>1e-5</c>); factors >= 0.001 render in plain decimal so the common case
    /// reads naturally (<c>300</c>, <c>1.2e-4</c>).
    /// </summary>
    public static string FormatRawFactor(double exaggeration)
    {
        if (exaggeration <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(exaggeration), exaggeration, "Vertical exaggeration must be positive.");

        return $"relief x{HumanizeFactor(exaggeration)} units";
    }

    /// <summary>
    /// Whether the given view mode should show the vertical-scale indicator. The world view and the
    /// hypsometric terrain view both displace caps by the exaggeration, so both show it; plate-
    /// identity (flat) and inactive regimes do not.
    /// </summary>
    public static bool ShouldShowIndicator(GlobeViewMode viewMode)
        => viewMode is GlobeViewMode.World or GlobeViewMode.HypsometricTerrain;

    /// <summary>
    /// Builds the suffix appended to the regime/tick status label text, e.g.
    /// <c>"  |  vertical x1e-5 units"</c>. Returns an empty string when the indicator should not
    /// show (caller guards on view mode; this method always appends for a valid factor).
    /// </summary>
    public static string BuildIndicatorSuffix(double exaggeration)
        => $"{IndicatorSeparator}vertical x{HumanizeFactor(exaggeration)} units";

    /// <summary>
    /// Profile-aware indicator (S2 honesty for the non-linear lens): when the displacement is
    /// <c>sign(h)*|h|^p * scale</c> with <paramref name="heightExponent"/> p != 1, the label names
    /// the profile — <c>"  |  vertical h^0.5 x5e-4 units"</c> — instead of pretending the factor is
    /// linear. Exponent 1 falls back to the plain linear form.
    /// </summary>
    public static string BuildIndicatorSuffix(double exaggeration, double heightExponent)
        => heightExponent == 1.0
            ? BuildIndicatorSuffix(exaggeration)
            : $"{IndicatorSeparator}vertical h^{heightExponent.ToString("0.###", CultureInfo.InvariantCulture)} x{HumanizeFactor(exaggeration)} units";

    private static string HumanizeFactor(double factor)
    {
        // Factors below 1 are most readable in scientific notation (the default 1e-5 case, and
        // small fractions like 0.001 -> 1e-3). >= 1 renders in plain decimal: 1, 300, 1234.5.
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