---
source: project/plugins/App.World.Composition/WorldStreamVocabulary.cs, project/tests/App.World.Tests/StreamVocabularyGuardTests.cs, project/plugins/App.World.Composition/OnsetRoster.cs, project/plugins/App.World/History/RotationImportCoordinator.cs, project/plugins/App.World/WorldPlugin.cs, project/plugins/App.World/Services/WorldHistoryCoordinator.cs, project/hosts/complete-app/config/collectible-bundles.json, project/hosts/complete-app/config/shared-assembly-policy.json (authored 2026-07-14)
source-status: current-code documentation
distilled: 2026-07-14
divergence: >
  Does not restate L×R×M or Variant×Branch doctrine (hub lrm-axis-model.md and
  variant-and-branch.md own that) — cites and surfaces the OWED Domain-vs-M
  decision rather than resolving it.
---

# Stream Identity Vocabulary (App-Side L2 Minting Guard)

`WorldStreamVocabulary` is the app-side enforcement point for hub L×R×M
stream-identity doctrine: the sole production factory for world-scoped
`TruthStreamIdentity`/`LayerTrackStreamId` values, backed by a source-scan
guard test.

## Doctrine (hub authority — cite, do not restate)

- **L ladder + world = L2**: `fantasim-hub/vault/architecture/lrm-axis-model.md`
  "Ladder numbering" — L2 is world/planetary write authority; plate topology
  is the worked L2 example.
- **Stream identity tuple** `(VariantId, BranchId, L, Domain, M)`:
  `lrm-axis-model.md` "Stream Identity" + `variant-and-branch.md`
  "Authoritative Identity Tuple."
- **Domain-vs-M rule**: `lrm-axis-model.md` "Domain Namespace vs M" — `Domain`
  is the hierarchical truth-slice namespace; `M` is the governing-model
  selector (`M0` default); a subsystem/artifact kind must not be stuffed into
  `M` to disambiguate streams.
- **OWED decision (2026-07-14)**: `lrm-axis-model.md` "Restoration status"
  flags that the shipped vocabulary violates the Domain-vs-M rule for every
  factory except `PlateTopologyTruth`; the open decision — migrate shipped
  streams to dotted-Domain+`M0`, or amend doctrine to match the shipped
  bare-Domain convention — is unresolved and cross-referenced from hub
  `planet-stack-model.md` §2 "Domain-vocabulary reality" note.
- **Variant/branch value convention**: `variant-and-branch.md` — Variant
  selects lawsets, Branch selects data/timeline authority; both orthogonal to
  Domain and M.

## Built

### The class

Static factory, `project/plugins/App.World.Composition/WorldStreamVocabulary.cs`
(assembly `FantaSim.App.World.Composition`, `ServiceArchiTier=T3` per its
csproj). XML doc: "The ONLY production minting point for world-scoped
five-axis stream identities." `WorldLLevel = 2` (const).

### The seven factories

| Factory | Returns | Identity | Note |
|---------|---------|----------|------|
| `Plates(worldId, branchId)` | `TruthStreamIdentity` | `{world}:{branch}:L2:geosphere:plates` | validates `worldId` |
| `PlateTopologyTruth()` | `TruthStreamIdentity` | `default:main:L2:geo.plates.topology:M0` | the **one** doctrine-correct dotted-Domain+`M0` form; XML doc warns it must track the engine's `PlateTopologyEmitter.EmitRoster` call path in lockstep; consumed by `OnsetRoster.cs` |
| `ImportsControl(worldId, branchId)` | `TruthStreamIdentity` | `{world}:{branch}:L2:world:imports` | validates `worldId`; consumed by `RotationImportCoordinator` |
| `RotationSelection()` | `TruthStreamIdentity` | `app:main:L2:world:rotation-bindings` | consumed by `WorldHistoryCoordinator` |
| `Generation()` | `TruthStreamIdentity` | `app:main:L2:world:default` | consumed by `WorldHistoryCoordinator` |
| `TrackDefault()` | `LayerTrackStreamId` | variation=`default`, branch=`main`, L=`L2`, domain=`world`, model=`default` | consumed by `TrackPipelineNodeCatalog` (two sites) |
| `TruthEventsTrack()` | `LayerTrackStreamId` | variation=`app`, branch=`main`, L=`L2`, domain=`world`, model=`default` | consumed by `WorldPlugin.cs`; mirrors `Generation()` as a track-layer view |

