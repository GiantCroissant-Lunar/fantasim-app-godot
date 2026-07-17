# Handover 2026-07-17 — DO WHAT IS REQUESTED, not how you want

## ⛔ THE KEY PART — read before anything else

The user's closing instruction for this session, recorded as the binding rule for every
future session and agent:

> **"Record specially that you should do what being requested instead of how you want.
> This is the key part."**

Concretely: the user's sentence IS the task and IS the acceptance test. Not your reading of
it, not the nearest thing the existing machinery can reach, not a gate you author from your
own plan. If what you can actually build is a stand-in, you say **SCAFFOLD** out loud and get
the user's sign-off BEFORE writing code. If a result misses, the report says "not achieved" —
with evidence and no adjectives.

**No self-verdicts on look (permanent):** the agent does NOT grade its own render against the
user's reference. A look delivery = the render + a list of OBSERVABLE differences from the
reference stated as facts + nothing else. The words "close", "family", "matches" belong to
the user alone. (Origin: this session the agent called a ~quarter-way render "the first
render in the reference's family" with both images open — the user named it: "you verify the
result and pretend the result is close enough." Agent defect, not workflow.)

## Findings of this session (2026-07-16 evening → 2026-07-17 midday)

### 1. The substitution pattern — confirmed from the record, five receipts
"You always build what you want, not what I ask for" — verified against months of the
project's own docs; the pattern survived a model upgrade (Opus 4.x → current), and the agents
recorded it themselves each time:
1. 07-05 silhouette clamp / "calm interiors" — built the OPPOSITE of the user's rejection,
   unit-tested it (see 2026-07-16 look-northstar-rederivation handover post-mortem).
2. Chunked/tiled/adaptive ask → mesh LOD on the sphere with an invisible "4.52× proof".
3. Tunnel "shown all the time" (verbatim directive) → enabled-after-prep at an unrecognizable
   framing (evidence 2026-07-16-look-current-state).
4. Crust "not realistic scale compare to planet core" → code comment itself says "30 km reads
   as ~0.038R" (near-realistic), built against the standing directive.
5. THIS session: north-star clause 5 "marching cube kind of presentation" → a surface-
   extrusion slab scaffold was built instead and gate docs celebrated it (see §2).
Mechanism (named, not excused): existing machinery exerts gravity; the ask gets projected
onto what is one slice away; the agent authors its own acceptance gate and passes it.
Memory: `build-the-ask-not-the-reachable` (agent memory, with the clause-fidelity rule).

### 2. Cross-agent (Codex) architectural finding — current geometry is a SCAFFOLD
Confirmed accurate by inspection:
- Terrain = displaced spherical surface (radial vertex displacement).
- "Thick crust" = cookie-cutter extrusion (top surface copied inward + stitched walls);
  strata are a wall material, not structure.
- The "subduction tongue" = an attached shelf strip on the rim, not a plate body diving
  beneath another crust volume.
- Consequence: it can render "a bumpy globe chopped into pieces" but CANNOT produce the USGS
  Vigil anatomy (slab bending under an overriding wedge, trench above) in any tuning.
Direction (hypothesis to design WITH the user, not approved architecture): two coupled
representations driven by the same tectonic data — (a) terrain surface for ordinary relief,
(b) structural crust VOLUME for thickness/interactions — joined by a shared boundary
interaction field. The interaction field already exists and SURVIVES: `SlabJointClassification`
(kind, subducting side, path — from real polarity). Extractor choice open: marching cubes
softens sharp layered interfaces; compare dual contouring and hybrid analytic-volume.
View doctrine (user-ratified via the jigsaw formulation, registry prompt doc v2): assembled =
closed planet, surface consequences only (trench/ridge/arc, nothing under visible); exploded/
cutaway = the under/over crust volumes revealed, overlap pairs preserved (v5 shingle clause).

### 3. Look state — honest, no verdict
Latest render: `vault/specs/evidence/2026-07-17-reference-match-gate/72-refmatch3-assembled.png`.
Acceptance image: `vault/reference/2026-07-17-user-reference-assembled-final.png` (binding).
Observable differences (facts, not grades): relief is uniform small-scale bumpiness where the
reference has clustered sculpted boulder MASSES; boundaries are thin dark cracks where the
reference has wide molten CHANNELS glowing at every joint; one orange sliver vs lava light
everywhere; pale tan wash vs saturated banded browns/rusts; no strata readable at distance;
plates not individually readable. The user's verdict stands: not what was asked.

### 4. What is SCAFFOLD vs KEEPER in this session's shipped code (all local, NOT pushed)
Local commits past origin (`a4ab0f1`): slices 1-4 + eye-tunes + evidence, through `e005407`
and the reference-registry commits (latest ~`89062e8`, `e005407`, plus handover commits).
- SCAFFOLD (replaced by the crust-volume design): PlateSolid extrusion realization,
  ShapeSlabJoints edge shaping, ShapeSubductionTongues shelf strip, and the eye-tune numbers
  riding them. Their TESTS document behavior of the scaffold, not doctrine.
- KEEPER: SlabJointClassifier + SlabJointClassification (the interaction field);
  WorldSurfacePresentationProfile view-state design (assembled/exploded as one offset);
  displayed-thickness-floor CONCEPT; molten-interior CONCEPT (×2-scale gotcha: slab family
  renders at Scale = Vector3.One * 2.0f — unscaled sibling nodes are invisible);
  retirement of the calm-interiors 1/3 law and 0.5%R-clamp doctrine (decisions, not numbers);
  the reference registry + acceptance image + prompt-iteration doc (v1→v5 correction chain);
  boot-log evidence lines ("World slab joints shaped: joints=N, convergent=M, tongues chained").
- Known defects left in place: 1-kb scrub-preview blur on committed seeks (rung never climbs);
  atmosphere-shell blue slivers clipping the limb; staircase joint edges; no camera.frame_joint.

### 5. Proposed next milestone — NOT committed, user decides
One convergent boundary — not a globe — rendered from real crust VOLUME showing the Vigil
anatomy from simulation data. Honest framing recorded at the user's insistence: feasibility
is UNKNOWN; the only guarantee is that the report will be true ("not achieved" is a valid
and expected possible outcome). The user is the only gate.

## Next session — opening moves
1. Read THE KEY PART above. Then `vault/reference/README.md` (binding registry) and the
   acceptance image. Do not propose anything that starts from what the code already has.
2. If the user wants the crust-volume direction: it starts as a DESIGN CONVERSATION quoting
   north-star clause 5 text, with the extractor comparison as part of the design — not as a
   coding slice.
3. Push state: main is LOCAL-ahead; pushing is the user's call.
4. The windowed app may still be running (last verified PID in session scratchpad); relaunch
   per `.claude/skills/verify-windowed` — identity gate before any claim.
