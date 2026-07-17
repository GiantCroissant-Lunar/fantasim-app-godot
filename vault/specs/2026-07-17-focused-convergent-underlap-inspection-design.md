# Focused convergent-underlap inspection — Replacement B0 design

**Status:** DRAFT FOR WRITTEN-SPEC REVIEW. The user approved the focused Slice-B direction on
2026-07-17. Implementation remains blocked until the user reviews this written specification.

## 1. Purpose and authority

This design is a narrow continuation of:

- `vault/specs/2026-07-17-spherical-plate-material-volume-design.md`;
- `vault/plans/2026-07-17-spherical-plate-material-volume-a0-b0-plan.md`; and
- `vault/specs/evidence/2026-07-17-spherical-plate-material-volume-a0-b0/README.md`.

Replacement A0 established one generated ray that intersects overriding plate 7 and then a
separate attached down-going interval belonging to plate 2 at convergent boundary arc 8.
Replacement B0 did not make that relationship visually readable. The whole-globe exploded view
shows all plates around a molten core, and the target pair remains hidden among unrelated bodies
or behind the core.

This design amends only section 14's earlier `boundary-focused explode mode` non-goal. A focused
inspection is now an approved supplementary projection used to finish Replacement B0. It does not
replace the assembled globe or the whole-globe radial exploded view, and it does not enter Slice C.

The user-provided references in the parent specification continue to bind the visual gate.

## 2. Binding user decisions

1. The normal assembled globe remains closed. Buried crust is not displayed through the surface.
2. The buried down-going volume remains causal and must be extracted from the same generated state
   when inspected.
3. A focused inspection shows two **complete plates**: the overriding plate and the plate carrying
   the attached down-going volume.
4. The core and unrelated plates may be hidden in the focused inspection because they are
   occluders, not the subject.
5. Cells and chunks remain invisible implementation partitions. They are never rendered or moved
   as inspection pieces.
6. Color and literal crust-to-core thickness scale are not acceptance concerns.
7. No new geological authority or peer plate-volume type may be added.
8. The implementation remains inside Replacement B0. Slice C and broad test expansion remain
   frozen.

## 3. Alternatives considered

### 3.1 Isolate the exact pair without separation

Hide the core and unrelated plates, retain both target plates at their generated positions, and
frame the convergent boundary.

This is the most literal projection, but it can repeat the current failure: the overriding plate
is supposed to occlude the buried portion. Isolation improves location but does not guarantee that
the descending body becomes readable.

### 3.2 Keep the global radial explosion and only improve the camera

Hide the core and unrelated plates while applying the existing whole-globe radial explosion to
both target plates.

This reuses the current transform but does not respond to the local under/over relation. Moving
both plates along unrelated centroid directions can pull the hinge apart without exposing the
descending geometry clearly.

### 3.3 Pair isolation with a small rigid reveal transform

Show only the two complete target plate solids, frame their known convergent boundary, preserve
factor zero as the exact generated relationship, and use a nonzero focused factor to lift only the
overriding whole plate. The down-going plate remains at its generated position.

This is selected. It exposes geometry that already exists without deforming either mesh,
regenerating mechanics, or creating a renderer-authored tongue.

## 4. Selected behavior

### 4.1 Existing projections remain

- **Assembled:** unchanged closed globe; buried material remains depth-occluded.
- **Whole-globe exploded:** unchanged all-plate radial explosion and core context.
- **Focused convergent inspection:** an additional presentation of the same
  `CrustVolumeState`, containing only the two plates named by its existing convergent-underlap
  proof.

### 4.2 Focus target

The presentation calls the existing `CrustVolumeState.TryFindConvergentUnderlapProof`. That proof,
not renderer inference, supplies:

- boundary arc index;
- overriding plate id;
- subducting plate id;
- subducting cell id; and
- the established ray origin and direction.

The selected boundary arc already belongs to the same state and supplies its ordered points for
framing. Presentation code may not choose arbitrary nearby plates, infer polarity from mesh
positions, or author a substitute overlap when no proof exists.

If the state has no convergent-underlap proof, focused inspection fails closed with a diagnostic.
It does not fabricate a pair and does not silently claim success through the global overview.

### 4.3 Whole-plate extraction

The focused view reuses the existing extraction chain:

```text
CrustVolumeState
    -> GlobePlateSurfaces.BuildVolumeSurfaces
    -> PlateSolidBuilder.Build
    -> the overriding and down-going complete PlateSolid bodies
```

Only emission is filtered. Both target plates retain their entire top, underside, sidewall,
thickened roots, and attached non-radial boundary deformation. Filtering may not crop a local
section or detach a smaller feature from its owning plate.

### 4.4 Exact and revealed states

- Focused factor `0` renders both target plates in their exact generated relationship.
- Focused factor greater than `0` applies one rigid outward translation to the overriding
  plate's complete top and solid mesh. The down-going plate remains at factor zero.
- The same rigid translation is applied to every vertex and surface belonging to the overriding
  plate. Triangles, normals, plate ownership, and the state digest do not change.
- Returning focused factor to `0` restores the exact generated relationship.

The first acceptance-oriented focused capture uses a small nonzero factor so the overriding plate
no longer hides the descending volume. The value is tuned by exported-window evidence rather than
treated as a scientific unit. A factor-zero companion capture proves where the generated plates
actually meet.

### 4.5 Framing

The focused crust root may receive one common view-only rotation derived from the proof ray and
the selected boundary arc's tangent. The identical rotation is applied to both complete target
plates, so their relative generated relationship is unchanged.

