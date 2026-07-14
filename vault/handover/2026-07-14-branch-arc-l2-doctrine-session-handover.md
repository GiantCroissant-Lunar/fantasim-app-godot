# Session handover — 2026-07-14 (branch arc, L2 alignment, doctrine audit, hub recovery)

**Next session's contract (user-set): ORGANIZE DOCS FIRST, before implementing anything.**
The user is bringing **fantasim-hub into `yokan-projects/`** — it exists (was never lost),
carries Oct-2025→May-2026 discussion history and the original doctrine docs
(`lrm-axis-model.md`, `variant-and-branch.md`, RFC-0500-004, `domain-organization.md`).
Read-order for the next session: this handover → engine
`vault/architecture/variant-fantasy-doctrine-recovered.md` → engine
`vault/handover/2026-07-14-stack-model-validity-audit.md` → then the hub once it lands.

## 1. What shipped this session (all committed, NOTHING pushed)

### fantasim-app-godot (main)
- `916dd4d` — Cartography.Globe.Core package migration completed (tree was unbuildable;
  contracts stay project refs, impl from local feed; assembly names identical so no ALC split).
- `93e3a9a` — merge of `fix/p9a-review-followups`: fail-closed `rotationSourceKind`
  (unknown kind throws BEFORE the durable selection append), plate-id collision rejection,
  lead-owned drifting-axis kinematics oracle. 640/640 at merge.
- `ee8bf4c` — **spline tunnel slice 1 (bent bore)**: `TunnelBoreSpline` (parallel-transport
  frames, FNV-1a branch seed + SplitMix64 phases, C1 ramp, cap 0.12 rad/unit, EXACTLY straight
  through depth 7.5 = whole interactive near-field), `TunnelBoreSegments`, pick guard
  `InteractiveThroatZ = -12.5`, corridor walls/filmstrips/dark shell on bore frames.
  Windowed gate PASSED (fresh export, tunnel via `timeline.tunnel_view`, zoom-out 0.61 exposes
  the far bore; evidence `/tmp/fantasim-spline-tunnel-gate/`).
- `43b6e3a` — **L-axis migration (L0→L2)** + `WorldStreamVocabulary` (App.World.Composition,
  deliberately NOT T1 contracts — ALC type-identity) as the SOLE production minting point;
  `StreamVocabularyGuardTests` source-scan bans raw identity construction; IP-shaped world ids
  throw at mint (the ingress ActorId→WorldId leak now fails loudly); variant/branch
  transposition in `TrackPipelineNodeCatalog` fixed. Durable SurrealDB restart proof passes on
  L2 cross-process. Suites 653/110/253/339.

### fantasim-world (main)
- `e64fec3` — materializer fix: absolute-samples-then-SLERP for ALL plates (stable chains had
  the forbidden interpolate-relatives-then-compose), missing-ancestor interpolation (was silent
  identity), `RelativeRotation` preserved for consumers (GLM's first cut dropped it — only the
  integrated app run caught it; LESSON: engine reconstruction changes must gate with the app suite).
- `794e7de` — **slice 2a: `world.branch-created.v1` ledger + one-level `BranchComposition`**
  ({world}:main:**L2**:world:branches, CAS + parent-cursor hash verification, idempotent
  replay, composed playback never copies/rehashes parent events, nested parents fail closed).
  Engine 651 tests / 24 projects green.
- `7d1c09b`, `b939273`, `8c0fd78` (app), `2a6b9d1` (app), `b938efc` (app) — the specs/plans:
  spline-tunnel design, slice-1 plan, branch-created concept-lock + slice-2a plan, L-axis
  decision + migration plan.
- `3dc13c2` — recovered variant/fantasy doctrine deposit (see §3).

### Reviews run
- 5-agent adversarial review of the canonical slice (deposit:
  `vault/handover/2026-07-13-canonical-slice-adversarial-review.md` — STILL UNTRACKED in the
  app repo; commit or discard during doc organization).
- 3-agent stack-model validity audit (deposit committed: engine
  `vault/handover/2026-07-14-stack-model-validity-audit.md`).

## 2. Live state

- Exported app RUNNING: PID 22715, log `/tmp/fantasim-windowed-*.log` (latest), launched from
  the spline-tunnel export; world.pck hot-reloaded post-L2-migration (`old ALC collected for
  bundle world` verified). Remote ingress :19292 healthy.
- Engine packages in the local feed are **0.1.12 — STALE** (predate `e64fec3` + `794e7de`).
  Project-reference mode (default) is current; package-mode consumers need a republish.
- Worktrees: `.agent/run/worktrees/fantasim-fix-p9a` (branch merged — prune when convenient);
  `yokan-projects/.worktrees/p9b-app` + `resident-test-fixtures` (pre-existing, not mine).

## 3. The doctrine/document situation (NEXT SESSION'S MAIN JOB)

**Verdict from the 3-agent audit:** planet-stack-model.md is a live authority; drift clusters
where axis meaning lives only in prose. The L2 migration + vocabulary guard fixed the
mechanical class. What remains is DOC work:

