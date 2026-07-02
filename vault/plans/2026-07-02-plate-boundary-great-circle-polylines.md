# Plate Boundaries as Typed Great-Circle Polylines

**Date:** 2026-07-02 · **Branch:** `agent/boundary-polylines` · **Status:** implemented (reference-tick arcs); per-tick evolution is the next slice.

## What shipped

At the mobile-plate regime, plate boundaries now render as **smooth great-circle polylines**
colored by boundary type, driven by plate-topology truth instead of the cell rasterizer:

- The topology engine's `Boundary` records (`PlateTopologyBuilder.ClassifyBoundariesAt`) carry
  ordered `SamplePoints` along each inter-plate arc plus a `Type`. Those points are now subdivided
  Godot-free into dense great-circle polylines and lifted into thin ribbon geometry by the host.
- Boundary **type** (convergent / divergent / transform) comes from the topology truth — not from a
  per-edge velocity heuristic as the old `PlateBoundaryFocusRenderer` did. Inactive arcs are omitted.

**Color key:** convergent = warm red-orange, divergent = cyan-teal, transform = pale yellow.

## Data path (new)

`GlobeReconstructor.BuildBoundaryArcsAt(tick)` (Godot-free, regime-gated) →
`PlanetPresentationDocument.BoundaryArcs` (new T1 contract `PlateBoundaryArc`) →
`PlateBoundaryFocusRenderer(IReadOnlyList<PlateBoundaryArc>)` (host).

The Godot-free `BoundaryArcSampler` owns great-circle subdivision (Unify `Quaternion.FromAxisAngle` +
`Rotate`), the topology→contract type mapping, and boundary-set diffing over ticks. The host renderer
only lifts pre-subdivided points into a quad-strip ribbon slightly above the surface radius.

## Identified gap — per-tick type evolution across the playhead

The document is rebuilt on generation change (`Rebind`), **not** on every timeline tick. So boundary
arcs are authoritative for the **reference (onset) tick**. Boundaries still **appear/disappear on regime
change** (the existing regime-gated visibility in `PlanetPresentationBinder.ApplyTimelineTick` covers
that), but a boundary **changing type** as plates drift (visible mid-playhead) is not yet live.

Closing that needs: a retained `GlobeReconstructor` behind a tick-parametric service query
(`IService.GetPlanetBoundaryArcsAt(tick)` or similar) so the binder can request reclassification on
seek. That is a separate slice — recorded here so the renderer contract (`PlateBoundaryArc`) is already
in place when it lands.

## What was NOT touched

- Cell-cap coloring / surface mesh (`GlobePlateSurfaces`, `BuildPlateSurface`) — unchanged.
- `WorldGlobeGeometry.BoundarySegments` (geodetic, lat/lon) — left as-is; it is a separate (still
  empty-sourced) concern used by the composition field-value layers, not this overlay renderer.

## Verification

- `task build` — 0 errors.
- `task test` — full xUnit green; 16 new tests cover arc sampling (midpoint, unit-length, equal/
  antipodal guards, subdiv clamping), type mapping, set diffing, and `BuildBoundaryArcsAt` (vocabulary,
  regime gating, determinism, subdiv scaling).
- Visual QA in the exported windowed app (`task run:exported`) is a follow-up: tune
  `RibbonHalfWidth` / `RibbonHeight` / `subdivsPerSegment` if lines read too thin/thick.
