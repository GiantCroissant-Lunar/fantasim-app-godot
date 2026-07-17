# Spherical plate-material volume A0/B0 evidence

Repository: `/Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot`
HEAD: `f2aa3ae3c593a709630118a11cc5c787336aabef`
App: `/Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/build/_artifacts/0.1.2/godot/osx/complete-app.app`
Executable: `/Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot/build/_artifacts/0.1.2/godot/osx/complete-app.app/Contents/MacOS/complete-app`
Bundle identifier: `com.giantcroissant.fantasim`
PID: `6092`
Log: `final-verified-run.log`
State digest: `2b075e133d105f8f39fda489ca087f859394e95bf14e146a934fb55f83a3bc57`
Material gate: `FANTASIM_NEUTRAL_CRUST_GEOMETRY=1`

The verified process was foreground-bound to PID 6092 before both required screenshots. `lsof`
reported the exact executable above. A separate user-owned process, PID 7144, was not changed.

## A0 structural result

ESTABLISHED:

- The production state logged an overriding plate-7 interval
  `[0.7170115911644652, 0.7955045434614672]` followed by a distinct down-going plate-2 interval
  `[0.8072680995185839, 0.8158645911818584]` on one ray.
- The down-going interval belongs to cell 300, which owns the convergent arc-8 edge used by the
  material deformation.
- Both intervals existed before either evidence projection mounted.
- The state algorithm was `crust-volume.v2`; it contained 5,120 material cells and 349 boundary
  arcs.
- The normal outer-envelope mount and factor-0/factor-1 exploded mounts reused the same state
  digest.
- The final material state passed wedge orientation, shared-face, contact, and overlap validation.

DISPROVEN:

- The first unrestricted corner-wise deformation inverted or overlapped wedges near boundary
  junctions. The final construction resolves a shared arc per cell corner and backs deformation
  down through the canonical validator rather than accepting the invalid candidate.
- A direct proof-ray camera is not a usable visual proof at present: the near-polar orbit magnifies
  and clips the exploded shell.

## B0 paired visual result

Assembled: `assembled.png`
Exploded: `exploded.png`

Supporting failed proof angles:

- `exploded-underlap-radial.png`
- `exploded-underlap-oblique-a.png`
- `exploded-underlap-oblique-b.png`

ESTABLISHED:

- `assembled.png` visibly contains one spherical outer globe with no exposed buried plate, cell
  grid, chunk grid, or independently separated tile.
- Broad, strongly amplified grooves, scarps, and raised boundary forms are visible in geometry.
- `exploded.png` visibly contains curved shell pieces with light outer faces and dark
  bottom/side geometry around a smaller spherical interior.
- The exploded unit is a whole plate: no cell or chunk explodes independently.

DISPROVEN:

- The assembled relief is still dominated by coarse faceting and very dark troughs. Some troughs
  read as open cracks even though the outer-envelope mesh is structurally closed.
- Mountain systems, individual peaks, and a volcanic cone chain are not yet unambiguously readable
  at globe distance.
- The exploded overview does not reveal a readable overriding/down-going relationship or a
  continuous attached descending plate edge.
- The proof-ray and two oblique captures also fail that visual requirement: unrelated plates and
  the interior occlude or merge with the target geometry, while near-polar framing clips the shell.
- Therefore B0 does **not** pass the user-reference comparison. The structural ray proof cannot
  substitute for the missing visible underlap.

## Authority audit

- `CrustVolumeState` remained the only geological plate-volume type. No
  `CrustVolumeState2`, `PlateVolume`, `CrustIsosurface`, `MaterialWedge`, or geological `RayHit`
  type was added.
- Existing `ValidateMaterialWedges` method names and the unrelated tunnel
  `TunnelRayHitMapper` are not peer geological authorities.
- Both binder paths use `PlateSolidBuilder.Build(caps, volume)`.
- The default binder paths have no radial extrusion, joint gap, or slab-joint mechanic caller.
- No whole-planet dense Cartesian three-dimensional allocation was added. Audit matches were
  existing image pixel buffers and local vector-normal arithmetic.

## Verification

- Authoritative command: `task build:godot:desktop && task bundles && task bundle:install`
- Result: succeeded with zero build errors.
- New tests: none, by explicit user direction.
- Exported app left open on the factor-1 exploded view for inspection.

## User verdict

Pending. A0 is structurally established; B0 is presented as a visual failure for discussion.
