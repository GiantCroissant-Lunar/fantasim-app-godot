namespace FantaSim.App.Timeline.Seam;

/// <summary>
/// Plugin-local (NOT T1) filmstrip texture cache key. Layers <see cref="GraphRevision"/> on top
/// of the T1 <c>TimelineFilmstripCacheKey</c> shape (project/contracts/App.Timeline/
/// TimelineFilmstrip.cs: SphereId, LayerId, SnapshotTick, ViewRung, Width, Height), which is left
/// untouched deliberately -- extending a T1 contract record is out of scope for this fix
/// (2026-07-11 cache-key completion, vault/specs/2026-07-11-surrealdb-persistence-slice1-design.md
/// §1.3). Godot-free by design (no Godot types), same pattern as the sibling FilmstripCacheLedger
/// -- both files are individually linked into App.Timeline.Tests so the key can be unit-tested
/// directly without a full ProjectReference to this Godot-dependent assembly.
///
/// GraphRevision is sourced from the same <c>WorldGenerationGraphFamilyDocument.Revision</c> the
/// crust-product cache key now carries (App.World/Services/Service.cs CrustProductCacheKey),
/// reached here through <c>ITimelineFaceContext.GenerationGraphFamilyProvider</c> -- but NOT by
/// invoking that provider again from this controller. It is already resolved once per
/// <c>TimelineFace.BuildLanes()</c> pass (TimelineFace.Lanes.cs ResolveGenerationGraphFamily,
/// cached per tick) for the layer-track-graph presenter, and this fix threads that ALREADY-PAID
/// value down through the existing track-render parameter chain
/// (BuildLaneTracks -> RenderTrackContent -> TrackContentRenderContext -> RenderFilmstripTrackContent
/// -> BuildCompactFilmstrip -> FilmstripPreviewController.RequestTexture) instead of adding a new
/// call. Calling the provider fresh from inside the filmstrip hot path would be a real behavior
/// regression: it round-trips through <c>IService.GetPlanetPresentationAsync</c>, which can run a
/// full crust materialization (~0.1-0.2s, WorldGenerationRenderOptions.cs) -- unacceptable on a
/// per-texture path that fires for every visible filmstrip thumbnail, and out of scope for a fix
/// that must not change behavior beyond key composition.
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
/// (project/contracts/App.World/LayerFilmstripPreview.cs) nor WorldGenerationGraphFamilyDocument
/// itself -- carries Seed. Threading Seed through requires adding it to one of those T1 contracts,
/// which is explicitly out of scope for this fix.
/// </summary>
internal readonly record struct FilmstripTextureCacheKey(
    string SphereId,
    string LayerId,
    long SnapshotTick,
    string ViewRung,
    int Width,
    int Height,
    int GraphRevision);
