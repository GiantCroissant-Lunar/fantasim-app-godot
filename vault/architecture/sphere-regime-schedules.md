---
source: fantasim-app-godot/project/plugins/App.World.Composition/SphereRegimeScheduleDefaults.cs,
  GeosphereStagnantLidLayer.cs, AtmosphereBulkLayer.cs, AtmosphereCoupledLayer.cs, OnsetRoster.cs
  + contracts/App.World/Composition/SphereRegimes.cs, RegimeSurfaceKind.cs +
  fantasim-world/project/plugins/Atmosphere.Genesis.Core/{AtmosphereForcing,PrimordialAtmosphereSolver}.cs
  + App.World/Services/Service.cs, App.World/Globe/GlobeReconstructor.cs,
  App.Presentation/PlanetPresentationBinder.cs (authored 2026-07-14)
source-status: current-code documentation
distilled: 2026-07-14
divergence: >
  Scoped to the geosphere + shipped atmosphere schedule only, per task (hydrosphere/biosphere have
  no schedule to document). Does not restate timeline-lane/track UI (owned by the still-missing
  P0 timeline-core doc) or the world-generation-graph regime-node feature (owned by P1 item #8) —
  both consume RegimeAt but are documented as separate paradigms elsewhere. OnsetRoster's plate-seed
  math is summarized, not reproduced (it is the onset-roster building block, not the schedule itself).
---

# Sphere regime schedules

**Doctrine:** [`planet-stack-model.md`](../../../fantasim-hub/vault/architecture/planet-stack-model.md)
§5 (Regimes — the time axis within a sphere) and §7 (cross-sphere gate graph). This doc covers only
how the app **implements** that doctrine today; it does not redefine the regime concept, the
Sphere/Regime/Layer axis split, or the L×R×M truth model — read the hub doc first.

## Doctrine (what must hold)

- `RegimeAt(sphere, tick)` → the active layer set for that tick (§5). Exactly one regime is active
  per sphere per tick; the render face may crossfade near boundaries, but that is
  presentation-only, never part of the schedule.
- The geosphere's three regimes (`magma-ocean` → `stagnant-lid` → `mobile-plate`) are **locked**
  doctrine (§5 table).
- Other spheres' regime names are **not locked**. The atmosphere's shipped schedule
  (`primordial-steam → secondary-co2 → coupled-climate`) is production reality but the
  lock-or-reject decision is explicitly **open** (§5, "Other spheres' regime names are NOT yet
  locked" + hub audit note).
- Cross-sphere gating (§7): atmosphere hydration crossing a threshold gates geosphere onset — this
  is the **Option A** causal thesis, proven in the app 2026-06-16 per the hub doc's built-reality
  note.

## Built (code-cited)

### Regime schedule shape

`SphereRegimeSchedule(SphereId Sphere, IReadOnlyList<SphereRegime> Regimes)` — record,
`contracts/App.World/Composition/SphereRegimes.cs`. `SphereRegime(RegimeId, StartTick, EndTick,
ActiveLayers, DefaultColorByField, ShowsPlateFeatures)` is one half-open `[StartTick, EndTick)`
window. `SphereRegimeSchedule.RegimeAt(long tick)` is a linear scan over `Regimes`, returning the
first regime whose `Contains(tick)` is true (or `null` for an ungapped-but-malformed schedule).
`IsRegimeTransition(previousRegimeId, tick)` reports whether the playhead crossed a regime boundary
since the last bind — used to gate expensive re-fetches (see Rendering handoff below), not to
gate composition itself.

### Geosphere schedule

`SphereRegimeScheduleDefaults.GeosphereFor(long onsetTick)`
(`App.World.Composition/SphereRegimeScheduleDefaults.cs`) builds the three locked regimes:

| Regime | Window | Active layers | `DefaultColorByField` | `ShowsPlateFeatures` |
|---|---|---|---|---|
| `magma-ocean` | `[0, MagmaOceanEndTick)` | `geosphere.magma-ocean` | `SurfaceTemperature` | false |
| `stagnant-lid` | `[MagmaOceanEndTick, onsetTick)` | `geosphere.stagnant-lid` | `HeatFlow` | false |
| `mobile-plate` | `[onsetTick, ∞)` | `geosphere.plate`, `geosphere.crust`, `geosphere.mantle` | none (plate coloring) | true |

`MagmaOceanEndTick = 1_000_000` ticks (const, stylized R1 boundary). `geosphere.stagnant-lid`
(`GeosphereStagnantLidLayer : IFieldProducer`) produces `CrustThickness` and `HeatFlow` per cell,
lerping crust thickness from a thin proto-crust (5,000 m) toward the **same formula**
`SyntheticCrustLayer` uses, closing the lerp exactly at `onsetTick` — authored C0-continuous so
scrubbing across the lid→plate boundary does not pop (`GeosphereStagnantLidLayer.Produce`).

### Onset: hydration-gated, computed from the atmosphere solver

`SphereRegimeScheduleDefaults.PlateOnsetTick` is a `static readonly long`, computed once at type
load as `PlateOnsetTickFor(AtmosphereForcing.Default)`. `PlateOnsetTickFor(AtmosphereForcing
forcing)` runs `ComputePlateOnsetTick` — a binary search over `[0, 1e9]` ticks for the smallest
tick where `PrimordialAtmosphereSolver.GetStateAtTick(tick).SurfaceHydrationIndex >=
HydrationOnsetThreshold` (`0.99`). `PrimordialAtmosphereSolver`
(`fantasim-world/project/plugins/Atmosphere.Genesis.Core/PrimordialAtmosphereSolver.cs`) is a pure
function of `(tick, forcing)`; `AtmosphereForcing(double OutgassingScale = 1.0)` is the tunable —
`SurfaceHydrationIndex = clamp(OutgassingScale * (t - 1e6) / 1e8, 0, 1)`, so a stronger scale
hydrates faster → earlier onset. With `AtmosphereForcing.Default` this yields onset at tick
`100_000_000` exactly, matching the hub doctrine's proven A/B (default → 100 Ma vs scale 2.0 →
50.5 Ma).

**Onset seeds the plate roster, not just the schedule window.** `OnsetRoster.Build` (same plugin,
`OnsetRoster.cs`) folds `LidFractureAtOnset.Fracture` (engine convection) into a
`PlateTopologyState` at the onset tick, producing the N-plate roster and geometry seeds
(`PlatesAt`/`SeedPlatesAt` return empty before `onsetTick`, populated at/after). This is the
concrete mechanism behind "the lid fractures into N plates" (doctrine §5/§6) — upwelling-seeded,
deterministic per `(seed, tick)`.

### Atmosphere schedule

`SphereRegimeScheduleDefaults.AtmosphereFor(long onsetTick)` builds the shipped, **not locked**,
three-regime schedule, phase-aligned to the same `onsetTick` and `MagmaOceanEndTick` boundaries as
the geosphere:

| Regime | Window | Active layers |
|---|---|---|
| `primordial-steam` | `[0, MagmaOceanEndTick)` | `atmosphere.bulk` |
| `secondary-co2` | `[MagmaOceanEndTick, onsetTick)` | `atmosphere.bulk` |
| `coupled-climate` | `[onsetTick, ∞)` | `atmosphere.bulk`, `atmosphere.coupled` |

`atmosphere.bulk` (`AtmosphereBulkLayer : IFieldProducer`) is active across **all** of time and
produces uniform (0-D, every cell identical) `AtmosphereGreenhouse`/`AtmosphereHydration`/
`AtmospherePressure` from the same `PrimordialAtmosphereSolver`. `atmosphere.coupled`
(`AtmosphereCoupledLayer`) activates only in `coupled-climate` and produces a latitude-banded
`AtmosphereSurfaceTemp` (warm equator/cold poles, lifted by the time-varying greenhouse baseline).
Both constructors accept an optional `AtmosphereForcing` (default `null` → baseline curve).

### The onset-tick single-source-of-truth caveat

`GeosphereFor`/`AtmosphereFor` both take an explicit `onsetTick` parameter and are forcing-agnostic
by signature. In practice, every production call site in `App.World/Services/Service.cs` (11+
call sites) and `App.World/HostComposition/CellElevationComposition.cs` reads
`SphereRegimeScheduleDefaults.PlateOnsetTick` — the **static, default-forcing-only** value — and
threads that same tick into both `GeosphereFor`/`GeosphereDefault` and `AtmosphereFor`. The
`PlateOnsetTickFor(forcing)` overload that actually varies onset by forcing is exercised only in
tests (`App.World.Composition.Tests/SphereRegimeScheduleTests.cs`,
`App.World.Composition.Tests/SeedPlatesAtTests.cs`,
`App.World.Composition.Tests/OnsetRosterTests.cs`) — no runtime knob wires a non-default
`AtmosphereForcing` into the live schedule today (see Not built/open).

### Rendering handoff (era/regime-band presentation)

- `RegimeSurfaceResolver.Resolve(string? regimeId)` (`contracts/App.World/Composition/
  RegimeSurfaceKind.cs`) is a pure `regimeId → RegimeSurfaceKind` map (`MagmaOcean`,
  `StagnantLid`, `MobilePlate`, `Default` fallback) that `PlanetPresentationBinder` maps to a
  concrete Godot mantle material.
- `GlobeReconstructor.ShowsPlateFeatures(tick)` (`App.World/Globe/GlobeReconstructor.cs`) gates
  every plate-feature query (boundary arcs, boundary-cell classification, junctions) with THREE
  checks in order: an independent `tick < _onsetTick → false` gate first, then a
  `_regimeSchedule is null → true` legacy branch, then `RegimeAt(tick)?.ShowsPlateFeatures ?? true`.
  Net effect: pre-onset ticks return empty plate-feature output rather than stale/garbage arcs,
  even if the schedule were absent or disagreed about onset.
- `PlanetPresentationBinder.ApplyTimelineTick` (`App.Presentation/PlanetPresentationBinder.cs`)
  re-fetches the whole presentation document only when `GeosphereSchedule.RegimeAt(tick).RegimeId`
  differs from the previously-bound regime id (`IsRegimeTransition`-style gating, inlined); a
  same-regime tick change only re-checks the crust-snapshot-tick boundary
  (`CrustSnapshotTickSeries`), never a full rebuild.
- `PlanetPresentationDocument.GeosphereSchedule` / `.AtmosphereSchedule` / `.MaxTick`
  (`contracts/App.World/PresentationLayers.cs`) carry both schedules out to
  `App.Presentation/PlanetTimelineController.cs`, which exposes them as `GeosphereSchedule`/
  `AtmosphereSchedule` properties for downstream track/lane consumers (the timeline-lane UI itself
  is documented separately — see divergence note above).

## Not built / open

- **Regime-name lock/reject decision (atmosphere + hydrosphere/biosphere) is the user's call,
  still open.** The hub doctrine explicitly defers it; this doc does not resolve it.
- **Hydrosphere and biosphere schedules do not exist in code.** No `SphereRegimeScheduleDefaults`
  equivalent, no layers, no regime names beyond the doctrine doc's illustrative examples
  (`dry → oceans → ice`, `absent → microbial → complex`). Nothing to cite.
- **The atmosphere-forcing → onset-shift path is code-complete but not runtime-wired.**
  `PlateOnsetTickFor(AtmosphereForcing)` works and is unit-tested, but every production call site
  uses the zero-arg `PlateOnsetTick` (baked to `OutgassingScale = 1.0` at type-load). There is
  currently no UI knob, config surface, or command that constructs a non-default `AtmosphereForcing`
  and threads it through `GeosphereFor`/`AtmosphereFor` in the live app — the hub doctrine's
  "atmosphereOutgassingScale knob measurably moved onset" A/B was demonstrated through this same
  test-level API, not a wired runtime control.
- **`flow-drives-drift` and the yield-stress fracture gate are not built** (hub §6 built-reality
  note, reconfirmed here): `OnsetRoster` seeds plates from upwelling centers at a single onset
  tick; ongoing plate drift does not consume the live convection flow field.
- **Cross-sphere gating beyond the one proven arrow is illustrative only.** Only "atmosphere
  hydration → geosphere onset" is implemented and tested; the hydrosphere-condensation and
  biosphere-onset arrows in doctrine §7 have no code counterpart.
