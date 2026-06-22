using System;
using System.Collections.Generic;
using FantaSim.App.World;   // WorldLayerDescriptor, GeoPoint, BoundaryGeoSegment

namespace FantaSim.App.World.Composition;

/// <summary>
/// The resident essential-core geosphere plate layer -- the first concrete <see cref="ILayer"/>.
/// It contributes the geosphere presentation descriptors (the RENDER face; the seven
/// descriptors were moved here verbatim from <c>WorldLayerRegistry</c> -- render-parity refactor,
/// layer-stack step 2b) and the per-cell <c>plate-boundary-distance-m</c> field (the PARAMETER
/// face; step 3a). The value computation is still deferred to the field-value runtime.
/// <para>
/// Deliberately NOT yet:
/// </para>
/// <list type="bullet">
/// <item><c>ITimelineLayer</c>: the world timeline source is still whole-world
/// (<c>SourceId = "world"</c>), not layer-scoped -- exposing it here would blur the abstraction.
/// Deferred until the world tracks are actually split per layer.</item>
/// <item><c>IGeneratorLayer</c>: the generator face (which carries the app-side truth-domain
/// binding, never the T3 <c>TruthStreamIdentity</c> -- see world-layer-stack.md S4.6).</item>
/// </list>
/// </summary>
public sealed class GeospherePlateLayer : IRenderLayer, IFieldProducer
{
    public LayerId Id { get; } = new("geosphere.plate");

    public SphereId Sphere { get; } = new("geosphere");

    public LayerFieldBinding Fields { get; }

    public IReadOnlyList<WorldLayerDescriptor> RenderLayers { get; } = new List<WorldLayerDescriptor>
    {
        new WorldLayerDescriptor("geosphere.cells.fill", "Cells fill", "fill", 0),
        new WorldLayerDescriptor("geosphere.cells.boundary", "Cell edges", "line", 10),
        new WorldLayerDescriptor("geosphere.plates.boundary", "Plate boundaries", "line", 20),
        new WorldLayerDescriptor("geosphere.junctions", "Junctions", "point", 30),
        new WorldLayerDescriptor("geosphere.phenomena.subduction", "Subduction", "line", 40),
        new WorldLayerDescriptor("geosphere.phenomena.rift", "Rift", "line", 41),
        new WorldLayerDescriptor("geosphere.phenomena.transform", "Transform", "line", 42),
    }.AsReadOnly();

    public GeospherePlateLayer()
    {
        // Parameter face: produces plate-boundary-distance-m per cell (value computation
        // deferred to the field-value runtime).
        Fields = new LayerFieldBinding(
            Id,
            Produces: new[] { GeosphereFieldCatalog.PlateBoundaryDistance },
            Consumes: System.Array.Empty<FieldConsumption>());
    }

    /// <summary>
    /// VALUE-compute (layer-stack step 4a-ii): for each cell (= plate, v1 per-plate granularity), the
    /// mean great-circle distance from the plate centroid to the midpoints of the boundary segments
    /// that border it. This is APPROXIMATELY static across the timeline -- under rigid rotation a
    /// plate and its boundaries move together -- so it is deliberately NOT the source of time
    /// variation (that is the crust layer's convergent-fraction term). Cells with no bordering
    /// segments fall back to the plate's own mean radius (centroid -> ring vertices) so the field is
    /// always defined (every owned output must be written -- resolver invariant 2).
    /// </summary>
    public void Produce(IFieldComputeContext context)
    {
        context.SetScalar(
            GeosphereFieldCatalog.PlateBoundaryDistance,
            ComputeBoundaryDistances(context.Geometry));
    }

    /// <summary>
    /// Per-cell mean great-circle distance (m) from the plate centroid to its bordering boundary
    /// segments' midpoints, falling back to the plate's own mean radius when it has no neighbours.
    /// Extracted (sphere-regimes step 4) so other geosphere regimes (e.g. stagnant-lid) can derive
    /// crust thickness from the SAME distance the mobile-plate crust consumes -- the basis of the
    /// cross-regime C0 continuity rule.
    /// </summary>
    public static double[] ComputeBoundaryDistances(WorldGlobeGeometry geometry)
    {
        var cells = geometry.Cells;
        var segments = geometry.BoundarySegments;
        var values = new double[cells.Count];

        for (int i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            var centroid = GeosphereFieldMath.Centroid(cell.OuterRing);

            double sum = 0;
            int count = 0;
            foreach (var seg in segments)
            {
                if (!BordersPlate(seg, cell.PlateId))
                    continue;
                var mid = GeosphereFieldMath.Midpoint(seg.Start, seg.End);
                sum += GeosphereFieldMath.GreatCircleMeters(centroid, mid);
                count++;
            }

            if (count > 0)
            {
                values[i] = sum / count;
            }
            else
            {
                // No bordering boundary segments: use the plate's own mean radius so the field is
                // never undefined (a real plate always has neighbours; this is defensive for sparse
                // synthetic geometries and degenerate single-plate worlds).
                double rsum = 0;
                foreach (var v in cell.OuterRing)
                    rsum += GeosphereFieldMath.GreatCircleMeters(centroid, v);
                values[i] = cell.OuterRing.Count > 0 ? rsum / cell.OuterRing.Count : 0.0;
            }
        }

        return values;
    }

    private static bool BordersPlate(BoundaryGeoSegment seg, string plateId)
        => string.Equals(seg.PlateAId, plateId, StringComparison.Ordinal)
        || string.Equals(seg.PlateBId, plateId, StringComparison.Ordinal);
}
