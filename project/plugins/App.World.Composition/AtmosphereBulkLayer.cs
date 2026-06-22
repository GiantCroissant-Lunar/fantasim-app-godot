using System;

using FantaSim.Atmosphere.Genesis.Core;
using FantaSim.Atmosphere.Contracts;

namespace FantaSim.App.World.Composition;

/// <summary>
/// The bulk/0-D atmosphere layer: the primordial atmosphere is GLOBAL (no spatial variation),
/// so every cell receives the same value -- a uniform tint that EVOLVES with the tick as the
/// atmosphere's greenhouse effect, surface hydration, and pressure change over geologic time.
/// The solver is <see cref="PrimordialAtmosphereSolver"/> (from FantaSim.Atmosphere.Genesis.Core),
/// which implements <see cref="IAtmosphereStateSolver"/> from the world shared contract.
/// <para>
/// Spatial variation (coupled-climate cells, latitude-dependent insolation) is a later lane;
/// this layer provides the zero-dimensional baseline that every cell shares.
/// </para>
/// </summary>
public sealed class AtmosphereBulkLayer : IFieldProducer
{
    private readonly IAtmosphereStateSolver _solver;

    /// <summary>Creates the bulk layer driven by the given atmosphere <paramref name="forcing"/>
    /// (null = the baseline curve).</summary>
    public AtmosphereBulkLayer(AtmosphereForcing? forcing = null)
        => _solver = new PrimordialAtmosphereSolver(forcing);

    public LayerId  Id     { get; } = new("atmosphere.bulk");
    public SphereId Sphere { get; } = new("atmosphere");
    public LayerFieldBinding Fields { get; } = new(
        new LayerId("atmosphere.bulk"),
        Produces: new[] { AtmosphereFieldCatalog.AtmosphereGreenhouse, AtmosphereFieldCatalog.AtmosphereHydration, AtmosphereFieldCatalog.AtmospherePressure },
        Consumes: Array.Empty<FieldConsumption>());

    public void Produce(IFieldComputeContext context)
    {
        var state = _solver.GetStateAtTick(context.Tick);
        int n = context.CellCount;

        // BULK / 0-D model: the primordial atmosphere is global, so every cell gets the same value
        // (a uniform tint that EVOLVES with the tick). Spatial variation is a later (coupled-climate) lane.
        var greenhouse = new double[n]; Array.Fill(greenhouse, state.GreenhouseDeltaCelsius);
        var hydration  = new double[n]; Array.Fill(hydration,  state.SurfaceHydrationIndex);
        var pressure   = new double[n]; Array.Fill(pressure,   state.SurfacePressureBar);

        context.SetScalar(AtmosphereFieldCatalog.AtmosphereGreenhouse, greenhouse);
        context.SetScalar(AtmosphereFieldCatalog.AtmosphereHydration,  hydration);
        context.SetScalar(AtmosphereFieldCatalog.AtmospherePressure,   pressure);
    }
}
