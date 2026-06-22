using System;

using FantaSim.Atmosphere.Genesis.Core;
using FantaSim.Atmosphere.Contracts;

namespace FantaSim.App.World.Composition;

/// <summary>
/// The coupled-climate atmosphere layer: latitude-banded surface temperature that VARIES
/// across cells (unlike <see cref="AtmosphereBulkLayer"/> which produces uniform per-cell
/// values). The equator is warm and the poles are cold, lifted by the time-varying
/// greenhouse baseline from <see cref="PrimordialAtmosphereSolver"/>. Active in the
/// coupled-climate atmosphere regime (post-plate, tick >= 100_000_000).
/// </summary>
public sealed class AtmosphereCoupledLayer : IFieldProducer
{
    private readonly IAtmosphereStateSolver _solver;

    /// <summary>Creates the coupled-climate layer driven by the given atmosphere
    /// <paramref name="forcing"/> (null = the baseline curve).</summary>
    public AtmosphereCoupledLayer(AtmosphereForcing? forcing = null)
        => _solver = new PrimordialAtmosphereSolver(forcing);

    public LayerId  Id     { get; } = new("atmosphere.coupled");
    public SphereId Sphere { get; } = new("atmosphere");
    public LayerFieldBinding Fields { get; } = new(
        new LayerId("atmosphere.coupled"),
        Produces: new[] { AtmosphereFieldCatalog.AtmosphereSurfaceTemp },
        Consumes: Array.Empty<FieldConsumption>());

    public void Produce(IFieldComputeContext context)
    {
        var cells = context.Geometry.Cells;
        int n = context.CellCount;

        var greenhouse = _solver.GetStateAtTick(context.Tick).GreenhouseDeltaCelsius;

        var temps = new double[n];
        for (int i = 0; i < n; i++)
        {
            var centroid = GeosphereFieldMath.Centroid(cells[i].OuterRing);
            double latAbs = Math.Abs(centroid.LatitudeDegrees);

            // Warm equator, cold poles, lifted by the time-varying greenhouse baseline.
            // Equator (lat 0): 15 + greenhouse; pole (lat 90): (15 + greenhouse) - 60.
            temps[i] = (15.0 + greenhouse) - (latAbs / 90.0) * 60.0;
        }

        context.SetScalar(AtmosphereFieldCatalog.AtmosphereSurfaceTemp, temps);
    }
}