This common-root rotation brings the known hinge and descending direction into a stable oblique
camera view without adding a second camera-control pathway. It does not rotate, bend, or translate
one plate relative to the other. Existing `camera.orbit` remains the sole camera ingress.

The core and every unrelated plate mesh are absent from the focused root. They cannot be made
merely transparent or recolored while still blocking or confusing the inspection.

## 5. Command and ownership design

The existing `render.exploded` ingress evolves instead of adding a parallel render command.

Conceptual payloads:

```json
{"factor": 1.0}
```

continues to mean the existing whole-globe radial explosion.

```json
{"factor": 0.0, "focusConvergent": true}
```

means the exact focused pair, while:

```json
{"factor": 0.15, "focusConvergent": true}
```

means the same pair with a small rigid lift of the overriding plate. The shown nonzero factor is
an initial visual-tuning value, not an acceptance clamp.

Ownership changes are deliberately small:

| Existing owner | Evolution |
|---|---|
| `ExplodedRequest` | Add the optional `focusConvergent` presentation flag; keep the existing factor parser and range. |
| `RenderComposition` | Forward factor plus the focus flag through the existing target registration. |
| `IPlanetPresentation` | Evolve `UpdateExploded` with the focus flag; do not introduce a duplicate request DTO in the presentation contract. |
| `PlanetPresentationBinder` | Reuse the state proof, complete-plate extraction, mesh emitters, and existing rigid translation machinery; filter scene emission and frame the common root. |
| `CrustVolumeState` | No change. It remains the sole volume and underlap authority. |
| `PlateSolidBuilder` | Prefer reuse unchanged. Evolve only if its existing rigid translation cannot be safely applied to the selected complete plate. |
| `WorldCrustMaterializer` / world service | No change. Focus is a projection and may not regenerate geology. |

No `FocusedPlate`, `InspectionPlate`, `CrustVolumeState2`, alternate slab model, or second
boundary-mechanics type is authorized.

## 6. Duplicate-authority and anti-fake guard

The implementation fails this design if it:

- appends a tongue, ribbon, shelf, wedge, or proxy mesh in presentation code;
- clips either target into a local cross-section and presents the crop as a complete plate;
- reads cells or chunks as renderable/explodable bodies;
- reconstructs an overlap from boundary metadata instead of extracting the existing state;
- changes the `CrustVolumeState` digest when switching view or factor;
- creates a second request/options type with the same responsibility as `ExplodedRequest`;
- creates a second plate-volume, boundary-polarity, or deformation authority;
- moves only part of a plate for the reveal; or
- relies on color, labels, logs, or a proof ray as a substitute for visible geometry.

## 7. Falsifiable acceptance gate

Verification deliberately avoids new test-suite expansion. Build and structural diagnostics are
supporting evidence only. Acceptance requires fresh captures from the exported windowed app.

For one unchanged seed, tick, parameter set, and `CrustVolumeState` digest, capture:

1. the assembled globe;
2. the focused pair at factor zero; and
3. the focused pair with a small nonzero reveal factor.

The capture set passes only when the user's visual inspection can establish all of the following:

- the assembled globe remains closed and does not show buried crust;
- the focused views show exactly two intact curved plate bodies and no core or unrelated plate;
- the down-going plate visibly continues from its surface region into a descending volume beneath
  the overriding plate's footprint;
- the overriding plate visibly occupies the upper position and has a readable underside/sidewall;
- the reveal capture exposes more of the already-present descending body by moving only the
  complete overriding plate;
- the factor-zero companion restores the generated contact and does not contain an appended or
  detached feature;
- both focused captures retain the same state digest as assembled;
- no cell, chunk, crop, proxy overlap, or renderer-authored boundary geometry is visible; and
- the camera framing shows the relationship without clipping the shell.

If the two complete plates remain visually ambiguous after isolation, framing, and small rigid
reveal, Replacement B0 remains failed. Logs, tests, type names, or the existing interval proof
cannot override that result.

## 8. Implementation boundary

The implementation plan may change only the existing render request, render composition,
presentation contract, presentation binder, and evidence documentation required for this focused
projection. It should use no new tests unless a production signature cannot be compiled safely
without adjusting an existing test fake.

It must not:

- change volume generation or the convergent deformation;
- tune color;
- reinterpret crust thickness as literal core scale;
- implement collision, divergence, transform, LOD, or Slice C;
- replace the whole-globe exploded view; or
- claim that focused inspection completes the broader amplified mountain/trench/detail work.

## 9. Established and disproven conclusions

### Established

- The generated state contains an ordered overriding-then-down-going intersection before
  presentation.
- Both target bodies already extract as complete plates from the same `CrustVolumeState`.
- The current visual failure is caused by global scene composition and occlusion, not by color or
  a missing peer volume type.
- A focused projection can hide occluders and rigidly move an intact target plate without changing
  geological state.

### Disproven

- A whole-globe radial explosion with every plate and the core visible is sufficient visual proof
  of the local under/over relationship.
- A proof-directed near-polar camera alone can repair the presentation.
- Structural logs or digest equality are visual acceptance.
- More color or literal thickness calibration addresses this failure.

## 10. Completion statement

This design becomes implementation authority only after:

1. the user reviews and approves this written specification;
2. that approval is recorded in this file and committed; and
3. `writing-plans` produces a bounded implementation plan for the focused Replacement B0
   inspection.

No production implementation belongs in the design-approval step.