1. **fantasim-hub lands in yokan-projects.** Concrete state (established 2026-07-14 by the
   user's other session, on the machine that held the C: copy): remote
   `git@github.com:GiantCroissant-Lunar/fantasim-hub.git` (private) now carries TWO completely
   unrelated histories — **`main` (248 commits, through May 2026: the Oct-2025→May-2026
   discussion era — old handovers/RFCs/observatory history and the doctrine originals
   `lrm-axis-model.md` / `variant-and-branch.md` / RFC-0500-004 / `domain-organization.md`)**
   and **`curated-restoration` (46 commits, Jun 10-16 2026: the deliberately re-chartered
   "curated restoration hub", pushed non-destructively from the C: copy; local main there
   tracks it)**. No common ancestor; no force-push was performed. When cloning into
   `yokan-projects/`, fetch BOTH branches — the doc-organization pass must decide the authority
   relationship: the curated line was an intentional fresh start, so likely
   curated-restoration = the go-forward hub with the 248-commit main mined for doctrine
   originals and discussion history (then reconcile the workspace's recovered-doctrine bridge
   against whichever line wins per doc). Cloning is a user-approved structural step — the user
   said they'll bring it in; if delegated, clone WITHOUT creating anything new beyond the clone.
2. **Reconcile** engine `vault/architecture/variant-fantasy-doctrine-recovered.md` (recovered
   this session from supermemory backups — source doc ids inside) against the real hub docs.
   Recovered highlights: variant axis = lawsets `science`/`wuxing`/`high-magic`; coupling laws
   "chi, mana, wuxing"; mana = CED domain potential/driver like heat (RFC-063/064/065/071,
   "Ambient Mana Density"); variation combination = opinion-strength overlays (FieldComposer IS
   this) + plugin-presence additivity (collectible bundles ARE this) + sphere concurrency.
3. **Owed doc amendments** (audit list): planet-stack-model §2 (real domain vocabulary — three
   conventions coexist: `geo.plates.topology`+`M0` vs `geosphere`+`plates` vs `world` control
   domain — plus L2 migration note + variant/branch value convention), §5/§10 (lock or reject
   shipped atmosphere regime names), §6 (flow-drives-drift + yield-stress gate unbuilt; interim
   inverse-projection mantle), §7 note + §9 ledger (rewrite — understates built reality; app
   repo needs its own column); `world-gen-design-direction.md:127` still carries the superseded
   "L0 = base geosphere" gloss; `ILayer.cs:39` stale comment; reconcile track-registry
   per-track StreamId with the "only the generator face binds truth" rule.
4. **Decide doc topology/authority chain**: hub vs engine vault vs app vault — who owns
   doctrine, who owns dated concept-locks, who owns implementation plans (today: hub=doctrine
   history, engine vault=world doctrine + engine specs, app vault=app specs/plans/handovers).

**Only after the doc pass:** the staged implementation arcs, in recommended order:
- **Variant-recipe slice** (wuxia prerequisite; audit defined the minimal slice + gate:
  realistic+wuxia disjoint in one process across restart; recipe = variantId → base, seed,
  tuning overlay, lawset/CosmologyId, enabled domains/coupling laws, bundle set — variants
  compose on the LAW axis the way slice-2a branches compose on the HISTORY axis).
- **Slice 2b** (branch-aware coordinator: import-into-new-branch replacing the "new branch
  required" rejection, per-branch selection/recovery, ListBranches for the tunnel) — engine
  primitives all landed in 2a.
- **Cross-L composition concept-lock** (game-consumer axis; L is labeling-only today).
- Tunnel slice 3 (junction seams + throat stubs) after 2b; flight mode after the eye pass.

## 4. Owed gates and eye sittings

- **USER EYE: spline-bore curvature feel** — app is open, F9 / `timeline.tunnel_view`; knobs =
  `TunnelBoreContract.StraightRadius (7.5) / CurvatureCapRadPerUnit (0.12) / RampLength (1.5)`;
  bend is deliberately subtle at head-on framing; raising cap toward ~0.2 and/or straight
  radius toward ~6 is the first move if it should announce itself. Resident code → full
  re-export per iteration.
- **Exported-app IMPORTED-rotation gate** (canonical review, still owed): real `.rot` import
  in the exported app on a durable backend → PCK reload → bound-cursor rediscovery →
  ALC collection. The durable proof exists only as a dotnet-test host gate.
- Engine package republish (0.1.13) when package-mode consumption next matters.
- Engine draft operators' `lLevel` fallback of 0 (`TruthStreamCommitOperator.cs:38`,
  `FieldContributionDraftOperator.cs:37`, `PlateSeedDraftOperator.cs:42`) — audit-flagged,
  NOT yet fixed (engine-side; fold into the doc-pass follow-up or the next engine slice).

## 5. Session gotchas worth keeping

- **cwd drift bit four times** (G9 lives): background/foreground shells reset cwd — prefix
  EVERY repo op with an absolute `cd`.
- `task … | tail` masks exit codes (known gotcha, self-caught once) — verify by artifacts.
- **/tmp git worktrees cannot restore** — NuGet's upward config walk never reaches the
  workspace root; put worktrees INSIDE lunar-horse (`.agent/run/worktrees/`).
- `.NET string literals are UTF-16` — plain `strings` misses them; search bytes UTF-16-LE.
- GLM packets behaved excellently with explicit escape hatches ("if plan value wrong, correct
  it and SAY SO") — the golden-seed correction and the theory-count reconciliation both used
  them properly. Keep writing hatches into packet prompts.
- Putting shared vocabulary in T1 contracts can SPLIT ALC type identity — vocabulary lives in
  the collectible plugin instead (`WorldStreamVocabulary` deviation, verified good).
- Supermemory backups (`supermemory-backup-20260619/`) hold pruned-forever session records —
  `pV3aAwbz2U` (the 2026-05-16 LRM/variant session) exists ONLY there until the hub lands.
