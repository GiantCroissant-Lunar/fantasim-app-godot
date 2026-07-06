# RFC salvage index — designs recoverable from the Dec 2025 – Jun 2026 eras

**Mined 2026-07-06** from the supermemory backups (1,223 unique docs) as the design-side companion
to the attempt ledger (`2026-07-06-half-year-attempt-ledger.md`). Most of these were designed and
NEVER implemented (or implemented and lost to restarts). Phase mapping targets the attempt-8
roadmap (P3–P7) and the long-term horizons (H2–H4, see `../plans/2026-07-06-long-term-roadmap.md`).

**Where the full bodies live:** NOT in any repo on this Mac (verified: `lunar-horse-001`'s
fantasim-hub is an infra shell; `lunar-horse-002` carries only an app clone). Recoverable text =
the `content` field of `supermemory-backup-20260619/full-export.jsonl` (per-RFC, quality varies;
`summary` holds ~700-char Purpose/Depends-On headers) + possibly the original Windows drives
(`D:\lunar-snake`, `C:\lunar-horse-002`). Treat the essences below as reliable, the algorithms as
needing re-derivation unless the content field proves richer for that RFC.

## Top salvages (ranked; the ones worth deep-reading before their phase starts)

1. **RFC-073 Ocean Basin Generation** (Draft, 01-09) — ocean mask, bathymetry, currents, coasts. → **H2** anchor.
2. **RFC-031 Hydrology & Drainage on Voronoi Mesh** (Draft) — the design behind May's G32–G45 lane (partially landed). → **H2**.
3. **RFC-027 Macro-Tectonic Initialization + RFC-029 Long-Term Tectonic Evolution** (Drafts) + the **RFC-0200-xxx plate-kinematics corpus** (Euler poles °/Ma, boundary networks, "Not Earth-only"). → **P5**.
4. **RFC-0001/0002 External Truth Providers** (VPLanet, Landlab; Proposed) — external sims as optional upstream truth via projection/reprojection. → **H4 oracles**.
5. **RFC-076 Volcanism** (**Accepted v1**, unbuilt) — provinces, eruptions, ashfall, CO₂ forcing. → **H2** + P6 atmosphere.
6. **RFC-078 Cryosphere** (Draft) — glaciation, permafrost, sea-level; ties to G117/G118 ice exports. → **H2**.
7. **RFC-074 Biosphere Biogeochemistry** (Draft; C/N/P + NPP) — the real design the May biosphere lane implemented ad hoc. → **H3**.
8. **RFC-048 Cartography Pipelines & Truth Contracts** + 044/045/080 (Drafts; 056 Implemented) — sim-truth vs map-aesthetic split. → **P3/H4**.
9. **RFC-065 Causality Doctrine + RFC-064 Driver→Event→Epoch** (were Implemented; impl lost) — the CED stack design. → P5/P6 + governs all.
10. **mung-bean dungeon-crawler ↔ sim-world design** (Dec 2025) — the only worked-out consumer/product contract (hash-chain integration, session-delta export, R/G realization/gameplay layers). → **H4** flagship.
11. **BimodalDistribution / Earth-like hypsometric curve + WidenPlateBoundaryEffects** noise design — directly on-target for **P3** calibration.
12. **Asthenosphere-promotion RFC + G123–G131** (05-25): `AsthenosphereProfile`, `ViscosityTier`, DuctileConvectionSolver, Euler MotionPath. → **P5/P6** mantle.

## Catalog A — fantasim-hub series (RFC-001…081, authored 2025-12-01→2026-01-11)

Statuses are as-stated at the 01-12 import; "Implemented" implementations were later lost to restarts.

