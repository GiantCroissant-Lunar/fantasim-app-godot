namespace FantaSim.App.World.Composition;

/// <summary>
/// Identifies a world layer selected from the timeline. The timeline owns the selection
/// intent; the world domain resolves it to a concrete layer/regime graph.
/// </summary>
/// <param name="SphereId">The sphere the layer belongs to, e.g. "geosphere" or "atmosphere".</param>
/// <param name="LayerId">The layer identifier, e.g. "geosphere.crust".</param>
public sealed record TimelineLayerSelection(string SphereId, string LayerId);
