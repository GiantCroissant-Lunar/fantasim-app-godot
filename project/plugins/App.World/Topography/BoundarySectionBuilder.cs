using System;
using System.Collections.Generic;
using System.Linq;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Dto;
using FantaSim.Geosphere.Crust;
using UnifyMaths;

namespace FantaSim.App.World.Topography;

/// <summary>
/// Builds boundary-normal section documents from the existing boundary arc + boundary profile model.
/// This is intentionally Godot-free: renderers consume <see cref="BoundarySectionDocument"/> only.
/// </summary>
public static class BoundarySectionBuilder
{
    public const int DefaultSampleCount = 65;
    public const double DefaultPlanetRadiusMetres = 6_371_000.0;
    public const double DefaultExaggeration = 1.0;

    public static BoundarySectionDocument? BuildForArc(
        WorldGlobeSnapshot globe,
        PlateBoundaryArc arc,
        IReadOnlyDictionary<int, CellCrustState> state,
        IReadOnlyDictionary<int, CrustFeature>? features,
        BoundaryProfileParameters parameters,
        int sampleCount = DefaultSampleCount,
        double planetRadiusMetres = DefaultPlanetRadiusMetres,
        double exaggeration = DefaultExaggeration)
    {
        ArgumentNullException.ThrowIfNull(globe);
        ArgumentNullException.ThrowIfNull(arc);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(parameters);

        if (arc.Kind == PlateBoundaryKind.Inactive || arc.Points.Count < 2)
            return null;
        if (sampleCount < 3)
            throw new ArgumentOutOfRangeException(nameof(sampleCount), sampleCount, "Boundary sections need at least three samples.");

        var arcs = new[] { arc };
        var polarity = ConvergentPolarity.Derive(arcs, globe.Cells, features, state);
        var key = arc.PlateA <= arc.PlateB ? (arc.PlateA, arc.PlateB) : (arc.PlateB, arc.PlateA);
        polarity.TryGetValue(key, out var convergentPolarity);

        var field = CellBoundaryField.Build(globe.Cells, arcs, polarity);
        int nearestPointIndex = SelectRepresentativePointIndex(field, arc);
        double transformPhaseCoordinate = CellBoundaryField.TransformPhaseCoordinate(
            arc,
            ToVector(SelectOrigin(arc)));
        double halfWidth = ResolveHalfWidth(arc.Kind, convergentPolarity.IsCollision, parameters);

        var samples = new BoundarySectionSample[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            double t = sampleCount == 1 ? 0.0 : (double)i / (sampleCount - 1);
            double signedDistance = -halfWidth + (2.0 * halfWidth * t);
            int cellPlateId = SelectSamplePlateId(arc, signedDistance, convergentPolarity);
            var profileSample = new CellBoundarySample(
                Found: true,
                signedDistance,
                arc.Kind,
                nearestPointIndex,
                transformPhaseCoordinate,
                cellPlateId,
                arc.PlateA,
                arc.PlateB,
                convergentPolarity.SubductingPlateId == default && arc.Kind != PlateBoundaryKind.Convergent
                    ? null
                    : convergentPolarity.SubductingPlateId,
                convergentPolarity.IsCollision);
            double elevation = BoundaryProfileShape.Contribution(profileSample, parameters);
            samples[i] = new BoundarySectionSample(
                signedDistance,
                elevation,
                CutawayStratumProfile.DefaultCrustThicknessMetres,
                arc.Kind);
        }

        var bands = CutawayStratumProfile.ComputeBands(
            CutawayStratumProfile.DefaultCrustThicknessMetres,
            CutawayStratumProfile.DefaultLithosphereLidThicknessMetres,
            exaggeration,
            planetRadiusMetres)
            .Select(ToSectionBand)
            .ToArray();

        return new BoundarySectionDocument(
            PlateA: arc.PlateA,
            PlateB: arc.PlateB,
            Kind: arc.Kind,
            Origin: SelectOrigin(arc),
            NormalAxis: SelectNormalAxis(arc),
            Samples: samples,
            InteriorBands: bands,
            Exaggeration: exaggeration,
            PlanetRadiusMetres: planetRadiusMetres,
            LabelOverride: BuildLabel(arc.Kind, convergentPolarity),
            SubductingPlateId: arc.Kind == PlateBoundaryKind.Convergent && !convergentPolarity.IsCollision
                ? convergentPolarity.SubductingPlateId
                : null,
            IsCollision: arc.Kind == PlateBoundaryKind.Convergent && convergentPolarity.IsCollision);
    }

