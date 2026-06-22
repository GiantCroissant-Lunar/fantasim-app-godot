using System;

using FantaSim.App.World;   // WorldGlobeGeometry

namespace FantaSim.App.World.Composition;

/// <summary>
/// The geosphere's STAGNANT-LID regime layer (sphere-regimes step 4): the cooled but immobile
/// single-lid crust between the magma ocean and mobile plate tectonics. A stylized model (NOT
/// geophysics) producing two per-cell fields:
/// <list type="bullet">
/// <item><c>crust-thickness-m</c> -- thickens from a thin proto-crust toward the mobile-plate
/// baseline. Authored to be C0-CONTINUOUS with the mobile-plate crust: at the plate-onset tick the
/// lerp reaches the SAME value <see cref="SyntheticCrustLayer"/> produces (same boundary-distance
/// helper, same constants), so scrubbing across the lid-&gt;plate boundary does not pop.</item>
/// <item><c>heat-flow-mw-m2</c> -- surface heat flux: high just after solidification, declining as
/// the lid cools, with extra flux retained near (future) boundaries.</item>
/// </list>
/// Reads only the reconstructed geometry (shared-seed continuity, like the magma-ocean layer); the
/// absolute tick window keeps it schedule-agnostic (the regime decides WHEN it is active).
/// </summary>
public sealed class GeosphereStagnantLidLayer : IFieldProducer
{
    private const double ThinProtoCrustM = 5_000.0;     // thin crust just after solidification
    private const double HighHeatFlowMwM2 = 250.0;      // hot, just-solidified lid
    private const double LowHeatFlowMwM2  = 60.0;       // cooled lid approaching mobile-plate onset
    private const double HeatBoundaryBoost = 0.6;       // fractional extra flux near a boundary
    private const double HeatBoundaryScaleM = 2_000_000.0;

    // The lerp window = the lid regime window, so progress reaches exactly 1 at the plate onset.
    // EndTick tracks the (possibly forcing-shifted) plate onset so crust stays C0-continuous at the
    // lid->plate boundary wherever the atmosphere forcing places it.
    private readonly long _startTick = SphereRegimeScheduleDefaults.MagmaOceanEndTick;
    private readonly long _endTick;

    /// <summary>Creates the lid layer whose crust lerp closes at <paramref name="plateOnsetTick"/>
    /// (null = the default-forcing onset, <see cref="SphereRegimeScheduleDefaults.PlateOnsetTick"/>).</summary>
    public GeosphereStagnantLidLayer(long? plateOnsetTick = null)
        => _endTick = plateOnsetTick ?? SphereRegimeScheduleDefaults.PlateOnsetTick;

    public LayerId  Id     { get; } = new("geosphere.stagnant-lid");
    public SphereId Sphere { get; } = new("geosphere");
    public LayerFieldBinding Fields { get; } = new(
        new LayerId("geosphere.stagnant-lid"),
        Produces: new[] { GeosphereFieldCatalog.CrustThickness, GeosphereFieldCatalog.HeatFlow },
        Consumes: Array.Empty<FieldConsumption>());

    public void Produce(IFieldComputeContext context)
    {
        // The SAME per-cell distance the mobile-plate crust consumes -> exact C0 at the boundary.
        var distances = GeospherePlateLayer.ComputeBoundaryDistances(context.Geometry);
        int n = context.CellCount;

        // 0 at lid start (thin proto-crust, hot) -> 1 at plate onset (mobile-plate crust, cooled).
        double progress = Math.Clamp(
            (double)(context.Tick - _startTick) / (_endTick - _startTick), 0.0, 1.0);

        var crust = new double[n];
        var heat = new double[n];
        for (int i = 0; i < n; i++)
        {
            // Target = the SAME formula SyntheticCrustLayer uses; the lerp reaches it exactly at onset.
            double mobilePlateCrust =
                SyntheticCrustLayer.BaseCrustThicknessM + SyntheticCrustLayer.CrustDistanceGain * distances[i];
            crust[i] = ThinProtoCrustM + progress * (mobilePlateCrust - ThinProtoCrustM);

            double baseFlux = LowHeatFlowMwM2 + (HighHeatFlowMwM2 - LowHeatFlowMwM2) * (1.0 - progress);
            double boundaryFactor = 1.0 + HeatBoundaryBoost * Math.Exp(-distances[i] / HeatBoundaryScaleM);
            heat[i] = baseFlux * boundaryFactor;
        }

        context.SetScalar(GeosphereFieldCatalog.CrustThickness, crust);
        context.SetScalar(GeosphereFieldCatalog.HeatFlow, heat);
    }
}
