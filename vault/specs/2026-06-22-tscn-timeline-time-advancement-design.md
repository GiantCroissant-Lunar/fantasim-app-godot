# .tscn-native timeline for time advancement — design

> **AUDIT (2026-07-06, code-verified):** SHIPPED — Timeline.tscn/TimelineFace live; §2/§9 anchors (GlobeView.BuildScrubber, RegimeTimelineTransport) are dead-code references. _(See the authority index in `vault/README.md`.)_


> status: concept-lock 2026-06-22 · DESIGN ONLY (no code change yet)
> repo: fantasim-app-godot · supersedes: the Plan 5a boom-hud timeline face (`TimelineViewSource`/`TimelineModel`)
> anchors (both in the **fantasim-world** repo): `vault/architecture/planet-stack-model.md` (§2 stream-id · §5 regimes · §7 Option A gate graph · §8 timeline vocabulary); `vault/architecture/canonical-foundation.md` (CT + odometer ladder)

## 1. Why

The app advances time with a Godot `HSlider`, and its `AnimationPlayer`/`AnimationTree` "transport" is a shell — the animations are empty placeholders and the tick is actually advanced by a hand-written accumulator loop. We have a timeline *concept* but nothing that *is* the timeline.

This replaces both with a **Godot-native, editor-authored `Timeline.tscn`** in which a real `AnimationPlayer` value-track drives the canonical tick, a **continuous playhead** replaces the slider, and the visible structure is a **multi-lane** read-out of the world model (sphere lanes, layer tracks, regime sections) addressed by the truth-stream identity. Time is labelled in canonical ticks via the odometer ladder, never `Ma`/`Ga`.

## 2. Current state

- `project/plugins/App.World.Seam/GlobeView.cs` — `BuildScrubber()` (~498–531) builds the `HSlider`; `SetTick`/`SetRegime` drive the globe.
- `project/plugins/App.World.Seam/RegimeTimelineTransport.cs` — `AnimationPlayer` + `AnimationTree` (idle/playing/scrub) exist, but the animations are **trackless placeholders**; the tick is advanced by a manual `_tickAccum += ticksPerSecond * delta` loop in `_Process`.
- `project/plugins/App.Timeline/{TimelineViewSource,TimelineModel}.cs` — the Plan 5a boom-hud face, authored 100% in C# (`RuntimeSurfaceDocument`); its `.tscn` is generated at runtime by the boom-hud renderer. The boom-hud basic catalog is flexbox, so the playhead could only be regime-level, not continuous.
- `project/contracts/App.World/Composition/ITimelineController.cs` — resident bridge (Tick/MaxTick/IsPlaying/schedules/Play/Pause/SeekTo/`TickChanged`).
- `project/contracts/App.World/Composition/SphereRegimes.cs` — `SphereRegime` + `SphereRegimeSchedule.RegimeAt(tick)`.
- `project/plugins/App.World.Composition/SphereRegimeScheduleDefaults.cs` — `HydrationOnsetThreshold = 0.99`; `PlateOnsetTickFor(forcing)` (hydration-derived) vs `PlateOnsetTick` (the constant default actually in use); `GeosphereFor(onsetTick)` / `AtmosphereFor(onsetTick)`.

## 3. Decisions

1. **Godot-native `Timeline.tscn`**, editor-authored, shipped in the existing hot-reloadable `timeline` collectible bundle (repurpose `App.Timeline`). Retire `TimelineViewSource`/`TimelineModel`.
2. An **`AnimationPlayer` master value-track** keyframes the canonical tick (CT) linearly `0 → maxTick`; playback position **is** the continuous playhead. An **`AnimationTree`** state machine (idle/playing/scrub) is the transport.
3. The continuous playhead **replaces** the `HSlider` (remove `GlobeView.BuildScrubber`/`_slider`).
4. **Multi-lane** structure built from the world model (§4), using the locked Godot vocabulary: track-group = sphere · track = layer/field · section = regime · state-machine = regime selector · blend = crossfade.
5. **Time in CT, labelled via the odometer ladder** (`ka`/`kb`…), never `Ma`/`Ga` (§5).
6. **Stream-path = track address** — each track is one layer = one truth stream (§4).
7. **One timeline per `(variant, branch)`**; a separate selector chooses them (§10) — a documented fast-follow, not MVP.
8. **Cross-sphere conditioning is emergent** (Option A, §6): section boundaries are gate outputs, not keyframes.
9. **Controller seam flips** (§9): the bundle's `AnimationPlayer` becomes the single time source; `ITimelineController` is adjusted to push ticks to the resident globe.
10. **Colours carry doctrine** (§8): lane = sphere, section = regime, track = layer.
11. **MVP lanes** = geosphere (`geosphere.plate` + `geosphere.crust`) + atmosphere (`atmosphere.bulk` / `atmosphere.coupled`).
12. **Placement** = collectible `timeline` bundle, hot-reloadable, with the Plan 5a cross-ALC unsubscribe discipline.
13. **Testing** = pure-C# unit tests for layout/colour/label mapping + windowed verify in the exported app.

