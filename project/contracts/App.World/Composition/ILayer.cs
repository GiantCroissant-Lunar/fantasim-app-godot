using System.Collections.Generic;
using FantaSim.App.World;       // WorldLayerDescriptor

namespace FantaSim.App.World.Composition;

/// <summary>
/// App-side sphere-category id. (The world's SphereId is intentionally kept OUT of this thin,
/// resident-shareable contract -- mirror how LayerId/FieldId are app-side string wrappers.)
/// </summary>
public readonly record struct SphereId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>
/// The composition unit. Every layer has identity + its field contract (the "parameter face").
/// </summary>
public interface ILayer
{
    LayerId Id { get; }
    SphereId Sphere { get; }
    LayerFieldBinding Fields { get; }
}

/// <summary>
/// OPT-IN capability: contributes node-graph generation products.
/// Placeholder marker for now -- its members land in a later step; presence is cast-probed.
/// </summary>
public interface IGeneratorLayer : ILayer { }

/// <summary>
/// OPT-IN capability: contributes renderable presentation layers.
/// </summary>
public interface IRenderLayer : ILayer
{
    IReadOnlyList<WorldLayerDescriptor> RenderLayers { get; }
}

// NOTE: ITimelineLayer omitted — App.Timeline / ITimelineSource not yet present in this app.
// Port it when the timeline contract is wired.
