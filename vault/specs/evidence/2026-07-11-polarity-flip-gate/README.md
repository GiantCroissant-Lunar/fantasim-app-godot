# Polarity-flip gate evidence — 2026-07-11 (evening session)

The bundle-maximalism polarity flip (spec §"Two systemic changes" item 1): `FantaSim.App.`
resident-by-default prefix removed from `shared-assembly-policy.json`; shared is now
14 enumerated `*.Contracts` exactMatches + the explicit resident floor.

## Decisions (user delegated to lead, adversarially audited by a fresh-context sonnet agent)

- `App.Common`, `App.Resource`, `App.Resource.Bundle.Seam` — PERMANENT floor (target-topology
  RESIDENT line; host ProjectReferences).
- `App.Ecs` — floor until phase 7; `App.NodeGraph`, `App.Ui.NodeGraph` — floor until phase 3.
  All six are structurally required while `complete-app.csproj` keeps the ProjectReferences:
  unsharing them would dual-copy into the world ALC (MessagePack type-split class).
- Contracts share = enumeration, NOT a suffix rule: the runtime `SharedAssemblyPolicy`
  (PluginArchi.Extensibility.Hosting) has no suffix concept; adding one is a cross-repo change.
- `DynamicData` prefix removed DELIBERATELY (audit falsified the "dead prefix" claim from
  07-08): only consumer is bundle-local App.World.FieldView, no resident copy ever existed —
  it now stages into world.pck (see staging-diff.txt), fixing a latent resolution gap.
- Dead-prefix removals verified: `ReactiveUI` (zero refs anywhere), `GodotSharp` (⊂ `Godot`),
  `FantaSim.App.World.` / `FantaSim.App.Command.` (all members covered post-flip).

## Gate results

- Staging behavior-neutrality: pre/post diff across ALL bundles = exactly one line
  (`world/DynamicData.dll` added). `stage_bundle.py --all --check-dual`: no dual copies.
- Fresh boot of the exported app with the flipped policy: 0 errors/exceptions, all bundles
  loaded, ingress up.
- Seek probe at t=150M: crust generation + presentation bind normal; the G34 chase-dedupe
  skip line fired (1 bind + skip).
- Reload round (`task bundle:install`): `old ALC collected` for ALL five collectible bundles
  (world, timeline ×2, activity, assist ×2, stage), `still pinned` count 0.
