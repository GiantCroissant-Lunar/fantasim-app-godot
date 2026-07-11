using System.Collections.Generic;
using FantaSim.App.World;
using FantaSim.App.World.Composition;
using FantaSim.App.World.Dto;

namespace FantaSim.App.Presentation;

/// <summary>
/// Godot-free content identity for the bound plate surface. RefreshPresentationForRegime compares
/// the candidate stamp of a freshly fetched document against the stamp of the last BindDocument to
/// skip the expensive mesh re-bind when a generation-completion chase (G34 "double full bind")
/// fetched content identical to what is already on screen. Timeline metadata (UpdateFrom) and the
/// node-graph view still apply on every refresh — only the surface re-bind is deduped.
///
/// Product metadata (Revision, ReferenceTick, CrustSnapshotTicks, schedules) is deliberately
/// EXCLUDED: it can flap when an async generation completes without changing the sampled surface,
/// and it only feeds diagnostic node metas / timeline lanes, never the rendered mesh. Everything
/// that DOES reach the mesh participates, with full-content hashes over the per-cell payloads, so
/// a skip is only possible for a provably identical surface — the 105M-identical-terrain delivery
/// guarantee (new content ⇒ new stamp ⇒ re-bind) holds by construction.
/// </summary>
public readonly record struct PlanetSurfaceBindStamp(
    string PlanetId,
    string SourceWorldId,
    long GlobeReferenceTick,
    int GlobeCellCount,
    string? RegimeId,
    ulong ActiveLayersHash,
    string? PlateViewOverride,
    SurfaceSubdivisionMode SurfaceSubdivision,
    int AdaptiveSubdivisionMaxDepth,
    double AdaptiveSubdivisionEdgeHeightDelta,
    double AdaptiveSubdivisionFeatureWeightDelta,
    double VerticalExaggeration,
    double CutawayExaggeration,
    ulong ElevationsHash,
    ulong ThicknessHash,
    ulong FeaturesHash,
    ulong FractionsHash,
    ulong ContinentalPlateIdsHash,
    int BoundaryArcCount,
    int BoundarySectionCount,
    int LayerCount,
    int RenderEntityCount)
{
    public static PlanetSurfaceBindStamp From(
        PlanetPresentationDocument document,
        string? regimeId,
        IReadOnlyList<TimelineLayerSelection> activeLayers,
        string? plateViewOverride)
        => new(
            PlanetId: document.PlanetId,
            SourceWorldId: document.SourceWorldId,
            GlobeReferenceTick: document.GlobeReferenceTick,
            GlobeCellCount: document.GlobeSnapshot?.CellCount ?? -1,
            RegimeId: regimeId,
            ActiveLayersHash: HashActiveLayers(activeLayers),
            PlateViewOverride: plateViewOverride,
            SurfaceSubdivision: document.SurfaceSubdivision,
            AdaptiveSubdivisionMaxDepth: document.AdaptiveSubdivisionMaxDepth,
            AdaptiveSubdivisionEdgeHeightDelta: document.AdaptiveSubdivisionEdgeHeightDelta,
            AdaptiveSubdivisionFeatureWeightDelta: document.AdaptiveSubdivisionFeatureWeightDelta,
            VerticalExaggeration: document.VerticalExaggeration,
            CutawayExaggeration: document.CutawayExaggeration,
            ElevationsHash: HashDoubles(document.CellElevations),
            ThicknessHash: HashDoubles(document.CellCrustThickness),
            FeaturesHash: HashFeatures(document.CellFeatures),
            FractionsHash: HashFractions(document.ContinentalFractionByCell),
            ContinentalPlateIdsHash: HashPlateIds(document.ContinentalPlateIds),
            BoundaryArcCount: document.BoundaryArcs?.Count ?? -1,
            BoundarySectionCount: document.BoundarySections?.Count ?? -1,
            LayerCount: document.Layers.Count,
            RenderEntityCount: document.RenderEntities.Count);

    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;
    private const ulong NullSentinel = 0x9E3779B97F4A7C15UL; // distinct from empty (FnvOffset)

    private static ulong Fold(ulong hash, ulong value)
    {
        hash ^= value;
        return hash * FnvPrime;
    }

    private static ulong HashDoubles(IReadOnlyList<double>? values)
    {
        if (values is null)
            return NullSentinel;
        ulong hash = FnvOffset;
        for (int i = 0; i < values.Count; i++)
            hash = Fold(hash, (ulong)System.BitConverter.DoubleToInt64Bits(values[i]));
        return hash;
    }

    private static ulong HashFeatures(IReadOnlyList<CellCrustFeature>? features)
    {
        if (features is null)
            return NullSentinel;
        ulong hash = FnvOffset;
        for (int i = 0; i < features.Count; i++)
        {
            hash = Fold(hash, features[i].Kind);
            hash = Fold(hash, (ulong)System.BitConverter.DoubleToInt64Bits(features[i].Magnitude));
        }
        return hash;
    }

    // Order-independent (dictionary enumeration order is unspecified): sum of per-pair FNV folds.
    private static ulong HashFractions(IReadOnlyDictionary<int, double>? fractions)
    {
        if (fractions is null)
            return NullSentinel;
        ulong hash = FnvOffset;
        foreach (var pair in fractions)
        {
            ulong pairHash = Fold(Fold(FnvOffset, (ulong)pair.Key), (ulong)System.BitConverter.DoubleToInt64Bits(pair.Value));
            unchecked { hash += pairHash; }
        }
        return hash;
    }

    private static ulong HashPlateIds(IReadOnlySet<int>? plateIds)
    {
        if (plateIds is null)
            return NullSentinel;
        ulong hash = FnvOffset;
        foreach (var id in plateIds)
            unchecked { hash += Fold(FnvOffset, (ulong)id); }
        return hash;
    }

    private static ulong HashActiveLayers(IReadOnlyList<TimelineLayerSelection> activeLayers)
    {
        ulong hash = FnvOffset;
        for (int i = 0; i < activeLayers.Count; i++)
        {
            hash = HashString(hash, activeLayers[i].SphereId);
            hash = HashString(hash, activeLayers[i].LayerId);
        }
        return hash;
    }

    private static ulong HashString(ulong hash, string? value)
    {
        if (value is null)
            return Fold(hash, NullSentinel);
        for (int i = 0; i < value.Length; i++)
            hash = Fold(hash, value[i]);
        return Fold(hash, (ulong)value.Length);
    }
}
