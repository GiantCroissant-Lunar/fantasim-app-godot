# Track-Embedded Layer Graphs Design

**Status:** D7c first-slice design lock, 2026-07-08.

## Layout Contract

- Each geosphere layer track owns its content area. The content is that layer's node graph, not a separate graph panel and not a dropdown below the timeline.
- Compact rows keep the current `TrackHeight` contract: 26px. In this state the track content is a miniature read-only strip: node chips in pipeline order with small edge hints between them.
- Track headers gain a separate chevron expand/collapse control. Expanding a track grows only that row to an expanded height of about 200px.
- Expanded rows host a pannable read-only `GraphEdit` for that layer's graph. The lane container must relayout from the sum of row heights so the playhead line, handle, band rows, and scrub surfaces keep matching the visible timeline bounds.

## Interaction Contract

- D5 active toggles stay on the track header. Clicking the layer label keeps the existing toggle behavior and multi-highlight semantics.
- Expand/collapse is a separate chevron; it must not toggle the layer.
- Compact tracks remain scrub-friendly. Ruler clicks, face-root scrub, playhead-line drag, band seeks, play/pause, fit, +/- zoom, and wheel time-zoom stay routed through the existing timeline input paths.
- Expanded graphs are read-only and pannable. They do not create, delete, connect, or edit nodes in this first slice.

## Data Source

- Track graphs come from the layer generation subgraph in the world-generation graph family.
- The P4b per-regime layer-generation bindings are the anchor: resolve the track's `sphereId + layerId + current regimeId` to its layer graph, then filter/project that graph for the track.
- If a specific layer graph cannot be resolved at the current tick, the track shows an empty unavailable strip rather than inventing demo data.
- D5 composition rules become graph compose nodes in a later slice. The AnimationTree reference semantics remain the target: layer inputs feed compose nodes with filters and one output.

## Deferred

- Editing layer graphs from the timeline.
- Presentation-composition nodes and D5 stack wiring in the graph.
- Atmosphere track graph lanes.
- Replacing the full `world-generation-node-graph` panel; this slice only embeds per-track read-only graphs.