| RFC | Title | Status | Feeds |
|---|---|---|---|
| 001/002 | Architecture overview; event sourcing + hash chains | Impl. | infra |
| 003/004 | Multi-script language evolution; canonical time + primordial language | Draft/partial | H3 |
| 005 | Traceable geology & climate | Superseded | P5/H2 |
| 006/007 | Traceable culture & politics; species evolution | Compl./Draft | H3 |
| 008–011 | Spatial topology; snapshots; causality rules; versioning | mixed | infra |
| 012–014 | ECS multi-world; WebGPU compute; Avalonia inspector | Impl./Deferred | infra/P3/P7 |
| 015 | Temporal terrain keyframes + heightfield import | Draft | P5/H2 |
| 016/017 | Tracery text layer; language↔text-gen bridge | Impl. | H3 |
| 018–026 | Constants; region polygons; world-creation pipeline; schema-driven model; project slicing; storage (RocksDB/UnifyStorage) | mixed | infra |
| **027/029** | **Macro-tectonic init; long-term tectonic evolution** | Draft | **P5** |
| 030/032–037 | Cartographic abstraction; view framing; hierarchical Voronoi; canonical sphere; MIConvexHull tessellation; quantization; Silk.NET globe | Draft/Def. | P3/H4 |
| **031** | **Hydrology & drainage on Voronoi mesh** | Draft | **H2** |
| 038/041/043 | World substrate authority; spherical sim authority; plate/site-count heuristics | Draft | P5 |
| 044–048 | Projection-native sampling; field reconstruction; vector styling; layer budget; **cartography truth contracts** | Draft (056 Impl.) | P3/H4 |
| 049–055 | MSBuild foundation; shared contracts; UnifySerialization; MapDocument/Mapsui; UnifyDiagnostics; MapView | Draft | infra/H4 |
| 056/058 | Projection catalog (**Implemented**); Robinson process alignment | | P3 |
| 057 | Cultural styling on maps | Draft | H3/H4 |
| 059 | Authoring/edits as first-class events (OpenGeofiction-style) | Draft | P7/H4 |
| 060–062 | Rule-pass readiness; deterministic rule passes; L2→L0 scope doctrine | Draft | P5/infra |
| 063–065 | Hierarchical cell resolution (**Impl.**); Driver→Event→Epoch (**Impl.**); causality doctrine (**Impl.**) | impl lost | P3/P5/P6 |
| 066/067 | Cultural/artificial selection; phenotype projection | Draft | H3 |
| 068–072 | Tiered services; service-archi; crosscut-foundation; canonical quantities; **sphere clocks/epoch gates/scheduler** | Active/Draft | infra/P5/P6 |
| **073** | **Ocean basin generation** | Draft | **H2** |
| **074** | **Biosphere biogeochemistry (C/N/P + NPP)** | Draft | **H3** |
| 075 | Noosphere (economy/trade/settlement) | Draft | H3 — **premature per 05-26 ruling** |
| **076** | **Volcanism (provinces, eruptions, ashfall, CO₂)** | **Accepted v1** | **H2**/P6 |
| 077 | Economic geology (ore deposits) | Accepted v1 | H4/H2 |
| **078** | **Cryosphere (glaciation, sea-level)** | Draft | **H2** |
| 079/080 | Density-field marching-cubes meshing; rendering authority boundary (meshes≠truth) | Draft | P3 |
| 081 | Biosphere bridge (disturbance + productivity) | Draft | H3/H2 |

Gaps: 039/040/042/052 unrecoverable; 082–099 mostly absent from the backups (series likely
extended beyond what was captured).

## Catalog B — repo-scoped families (Feb–Jun)

- **RFC-0200-xxx** plate topology/kinematics (planar boundary graphs, Euler poles, plate-pair
  observations; 0200-015 "Not Earth-only") → **P5**.
- **RFC-0300-xxx** DES runtime + CanonicalTick JSON encoding → P5/P7.
- **RFC-0400-002** MessagePack canonical encoding → infra.
- **RFC-0500-xxx** geometry topology kernel 2D + L3/L2/L1 scope + EarthDefault scale presets → P5/infra.
- **RFC-0010-001** L-axis single-writer doctrine → governs all.
- **RFC-0024** Tunnel Timeline (boom-hud spatial 3D timeline) → **P7**.

## G-series groups (May, fantasim-world task ladder — trust commits over `_index.md`)

- G32–G45 hydrology (lakes, watersheds, priority-flood) — keeper, mostly landed. → H2.
- G33–G60 biosphere lane — landed but ran AHEAD of geosphere (caution). → H3 retrofit via RFC-074.
- G61–G68 canonical-units/crust — keeper ("crust lane canonical-complete").
- G69–G77 climate + sphere migration (temp/precip belts, seasonality) — keeper. → H2.
- G80–G94 erosion/soil/framework — → H2.
- G102–G121 hydrosphere export integration (ET, ice, groundwater) — → H2. **G106 noosphere
  settlement = the premature run-ahead** (user 05-26: "noosphere is very case-by-case… a default
  one won't fit"; base world stays neutral, consumers add overrides 05-17).
- G123–G131 asthenosphere promotion + SST — → **P5/P6**.
- G110 habitability (4 known-failing baselines); G143 CLI plate-time scoping → P7; G149–G151
  produce-flow refactor → P7/infra.

## Usage rule

Before writing any P5/P6/P7/H2+ plan: pull the relevant rows here, extract that RFC's `content`
from the 20260619 export, and salvage the design skeleton BEFORE drafting anew (session-goal-
contract: prior-attempt audit). USD/DCC interchange was ruled "too early" (05-28) — do not revive
without an unlock.