    public static IReadOnlyList<BoundarySectionDocument> BuildRepresentativeSections(
        WorldGlobeSnapshot globe,
        IReadOnlyList<PlateBoundaryArc> arcs,
        IReadOnlyDictionary<int, CellCrustState> state,
        IReadOnlyDictionary<int, CrustFeature>? features,
        BoundaryProfileParameters parameters,
        int sampleCount = DefaultSampleCount,
        double planetRadiusMetres = DefaultPlanetRadiusMetres,
        double exaggeration = DefaultExaggeration)
    {
        ArgumentNullException.ThrowIfNull(arcs);

        var result = new List<BoundarySectionDocument>();
        foreach (var kind in new[] { PlateBoundaryKind.Convergent, PlateBoundaryKind.Divergent, PlateBoundaryKind.Transform })
        {
            var arc = arcs.FirstOrDefault(candidate => candidate.Kind == kind && candidate.Points.Count >= 2);
            if (arc is null)
                continue;

            var section = BuildForArc(
                globe,
                arc,
                state,
                features,
                parameters,
                sampleCount,
                planetRadiusMetres,
                exaggeration);
            if (section is not null)
                result.Add(section);
        }

        return result;
    }

    private static int SelectRepresentativePointIndex(IReadOnlyList<CellBoundarySample> field, PlateBoundaryArc arc)
    {
        var indices = field
            .Where(sample => sample.Found
                && sample.Kind == arc.Kind
                && sample.ArcPlateA == arc.PlateA
                && sample.ArcPlateB == arc.PlateB)
            .Select(sample => sample.NearestPointIndex)
            .Order()
            .ToArray();
        if (indices.Length == 0)
            return arc.Points.Count / 2;
        return indices[indices.Length / 2];
    }

    private static int SelectSamplePlateId(
        PlateBoundaryArc arc,
        double signedDistance,
        ConvergentBoundaryPolarity polarity)
    {
        if (arc.Kind != PlateBoundaryKind.Convergent || polarity.IsCollision)
            return arc.PlateA;

        if (signedDistance <= 0.0)
            return polarity.SubductingPlateId;
        return polarity.OverridingPlateId;
    }

    private static double ResolveHalfWidth(
        PlateBoundaryKind kind,
        bool isCollision,
        BoundaryProfileParameters parameters)
        => kind switch
        {
            PlateBoundaryKind.Convergent when isCollision => parameters.ConvergentCollisionHalfWidthRad,
            PlateBoundaryKind.Convergent => Math.Max(
                parameters.ConvergentTrenchHalfWidthRad,
                parameters.ConvergentArcSetbackRad + parameters.ConvergentArcHalfWidthRad),
            PlateBoundaryKind.Divergent => parameters.DivergentSwellHalfWidthRad,
            PlateBoundaryKind.Transform => parameters.TransformHalfWidthRad,
            _ => 0.01,
        };

    private static GlobeVec3 SelectOrigin(PlateBoundaryArc arc)
        => arc.Points[arc.Points.Count / 2];

    private static GlobeVec3 SelectNormalAxis(PlateBoundaryArc arc)
    {
        var origin = ToVector(SelectOrigin(arc));
        var start = ToVector(arc.Points[0]);
        var end = ToVector(arc.Points[^1]);
        var tangent = Normalize(end - start);
        var normal = Normalize(Cross(tangent, origin));
        return ToGlobe(normal.Length() > 1e-12 ? normal : new Vector3D(0, 0, 1));
    }

    private static string BuildLabel(PlateBoundaryKind kind, ConvergentBoundaryPolarity polarity)
        => kind switch
        {
            PlateBoundaryKind.Convergent when polarity.IsCollision => "Convergent collision section",
            PlateBoundaryKind.Convergent => "Convergent subduction section",
            PlateBoundaryKind.Divergent => "Divergent rift section",
            PlateBoundaryKind.Transform => "Transform shear section",
            _ => "Boundary section",
        };

    private static BoundarySectionBand ToSectionBand(StratumBand band)
        => new(
            band.Label,
            new BoundarySectionColor(band.Color.R, band.Color.G, band.Color.B),
            band.OuterRadius,
            band.InnerRadius);

    private static Vector3D ToVector(GlobeVec3 value) => new(value.X, value.Y, value.Z);

    private static GlobeVec3 ToGlobe(Vector3D value) => new((float)value.X, (float)value.Y, (float)value.Z);

    private static Vector3D Normalize(Vector3D value)
    {
        double length = value.Length();
        return length > 1e-12 ? value * (1.0 / length) : new Vector3D(0, 0, 0);
    }

    private static Vector3D Cross(Vector3D a, Vector3D b)
        => new(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X);
}
