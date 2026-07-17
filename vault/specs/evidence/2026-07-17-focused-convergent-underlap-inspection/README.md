# Focused convergent underlap inspection evidence

Date: 2026-07-17

## Exported artifact

- Source commit: `c6d23ac681a872d359fc694309d486271f295775`
- Artifact version: `0.1.2`
- Bundle identifier: `com.giantcroissant.fantasim`
- Executable:
  `build/_artifacts/0.1.2/godot/osx/complete-app.app/Contents/MacOS/complete-app`
- Focus verification PID: `37412`
- Assembled verification PID: `39135`
- Remote health at capture: `{"ok":true,"commands":19}`

`lsof -a -p 37412 -d txt -Fn` resolved the live process text image to the
executable above. Both exported verification processes remain open.

## Build and reload gate

The required repository path completed successfully:

```text
dotnet tool restore
task build:godot:desktop
task bundle:world
task bundle:install
```

The already-running verification process observed the installed `world.pck`,
unloaded the prior world bundle, loaded the replacement, and logged
`Hot-reload: old ALC collected for bundle world`.

No tests were added or run for this slice, per the explicit session direction.
The solution was compiled by the build task, the Godot macOS export succeeded,
and the result was verified in the exported windowed app.

## Canonical generated state

Both runs advanced to tick `105000000`. The assembled and focused logs record
the same generated state:

- Crust volume algorithm: `crust-volume.v2`
- Digest:
  `5726f3e842227a4118a58d4676d82490d5fd166e96eb84cdfc070e68ab23126e`
- Cells: `5120`
- Plates: `10`
- Boundary arcs: `346`
- Convergent arcs: `38`
- Surface features:
  `Mountain=69`, `VolcanicArc=77`, `Trench=69`, `Ridge=76`, `Fault=632`
- Amplification: `5096.8x`

The convergent underlap proof selects boundary arc `19`, overriding plate `7`,
and down-going plate `2`. One ray intersects the overriding body first and the
separate down-going body second:

```text
overridingInterval=[0.7243793146256793,0.7993426871584757]
downGoingInterval=[0.812291713087012,0.8216343078708603]
```

That ordering is the structural proof that this is not an elevation-only or
thickness-array representation.

## Captures

- `assembled-production.png` — normal assembled planet. Buried underlap remains
  hidden; generated surface relief remains visible.
- `focused-exact-production.png` — only the two complete selected plate bodies,
  factor `0`, preserving their generated contact/underlap relationship.
- `focused-reveal-production.png` — the same digest, arc, and plate IDs, factor
  `0.15`; only the complete overriding plate is translated so the down-going
  sidewall/volume can be inspected.
- `focused-exact-p35.png` and `focused-reveal-p35.png` — neutral geometry
  counterparts used to check that the reading does not depend on terrain color.

The focused log explicitly records `plates=2` for both factor values and the
same digest, arc, overriding ID, and down-going ID. The implementation reuses
`CrustVolumeState`, `PlateCap`, `PlateSolid`, and `PlateSolidCentroid`; no
parallel plate/crust domain type was introduced.

## Logs

- `run.log` — production-material focused run and captures.
- `assembled-run.log` — production-material assembled run and capture.

## Visual conclusion

The implementation gate passes structurally: the normal globe hides buried
material, focus mode isolates two complete plates rather than cells/tiles,
factor zero preserves their exact generated relationship, and a small reveal
separates only the overriding plate. The captures intentionally retain the
application timeline HUD; final aesthetic acceptance remains a human visual
judgment, not a claim inferred from compilation.
