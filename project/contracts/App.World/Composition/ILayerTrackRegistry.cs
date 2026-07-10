using System;

namespace FantaSim.App.World.Composition;

/// <summary>
/// The layer-&gt;track registry (slice 1): merges the generation family json's declared layers
/// with the declared-layers asset into one track list every view reads instead of hardcoding
/// lane/track names. See vault/specs/2026-07-10-layer-track-registry-design.md.
/// </summary>
/// <remarks>
/// Implemented by <c>FantaSim.App.World.Composition.LayerTrackRegistryService</c> (T3,
/// plugins/App.World.Composition). Consumers on the resident side (e.g. the timeline face)
/// receive this reference through the resident-context proxy pattern -- never a static -- and
/// must unsubscribe <see cref="Changed"/> when the context is cleared or the node exits the
/// tree (ALC discipline: an unreleased subscription pins the owning bundle's ALC).
/// </remarks>
public interface ILayerTrackRegistry
{
    /// <summary>The current merged snapshot. Never null; empty when nothing is declared yet.</summary>
    LayerTrackRegistrySnapshot Current { get; }

    /// <summary>Raised after <see cref="Current"/> changes (archive toggle or <see cref="Reload"/>).</summary>
    event Action<LayerTrackRegistrySnapshot>? Changed;

    /// <summary>
    /// Archives or restores one track. The underlying declared/discovered data is never deleted --
    /// archiving only flips <see cref="LayerTrackDescriptor.State"/> to
    /// <see cref="LayerTrackStates.Archived"/> (or back) for the given track and raises
    /// <see cref="Changed"/> exactly once.
    /// </summary>
    void SetArchived(string sphereId, string layerId, bool archived);

    /// <summary>
    /// Re-reads the pipeline json and its declared-layers/family-document inputs and rebuilds
    /// <see cref="Current"/>, preserving the archive overlay, then raises <see cref="Changed"/>.
    /// </summary>
    void Reload();
}
