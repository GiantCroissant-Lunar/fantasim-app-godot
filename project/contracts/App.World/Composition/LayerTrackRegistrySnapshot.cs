using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FantaSim.App.World.Composition;

/// <summary>
/// The merged layer-track registry at a point in time: every declared/discovered track, archived
/// or not (views filter archived tracks out; the archive flag itself is never lost -- see
/// <see cref="ILayerTrackRegistry.SetArchived"/>). <see cref="Revision"/> increments on every
/// mutation so consumers can detect a stale cached snapshot.
/// </summary>
public sealed record LayerTrackRegistrySnapshot(
    [property: JsonPropertyName("revision")] int Revision,
    [property: JsonPropertyName("tracks")] IReadOnlyList<LayerTrackDescriptor> Tracks);