### `ValidateWorldId` — the ingress-leak guard

Private helper called by `Plates` and `ImportsControl` (the two factories
taking a caller-supplied `worldId`). Rejects null/whitespace and anything
`IPAddress.TryParse` accepts. XML doc: "The HTTP ingress path can leak caller
IPs into WorldId; this guard makes that loud at mint time until the ingress
mapping is redesigned." Test coverage: `Plates_rejects_empty_worldId`
(`""`, `"   "`), `Plates_rejects_ip_address_worldId`
(`"127.0.0.1"`, `"::1"`, `"10.0.0.1"`).

### Why the class lives in the plugin, not T1 contracts

`WorldStreamVocabulary`'s own assembly is itself collectible:
`collectible-bundles.json` lists `FantaSim.App.World.Composition` in the
`world` bundle's `assemblyNames`, alongside `FantaSim.App.World`,
`FantaSim.App.World.FieldView`, `FantaSim.App.Presentation` — all in one
collectible ALC. Its two return types have different residency:
`LayerTrackStreamId` lives in `FantaSim.App.World.Contracts` (T1,
shared-resident per `shared-assembly-policy.json` `exactMatches`);
`TruthStreamIdentity` lives in the engine package
`GiantCroissant.FantaSim.World.TruthStream.Contracts`, absent from
`shared-assembly-policy.json` entirely (checked directly) and therefore
bundle-local. Its only two consumers in this repo — `App.World` and
`App.World.Composition` (csproj-confirmed) — are both members of that same
`world` bundle, so every mint/consume site for both types shares one ALC and
never crosses the resident/collectible boundary. That is what makes it safe
to keep the vocabulary in a T3 plugin rather than a T1 contracts assembly;
promotion would only be needed if a resident (non-bundle) caller had to mint
or hold one of these identities, which none does today.

### The guard test

`StreamVocabularyGuardTests.cs` (`project/tests/App.World.Tests/`)
source-scans `project/plugins/App.World` and `project/plugins/App.World.Composition`:

1. **Literal scan** — rejects `"new TruthStreamIdentity("` / `"new LayerTrackStreamId("`
   anywhere outside two allowed files: `WorldStreamVocabulary.cs` itself and
   `RotationSourceSelectionCodec.cs` (its one construction sits inside
   `DecodeImported`, a deserialization path, not a minting point).
2. **2026-07-14 hardening** — a compiled regex additionally catches C#
   target-typed `new(...)` on a declared-type field/property/local (e.g.
   `TruthStreamIdentity Foo = new(...)`), added the day
   `OnsetRoster.PlateTopologyStreamIdentity` was found bypassing the literal
   scan that way. The fix routed `OnsetRoster` through
   `WorldStreamVocabulary.PlateTopologyTruth()` (app commit `1a83dc1`, per
   `lrm-axis-model.md` "Restoration status").
3. **Per-factory correctness assertions** — `LLevel == 2` and the exact
   `ToStreamKey()`/field values for all seven factories above, plus the two
   `ValidateWorldId` rejection cases.

Result: 14/14 green (count per `lrm-axis-model.md` "Restoration status" — this
doc does not independently re-run the suite).

## Not built / open

- **Domain-vs-M migrate-vs-amend decision** — see Doctrine above; OWED at user
  level, unresolved as of 2026-07-14.
- **Variant axis not plumbed** — all seven factories hardcode a world-identity
  value (`"default"`/`"app"`) in the `VariantId` slot, not a lawset Variant
  (`science`/`wuxing`/`high-magic`) per `variant-and-branch.md`. The axis
  itself is absent app-side (`variant-and-branch.md` "Restoration status":
  "Variant side still absent"); staged behind the variant-recipe slice.
- **Guard coverage is two directories only** — `App.World` and
  `App.World.Composition`. It does not scan the engine repo's own
  `TruthStreamIdentity` construction sites (`RotationImportPayloadCodec.cs`,
  `MessagePackTruthEventSerializer.cs` DTOs) or any future app plugin outside
  the scanned pair.
