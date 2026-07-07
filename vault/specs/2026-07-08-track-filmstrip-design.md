# Track Filmstrip Design Addendum

Status: D8c first-slice design, 2026-07-08.

## Compact vs expanded content

Each timeline track keeps the D7c split:

- Compact row content is a low-resolution image filmstrip of that layer over the visible time range.
- Expanded row content stays the embedded layer node graph. The graph is not replaced or moved.

Compact rows use a 40 px row height so a 96x48 equirectangular thumbnail can remain readable when
letterboxed into the track strip. Headers, toggles, scrub, zoom, and the playhead continue to share
the same row layout and input routes.

## Frame slots

The compact content strip is divided into one thumbnail slot per 96 px of visible content width.
Each slot samples the tick at the center of its pixel interval:

`slotCenterFraction = (slotX + slotWidth / 2) / contentWidth`

`slotTick = viewStart + slotCenterFraction * (viewEnd - viewStart)`

The final slot may be narrower when the content width is not a multiple of 96 px. A content strip
that is narrower than one frame still gets one slot.

## Cache key

Godot-side texture cache keys are:

`sphereId + layerId + snapshotTick + viewRung + width + height`

`snapshotTick` is the world source tick that governed the preview image. For crust this is the
5M-tick materialization snapshot selected for the frame tick. For continuously sampled or placeholder
layers it is the requested frame tick. The cache includes view rung so zooming between time scales can
replace thumbnails even when the same snapshot tick is reused.

## Async generation

Compact slots first render a neutral placeholder. The seam starts preview generation on a background
`Task`, then marshals only texture creation back to the Godot main thread with `CallDeferred`.
Stale completions are ignored by a generation token that increments when lanes rebuild or the node
exits. Cached `ImageTexture` objects are disposed on `_ExitTree`.

Zoom and resize rebuild compact filmstrips from the current visible range. Rebuilds reuse cached
textures and only request missing keys, so repeated wheel zooms do not fan out full generations.

## Low-resolution source doctrine

Track previews are not screenshots and do not use offscreen viewports in this slice. The source is a
small CPU equirectangular map, currently 96x48 RGBA, generated from low-frequency world data:

- Crust: frequency 2/3 crust materialization, sampled at the frame tick from the governing
  5M-tick snapshot, colored from continental fraction with elevation shading.
- Plate: low-frequency globe snapshot at the frame tick, colored by plate identity.
- Magma-ocean and stagnant-lid lanes: regime-colored procedural fills.
- Mantle: fixed gradient placeholder for this slice.
- Atmosphere lanes: blue procedural placeholder for this slice.

The preview path must never call the full-frequency presentation document just to paint thumbnails.