## 4. Timeline data model

The timeline is a read-out of the composition model, addressed by the truth-stream id.

- **Lane = sphere (`domain`).** Geosphere, atmosphere, … Each lane is a track-group.
- **Track = layer (`M`, the model).** `geosphere.plate`, `geosphere.crust`, `atmosphere.bulk`, `atmosphere.coupled`. A layer's *generator* face binds to a truth stream via its `domain` (planet-stack-model §4), so **each track is one stream**. (Layer ids are the exact `LayerId` strings in `SphereRegimeScheduleDefaults` — verify there, don't guess.)
- **Section = regime.** A lane's sections come from that sphere's `SphereRegimeSchedule` (`RegimeAt(tick)`): geosphere `magma-ocean → stagnant-lid → mobile-plate`; atmosphere `primordial-steam → secondary-co2 → coupled-climate`.
- **Track address = the stream key.** `TruthStreamIdentity(VariantId, BranchId, LLevel, Domain, Model)` → `variant:branch:L{n}:domain:model` (engine `World.TruthStream/TruthStreamIdentity.cs`). On the timeline: `domain` → lane, `M` → track, `L` → a write-authority **badge** on the track (not a separate timeline). `R` (resolution/LOD) is a **view**, not in the key and not a timeline. (The app also carries the path form `/{Variant}/{Branch}/{Domain}/{Product}@{Tick}` — ref-projects only.)
- **One timeline per `(variant, branch)`.** Those two segments are the stream-key *prefix*; they **select** which timeline is shown — they are not events on it. The widget always renders exactly one. Switching them is the selector's job (§10).

## 5. Time & the odometer ladder

- The tick axis is **`CanonicalTick` (CT)**, the integer substrate. `UnitConverter.TicksPerMegaAnnum == 100_000`.
- Magnitudes are **never** shown as `Ma`/`Ga`. Labels render through `CanonicalDisplayFormatter`, which walks a scale profile and emits the largest ladder glyph ≥ 1 (`…jz, ka, kb…`). On `geosphere.plate.time.v1`: `1 ka CTU = 1 Ma = 100_000 CT`, every rung ×1000, so `kb = 1000 Ma`. Onset (`1e8 CT = 1000 Ma`) reads ≈ **`1 kb`** — the "ka→kb rollover at onset" seen in Plan 4.
- The AnimationPlayer tick-track is **linear in CT**; only the *labels* use the ladder. Scrubbing is linear; display is glyph-formatted. (Sources: `canonical-foundation.md`; `World.Shared/Quantities/OdometerLadder.cs`, `BaselineScaleProfiles.cs`, `CanonicalDisplayFormatter.cs`.)

## 6. Cross-sphere conditioning (Option A)

Per planet-stack-model §7 (locked): the planet is one coupled system; the master timeline **emerges from cross-sphere gates** — section boundaries are computed gate outputs, not hand-placed keyframes.

- **Proven gate:** atmosphere surface-hydration ≥ `0.99` → geosphere `mobile-plate` onset (`PlateOnsetTickFor`). Turning the outgassing knob slides onset.
- Forward-note gates (not wired): geosphere cool → hydrosphere oceans; oceans + CO₂ → biosphere.
- **Consequence:** because each lane reads its `SphereRegimeSchedule`, the timeline is **emergent-ready** — the day hydration-derived onset is wired, the geosphere boundary slides with no UI change. A conditioned regime is just a section whose boundary came from another sphere's field; no cross-track wiring, no second timeline.

## 7. Components & data flow

`Timeline.tscn` (in the `timeline` bundle) contains:

- **`AnimationPlayer`** — the master tick value-track (CT `0 → maxTick`).
- **`AnimationTree`** — idle/playing/scrub state machine.
- **Timeline UI** — sphere lanes, each with a regime-section row + layer track-rows + a draggable playhead bound to the player position.
- **`TimelineFace.cs`** — thin glue (this is the "not entirely C#" line: structure/sections/colours live in the `.tscn`; the script only wires). Builds lanes/sections from the schedules, binds the playhead, forwards play/scrub/seek.

Data flow:

- **Play** → AnimationTree "playing" → AnimationPlayer plays → tick-track advances → `TimelineFace` pushes the tick to `ITimelineController` → resident `GlobeView.SetTick` + `SetRegime`. Playhead follows the player.
- **Scrub** → drag playhead / click a section → `AnimationPlayer.Seek(pos)` (state → "scrub") → same push.
- **Labels/colours** → from the schedules + palette; active section/track highlighted at the current tick.

## 8. Colours

Doctrine, not decoration — lane = sphere, section = regime, track = layer.

- Geosphere regimes: `magma-ocean` = **amber** (molten), `stagnant-lid` = **gray** (slate), `mobile-plate` = **teal**. Atmosphere regimes (cool): `primordial-steam` / `secondary-co2` = **blue** (pre-onset), `coupled-climate` = **teal**.
- Per-sphere group accent: geosphere warm, atmosphere cool.
- Active (at the playhead) = full opacity; inactive = dim.

## 9. Controller seam & what's retired

- **Time source flips to the bundle.** Today the resident `RegimeTimelineTransport` owns playback and the bundle subscribes its `TickChanged`. Now the bundle's `AnimationPlayer` is the single time source: `ITimelineController` is adjusted so the bundle **pushes** the current tick to the resident `GlobeView` (`SetTick` + `SetRegime`) and reads schedules/`MaxTick`; Play/Pause/Seek act on the `AnimationPlayer`.
- **Retired:** `GlobeView`'s `HSlider`/`BuildScrubber`; the boom-hud `TimelineViewSource`/`TimelineModel`; `RegimeTimelineTransport`'s manual `_tickAccum` loop (its resident duties fold into the controller/adapter).
- **Hot-reload:** during a bundle unload, time pauses on the last tick (acceptable). Keep the Plan 5a discipline — anything in the bundle subscribing a resident event implements `IDisposable` and unsubscribes on `ShutdownAsync`, or it pins the collectible ALC.

## 10. Variant/branch selector (fast-follow)

`variant` and `branch` are the stream-key prefix; they select which timeline is rendered (git-checkout model). A **separate, small selector UI** (variant dropdown + branch picker) sets the active `(variant, branch)`; the one timeline widget re-renders. It is **not MVP** — today only `realistic : main` exists. Documented here so the timeline is built to read "the current `(variant, branch)`" from the start (one extra method on `ITimelineController`, or a tiny `IWorldSelector`).

## 11. Testing

- **Pure-C# unit tests** (no Godot) for: schedule → lane/section/track layout; palette mapping; ladder-label formatting (mirror the current `TimelineModel` tests).
- **Windowed verify** in the exported app — the only gate that exercises the Godot seam + hot-reload (renders, tracks the regime across onset, hot-reloads `timeline.pck` without restart).

## 12. Honest caveats

- **Onset is a configured constant today.** `PlateOnsetTickFor(forcing)` (hydration-derived) exists, but the live default uses the constant `PlateOnsetTick`. The timeline reads the schedule, so emergent onset is one engine-side wire-up away — but as shipped, the boundary won't move until that's wired.
- **Retiring the boom-hud face is deliberate.** It was recent Plan 5a work; it's retired because the flexbox boom-hud catalog cannot draw a continuous playhead — the `.tscn` solves it natively. The boom-hud view-in-bundle pattern itself remains valid for other live views.

## 13. Out of scope / fast-follows

- Variant/branch selector UI (§10).
- Real per-layer `AnimationPlayer` tracks (editor-keyframable) beyond the master tick track — MVP uses the master tick track + schedule-driven visual track-rows.
- Wiring hydration-derived onset (engine side).
- Render polish (boundary terrain, magma glow) — separate Plan 5.

---

> **Review trail.** Self-reviewed at authoring; citations independently verified 2026-06-23 by an external `agy` (Antigravity/Gemini) delegation — report at `.agent/logs/agy/spec-citation-findings.md`. That pass confirmed ~25 file/symbol citations and caught three errors since fixed: `geosphere.plate` (was plural), `atmosphere.bulk`/`atmosphere.coupled` (was `atmosphere.genesis`), and the cross-repo location of the anchor docs.
