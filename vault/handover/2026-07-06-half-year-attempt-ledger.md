# FantaSim attempt ledger — Dec 2025 → Jul 2026 (reconstructed from supermemory backups)

**Source:** 1,235 unique docs from `supermemory-backup-20260619/-0626/-0705` (526 fantasim-relevant),
mined 2026-07-06 because most of this history was never committed (repos restarted repeatedly).
**Why it exists:** the anti-circling rule requires a prior-attempts audit; this is the audit's
source of truth. The user counts ~8 attempts; the doc record resolves 6–7 clear eras (attempt 0
known only by reference; May's re-foundation is arguably its own).

## Attempt chronology

**Attempt 0 (pre-Dec 2025) — build-world.** Known only by reference: RFC-001 (2025-12-01) calls
sim-world a "ground-up refactoring of build-world."

**Attempt 1 (Dec 1–18, 2025) — sim-world (`D:\lunar-snake`).** RFCs 001–011: hash-chained event
sourcing, canonical time, traceable geology; ArangoDB store, WorldStateProjector/ReplayEngine;
mung-bean dungeon-crawler as consumer. **Nothing planetary rendered** (console hosts). Ended in a
storage pivot (ArangoDB → LiteDB/LMDB, UnifyStorage RFCs).

**Attempt 2 (Dec 22 – Feb 1) — fanta-world → fantasim-inone + fantasim-hub (`C:\lunar-horse-002`).**
RFC explosion 027–081 (macro-tectonics, hierarchical Voronoi, canonical sphere, Silk.NET globe,
cartography, hydrology, cryosphere, oracles, Tracery). **81 RFCs; the furthest visual evidence is
2D Mapsui maps** (Robinson projection "Implemented" 12-30); the Silk.NET 3D globe stayed Draft.
Tier model (RFC-068/069) and L2→L0 causal levels born here ON PAPER. By 02-18 fantasim-inone is
"on the old drive and needs a clean rewrite."

**Attempt 3 (Feb 14–16) — the identity fork.** "FantaSim" briefly meant a DIFFERENT product: a
self-building coding-tycoon TUI (Fluid-HTN, multi-agent panels — ~140 delegation docs of noise).
**The hot-reload/ALC PCK bundle shell — today's defining architecture — was invented here, for
the tycoon (02-16), and inherited by the planet app two days later.**

**Attempt 4 (Feb 18–21) — fantasim-world rewrite + fantasim-app-godot (`C:\lunar-horse`).**
Bootstrap from hub docs; truth stream, plate topology contracts, kinematics libraries. Visual
peak: **3 plates on an icosphere with motion arrows** (hardcoded Euler poles, 02-19). Died
mid-bugfix on broken rotation math: reconstruction "sums rate*dt … instead of composing
quaternion rotations" (02-21). Then the gap.

**The March–April void.** 189 March + 3 April docs — **none fantasim**. A Unity game
(`ray_collab`: boss phases, HUD, Text Animator) consumed the whole period; early May continues
Unity/articy before FantaSim resumes ~05-16.

**Attempt 5 (May 11 – Jun 18) — same repos, re-foundation.** G-series solver grind (G42→G150+:
hydrosphere, cryosphere, erosion — judged premature 05-26); SurrealDB-in-Rust store; icosphere
LOD; node-graph pipeline; 4-tier T1–T4; hot-reload "globe pattern"; CanonicalTick unification;
planetary regimes. Visual: globe viewer with multi-tick snapshots (05-17), **geological
reconstruction of plate movement verified on the globe (06-03)**, regime crossfade (06-15).
Ended by migration, not failure: phased D:→C: restoration (06-10), then to the Mac.

**Attempt 6 (Jun 19 → present) — Mac era (`/Users/apprenticegc/Work/lunar-horse`).** Restoration
audits; doctrinal reset (time-neutral engine, motion-spine-first, Plans 1–4); "rubber-ball with
strips" → lit terrain (06-21); watertight Cartography.Globe; blue 6-cap onset globe
windowed-verified (06-22); July: locked world-view look, cutaway, host-slim, and (07-06) the
motion-death diagnosis + attempt-8 recovery this ledger belongs to.

## Recurring failure themes (dated evidence)

1. **Spine built, wired to nothing / frozen topology** — "motion/reconstruction spine is built but
   wired into nothing while features are derived on frozen topology (recurring)" (06-20); rule
   locked 06-21; STILL the root cause found 07-06. → countered by station contract 6 + C3 gate.
2. **Broken rotation math, repeatedly** — quaternion-composition bug (02-21); "hand-rolled
   Vector3d vs UnifyMaths" still flagged 06-23. → countered by the Unify-reuse rule + P2-E's
   UnifyMaths-only mandate.
3. **Generation-vs-simulation confusion** — "reference tools are one-shot GENERATORS, we built a
   time SIMULATION" (06-21). → doctrine now explicit (t=0 emitter framing).
4. **Degenerate meshes / ball never a world** — independent-triangle cells (06-21), "seams +
   organic-continents still gaps" (06-22). → watertight caps landed; organic continents = P2.
5. **Unreconciled duplicate subsystems** — "two motion approaches never reconciled" (06-20);
   dead GlobeView pipeline misleading diagnosis (07-06). → proxy ban C4 + retirement discipline.
6. **Agents drifting from canon** — "non-canonical CLI changes and incorrect globe modifications"
   (06-03); biased-reconstruction memory. → session-goal-contract rule + conformance gates.
7. **Placeholder verification** — "pipelines functional but rely on placeholder smoke tests"
   (06-19). → no-smoke rule + the two mandatory gates.

## Infrastructure-vs-domain balance

Roughly **70–80% of doc volume is infrastructure/process**, not planet: Dec = storage plumbing;
Jan = RFC mass (much meta); Feb 14–15 alone = ~210 lines of TUI delegation noise; May–Jun =
bundles/ALC, tiers, node graph, DI, restorations. Storage churn alone spans FIVE backends
(ArangoDB → LiteDB/LMDB → RocksDB → SurrealDB C# → SurrealDB-in-Rust). **Domain/look work
clusters in short bursts (02-19–21, 06-03, 06-14/15, 06-20–22) — and those bursts are where all
visible progress happened.**

## Striking facts

- The ALC/hot-reload architecture predates its own product: invented for the abandoned tycoon.
- Every restart correlates with a workspace/drive migration (D:\lunar-snake → C:\lunar-horse-002
  → C:\lunar-horse → D:/C: shuffle → Mac). Migrations, not technical dead-ends, killed attempts.
- Paper-vs-pixels: 81 RFCs by January; the era's best visual was "3 plates with motion arrows."
- The original consumer (mung-bean dungeon crawler) vanished after Dec 2025, never to return.
- **The screenshot/exported-windowed-app verification loop (first seen 05-17) is the single
  process change that consistently preceded real visual progress** — it became doctrine, and its
  scripted form is now one of the two mandatory gates.

## How this ledger is used

Per the anti-circling rule and session-goal-contract: before proposing anything in the planet
domain, check this ledger + the circle map. Anything resembling themes 1–7 must name its
countermeasure. The ledger is append-only history; the roadmap
(`2026-07-06-attempt8-recovery-roadmap.md`) is where the present lives.
