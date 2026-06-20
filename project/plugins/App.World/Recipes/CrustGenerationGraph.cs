using System.Text.Json.Nodes;
using FantaSim.App.NodeGraph;

namespace FantaSim.App.World.Recipes;

/// <summary>
/// The crust-evolution pipeline as a data-driven graph: a single <c>crust.generate</c> sink that
/// runs PlateTopologyBuilder -> CrustPipeline and returns the run summary. Mirrors
/// <see cref="T:FantaSim.App.Iii.Recipes.TextTo3dGraph"/>: a recipe is a named
/// <see cref="GraphDocument"/> builder shipped with the providing axis (here App.World). The graph is
/// executed by the general <see cref="GraphExecutor"/> through <see cref="WorldFunctionProvider"/>.
///
/// <para>v1 is a single node — the crust pipeline is itself an end-to-end run, so there is nothing to
/// wire upstream yet. Later phases (e.g. a viz / export node) can append nodes and data wires.</para>
/// </summary>
public static class CrustGenerationGraph
{
    /// <summary>
    /// Build the default crust-generation graph. Parameters are optional overrides; when omitted the
    /// provider applies the proven 3-plate defaults (frequency 3, 3 equatorial plates, plate 0 spinning,
    /// plates 0 and 1 continental — an active convergent 0|1 boundary that drives orogeny).
    /// </summary>
    /// <param name="frequency">Geodesic tessellation frequency (cells = 20*frequency^2 triangular faces).</param>
    /// <param name="ticks">Number of evolution ticks (endTick); accumulated orogeny grows with this.</param>
    public static GraphDocument Build(int? frequency = null, long? ticks = null)
    {
        var generateParams = new JsonObject();
        if (frequency is not null) generateParams["frequency"] = frequency.Value;
        if (ticks is not null) generateParams["ticks"] = ticks.Value;

        return new GraphDocument(
            Nodes: new[]
            {
                new GraphNode("crust", "crust.generate", generateParams),
            },
            Wires: System.Array.Empty<GraphWire>(),
            SinkNodeId: "crust");
    }
}
