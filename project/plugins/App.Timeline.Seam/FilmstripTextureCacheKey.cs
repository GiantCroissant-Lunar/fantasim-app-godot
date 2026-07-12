namespace FantaSim.App.Timeline.Seam;

/// <summary>
/// Plugin-local, Godot-free texture identity. RequestedTick identifies the exact rendered frame;
/// SnapshotTick identifies its governing stored world state. GraphRevision is provider-proven by
/// App.World's before/render/after gate. The cheap products revision is resolved once per lane or
/// corridor rebuild and threaded into every request, never reconstructed by the controller.
///
/// RESIDUE -- Seed is NOT included, and cannot be without a T1 contract change:
/// <c>WorldGenerationRenderOptions.Seed</c> is resolved only inside the App.World PLUGIN assembly
/// (project/plugins/App.World/GenerationGraph/WorldGenerationRenderOptions.cs), a
/// collectible-bundle-private type this project does not and must not reference (App.Timeline.Seam
/// depends only on contracts/App.World, never the App.World plugin project -- crossing that
/// boundary would dual-copy the shared Unify assembly closure across two collectible ALCs, the
/// type-identity-split incident class documented in vault/architecture/cross-alc-rules.md and
/// called out by the polarity-flip commit 9bda14f). Neither of the T1 contracts this bundle CAN
/// see -- LayerFilmstripPreviewMap / LayerFilmstripPreviewRequest
/// (project/contracts/App.World/LayerFilmstripPreview.cs) carries Seed. Threading Seed through
/// requires another T1 contract change and is explicitly deferred by the approved design.
/// </summary>
internal readonly record struct FilmstripTextureCacheKey(
    string SphereId,
    string LayerId,
    long RequestedTick,
    long SnapshotTick,
    string ViewRung,
    int Width,
    int Height,
    int GraphRevision);
