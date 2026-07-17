# Focused Convergent-Underlap Inspection Implementation Plan

> **For agentic workers:** Implement this plan task-by-task. Prefer orchestrating bounded
> per-task agents (see the `orchestrate-before-implementing` rule + the `external-agent-delegation`
> skill); otherwise execute inline with a review checkpoint per task. Steps use checkbox
> (`- [ ]`) syntax for tracking.

**Goal:** Make the existing overriding-plate/down-going-plate relationship visually readable in
Replacement B0 by isolating the two complete generated plate bodies, hiding occluders, and
rigidly lifting only the overriding plate.

**Architecture:** Evolve the existing `render.exploded` request and callback path with one
`focusConvergent` flag. The presentation binder resolves the existing
`CrustVolumeState.TryFindConvergentUnderlapProof`, extracts only those two complete plates through
the existing cap/solid builders, omits the core, applies the existing rigid translation to the
overriding plate only, and rotates their common presentation root to place the proven boundary in
front of the existing orbit camera.

**Tech Stack:** Godot .NET SDK 4.7.0, .NET 8, C#, UnifyMaths 0.1.5, the existing FantaSim render
command/presentation seams, Taskfile, and UnifyBuild.

## Global Constraints

- `CrustVolumeState` remains the only plate-volume and underlap authority.
- Do not add a new geological type, request duplicate, plate DTO, slab proxy, tongue, ribbon,
  shelf, crop, or renderer-authored boundary mesh.
- Factor zero must preserve the exact generated relationship and digest.
- A nonzero focused factor moves the complete overriding plate only; it cannot deform either plate.
- The assembled and whole-globe exploded projections retain their existing behavior.
- Cells and chunks remain invisible and never become exploded units.
- Hide the core and unrelated plates in focused mode instead of recoloring them.
- Do not tune color or literal crust-to-core scale.
- Do not enter Slice C or expand collision/divergence/transform/LOD work.
- Per the user's instruction, add no new tests. Only update an existing test fake if required for
  compilation.
- Product acceptance is based on fresh exported-window screenshots, not build output, tests, logs,
  or the structural proof alone.
- Use `dotnet unify-build` through the repository Taskfile for the outer Godot export.
- Godot 4.7 transform authority:
  `https://docs.godotengine.org/en/4.7/tutorials/3d/using_transforms.html` and
  `https://docs.godotengine.org/en/4.7/classes/class_basis.html`.

---

### Task 1: Evolve the existing exploded ingress without duplicating request types

**Files:**

- Modify: `project/plugins/App.Render/ExplodedRequest.cs:6-58`
- Modify: `project/plugins/App.Render.Seam/HostComposition/RenderComposition.cs:146-188`
- Modify: `project/plugins/App.Render.Seam/HostComposition/RenderComposition.cs:305-394`
- Modify: `project/contracts/App.Presentation/IPlanetPresentation.cs:18-19`
- Modify: `project/tests/App.Presentation.Tests/PresentationPluginTests.cs:20`

**Interfaces:**

- Consumes: existing `ExplodedRequest`, `ExplodedRequestParser`,
  `IRenderCompositionHandle.SetExplodedTarget`, and `IPlanetPresentation.UpdateExploded`.
- Produces:
  `ExplodedRequest(double Factor, bool FocusConvergent = false)`,
  `Action<double, bool>` exploded target registration, and
  `IPlanetPresentation.UpdateExploded(double factor, bool focusConvergent)`.

- [ ] **Step 1: Evolve `ExplodedRequest` in place**

Change the primary constructor and parser without adding a peer request/options type:

```csharp
public readonly record struct ExplodedRequest(
    double Factor,
    bool FocusConvergent = false)
{
    public bool IsAssembled => Factor <= 0.0;
}
```

In `Parse`, preserve the existing factor behavior and add:

```csharp
bool focusConvergent = false;

if (payload["factor"] is { } fNode)
    factor = ReadDouble(fNode, "factor");

if (payload["focusConvergent"] is { } focusNode)
{
    try
    {
        focusConvergent = focusNode.GetValue<bool>();
    }
    catch (Exception ex) when (ex is InvalidOperationException or FormatException)
    {
        throw new ArgumentException(
            "render.exploded 'focusConvergent' must be a JSON boolean.",
            ex);
    }
}
```

Return:

```csharp
return new ExplodedRequest(factor, focusConvergent);
```

Null/empty payloads continue returning `new ExplodedRequest(0.0)`.

- [ ] **Step 2: Forward the focus flag through the existing render composition**

Update the command description to document both payload shapes:

```text
{"factor":N} keeps the whole-globe radial explosion.
{"factor":N,"focusConvergent":true} isolates the proven convergent pair;
factor 0 is exact and factor > 0 lifts only the overriding complete plate.
```

Invoke:

```csharp
target(req.Factor, req.FocusConvergent);
```

Log and return the focus flag:

```csharp
log.LogInformation(
    "render.exploded: factor={Factor} focusConvergent={FocusConvergent}",
    req.Factor,
    req.FocusConvergent);

return Task.FromResult<string?>(JsonSerializer.Serialize(new
{
    ok = true,
    factor = req.Factor,
    assembled = req.IsAssembled,
    focusConvergent = req.FocusConvergent,
}));
```

Evolve the existing callback surfaces:

```csharp
void SetExplodedTarget(Action<double, bool>? target);
```

```csharp
public void SetExplodedTarget(Action<double, bool>? target)
    => _explodedTarget.Target = target;
```

```csharp
internal sealed class ExplodedTargetHolder
{
    public Action<double, bool>? Target { get; set; }
}
```

- [ ] **Step 3: Evolve the presentation contract and its existing compile fake**

Change the contract:

```csharp
/// <summary>
/// M-B exploded solid crust. Factor is in [0,1]; focused mode isolates the proven
/// convergent pair and uses factor as the overriding-plate reveal translation.
/// </summary>
void UpdateExploded(double factor, bool focusConvergent);
```

Update only the existing fake required by the interface change:

```csharp
public void UpdateExploded(double factor, bool focusConvergent) { }
```

Do not add or modify test cases.

- [ ] **Step 4: Compile the affected signature path**

Run:

```bash
dotnet tool restore
dotnet build project/hosts/complete-app/complete-app.csproj
```

Expected: exit code 0 with no interface/delegate mismatch.

- [ ] **Step 5: Commit the ingress checkpoint**

```bash
git add \
  project/plugins/App.Render/ExplodedRequest.cs \
  project/plugins/App.Render.Seam/HostComposition/RenderComposition.cs \
  project/contracts/App.Presentation/IPlanetPresentation.cs \
  project/tests/App.Presentation.Tests/PresentationPluginTests.cs
git commit -m "feat(render): add convergent exploded focus"
```

### Task 2: Assemble the focused scene from the two canonical complete plate solids

**Files:**

- Modify: `project/plugins/App.Presentation/PlanetPresentationBinder.CutawayExploded.cs:22-27`
- Modify: `project/plugins/App.Presentation/PlanetPresentationBinder.CutawayExploded.cs:54-67`
- Modify: `project/plugins/App.Presentation/PlanetPresentationBinder.CutawayExploded.cs:142-236`

**Interfaces:**

- Consumes:
  `CrustVolumeState.TryFindConvergentUnderlapProof`,
  `CrustVolumeState.BoundaryArcs`,
  `GlobePlateSurfaces.BuildVolumeSurfaces`,
  `PlateSolidBuilder.Build`, `PlateSolidBuilder.ApplyExplodedFactor`,
  `PlateSolidBuilder.DefaultMaxOffset`, `PlateCap`, `PlateSolid`, and
  `PlateSolidCentroid`.
- Produces: focused scene assembly behind
  `PlanetPresentationBinder.UpdateExploded(double factor, bool focusConvergent)`.

- [ ] **Step 1: Store focused presentation state**

Add beside `_explodedFactor`:

```csharp
private bool _explodedFocusConvergent;
```

Evolve the entry method:

```csharp
public void UpdateExploded(double factor, bool focusConvergent)
{
    if (_disposed)
        return;

    _explodedActive = true;
    _explodedFactor = factor;
    _explodedFocusConvergent = focusConvergent;
    RebuildExplodedCrust();
    ApplyTimelineTick(_timeline.Tick);
}
```

- [ ] **Step 2: Preserve the existing whole-globe path and branch only after canonical extraction**

In `BuildExplodedSolidCrust`, keep the current document/snapshot/volume checks, centroid
calculation, and `BuildSlabTopCaps` call. After resolving `factor`, branch:

```csharp
if (_explodedFocusConvergent)
{
    BuildFocusedConvergentCrust(
        root,
        volume,
        slabCaps,
        centroids,
        factor,
        slabPerPlateVertexColors);
    return root;
}
```

Leave the existing all-solid build, global `ApplyExplodedFactor`, core creation, and all-plate
`AddSlabMeshInstances` call unchanged below the branch.

- [ ] **Step 3: Resolve the target exclusively from the existing state proof**

Add this binder-private method:

```csharp
private void BuildFocusedConvergentCrust(
    Node3D root,
    CrustVolumeState volume,
    IReadOnlyList<PlateCap> slabCaps,
    IReadOnlyList<PlateSolidCentroid> centroids,
    double factor,
    IReadOnlyDictionary<int, RampColor[]> slabPerPlateVertexColors)
{
    if (!volume.TryFindConvergentUnderlapProof(out var proof))
    {
        _log.LogWarning(
            "Focused convergent crust skipped: digest={Digest} has no convergent underlap proof.",
            volume.Digest);
        return;
    }

    var focusedCaps = slabCaps
        .Where(cap =>
            cap.PlateId == proof.OverridingPlateId
            || cap.PlateId == proof.SubductingPlateId)
        .ToArray();
    var focusedCentroids = centroids
        .Where(centroid =>
            centroid.PlateId == proof.OverridingPlateId
            || centroid.PlateId == proof.SubductingPlateId)
        .ToArray();

    if (focusedCaps.Length != 2 || focusedCentroids.Length != 2)
    {
        _log.LogWarning(
            "Focused convergent crust skipped: digest={Digest} arc={ArcIndex} expected two complete plates; caps={CapCount} centroids={CentroidCount}.",
            volume.Digest,
            proof.BoundaryArcIndex,
            focusedCaps.Length,
            focusedCentroids.Length);
        return;
    }

    var assembledSolids = PlateSolidBuilder.Build(focusedCaps, volume);
    var displayedSolids = assembledSolids.ToArray();
    int overridingIndex = Array.FindIndex(
        displayedSolids,
        solid => solid.PlateId == proof.OverridingPlateId);
    var overridingCentroid = focusedCentroids
        .Single(centroid => centroid.PlateId == proof.OverridingPlateId);

    displayedSolids[overridingIndex] = PlateSolidBuilder.ApplyExplodedFactor(
        new[] { displayedSolids[overridingIndex] },
        new[] { overridingCentroid },
        factor)[0];

    var offsetByPlate = new Dictionary<int, double>
    {
        [proof.OverridingPlateId] = factor * PlateSolidBuilder.DefaultMaxOffset,
        [proof.SubductingPlateId] = 0.0,
    };

    AddSlabMeshInstances(
        root,
        focusedCaps,
        displayedSolids,
        focusedCentroids,
        offsetMag: 0.0,
        slabPerPlateVertexColors: slabPerPlateVertexColors,
        offsetMagnitudeByPlate: offsetByPlate);
    OrientFocusedConvergentRoot(root, volume, proof.BoundaryArcIndex);

    root.SetMeta("crustVolumeDigest", volume.Digest);
    root.SetMeta("focusBoundaryArcIndex", proof.BoundaryArcIndex);
    root.SetMeta("overridingPlateId", proof.OverridingPlateId);
    root.SetMeta("subductingPlateId", proof.SubductingPlateId);
    _log.LogInformation(
        "Focused convergent crust mounted: digest={Digest}, arc={ArcIndex}, overridingPlate={OverridingPlateId}, downGoingPlate={SubductingPlateId}, factor={Factor:R}, plates=2.",
        volume.Digest,
        proof.BoundaryArcIndex,
        proof.OverridingPlateId,
        proof.SubductingPlateId,
        factor);
}
```

This method does not catch extraction errors. Invalid state/mesh identity remains a construction
failure instead of silently falling back to fabricated geometry.

- [ ] **Step 4: Give top meshes the same per-plate rigid offset as their solids**

Add an optional final parameter to `AddSlabMeshInstances`:

```csharp
IReadOnlyDictionary<int, double>? offsetMagnitudeByPlate = null
```

Before `BuildExplodedTopDto`, select the offset:

```csharp
double plateOffsetMag = offsetMag;
if (offsetMagnitudeByPlate is not null
    && offsetMagnitudeByPlate.TryGetValue(cap.PlateId, out double focusedOffset))
{
    plateOffsetMag = focusedOffset;
}

var topDto = BuildExplodedTopDto(
    cap,
    centroid,
    plateOffsetMag,
    slabPerPlateVertexColors);
```

Existing callers omit the optional dictionary, so the whole-globe and slab-assembly behavior
remains unchanged.

- [ ] **Step 5: Orient both plates together using the canonical arc**

Add:

```csharp
private static void OrientFocusedConvergentRoot(
    Node3D root,
    CrustVolumeState volume,
    int boundaryArcIndex)
{
    var points = volume.BoundaryArcs[boundaryArcIndex].Points;
    int middle = points.Count / 2;
    int previous = Math.Max(0, middle - 1);
    int next = Math.Min(points.Count - 1, middle + 1);

    var middlePoint = points[middle];
    var outward = new Vector3(
        (float)middlePoint.X,
        (float)middlePoint.Y,
        (float)middlePoint.Z).Normalized();
    var previousPoint = points[previous];
    var nextPoint = points[next];
    var rawTangent = new Vector3(
        (float)(nextPoint.X - previousPoint.X),
        (float)(nextPoint.Y - previousPoint.Y),
        (float)(nextPoint.Z - previousPoint.Z));
    var tangent = (
        rawTangent - outward * rawTangent.Dot(outward)
    ).Normalized();
    var across = outward.Cross(tangent).Normalized();

    if (!outward.IsFinite() || !tangent.IsFinite() || !across.IsFinite())
        return;

    // A Node3D basis is relative to its parent; applying one basis to this common root rotates
    // both complete plates identically and preserves their relative generated relationship.
    // Source: https://docs.godotengine.org/en/4.7/tutorials/3d/using_transforms.html
    var sourceFrame = new Basis(tangent, across, outward).Orthonormalized();
    root.Basis = sourceFrame.Transposed();
}
```

The source frame maps the boundary tangent to world +X and the boundary outward direction to
world +Z, placing the proven boundary in front of a zero-yaw orbit camera. The rotation is on the
common root, never on an individual plate.

- [ ] **Step 6: Compile the focused presentation**

Run:

```bash
dotnet build project/plugins/App.Presentation/App.Presentation.csproj
dotnet build project/FantaSim.sln
```

Expected: exit code 0. Do not run or add tests.

- [ ] **Step 7: Audit duplicate authority before committing**

Run:

```bash
rg -n \
  'class FocusedPlate|record FocusedPlate|CrustVolumeState2|InspectionPlate|render\\.exploded\\.focus' \
  project
git diff --check
```

Expected: `rg` returns no matches and `git diff --check` exits 0.

- [ ] **Step 8: Commit the presentation checkpoint**

```bash
git add project/plugins/App.Presentation/PlanetPresentationBinder.CutawayExploded.cs
git commit -m "feat(presentation): focus convergent underlap pair"
```

### Task 3: Export and judge the real focused view

**Files:**

- Create:
  `vault/specs/evidence/2026-07-17-focused-convergent-underlap-inspection/assembled.png`
- Create:
  `vault/specs/evidence/2026-07-17-focused-convergent-underlap-inspection/focused-exact.png`
- Create:
  `vault/specs/evidence/2026-07-17-focused-convergent-underlap-inspection/focused-reveal.png`
- Create:
  `vault/specs/evidence/2026-07-17-focused-convergent-underlap-inspection/README.md`

**Interfaces:**

- Consumes: the real exported `complete-app`, remote `render.exploded`, `camera.orbit`, and
  `render.screenshot` commands.
- Produces: same-digest assembled/focused exact/focused reveal visual evidence and an honest pass
  or fail against the approved specification.

- [ ] **Step 1: Build through the repository workflow**

Run:

```bash
dotnet tool restore
task build:godot:desktop
task bundle:world
task bundle:install
```

Expected:

- all commands exit 0;
- the exported app exists under
  `build/_artifacts/<ARTIFACTS_VERSION>/godot/osx/complete-app.app`;
- `world.pck` and `common.pck` reflect the current source.

- [ ] **Step 2: Bind runtime verification to the exact checkout**

Record:

```bash
git rev-parse HEAD
task version:artifacts
```

Resolve and record the absolute `.app`, executable, bundle identifier, PID, and log path. Use:

```bash
lsof -a -p "$PID" -d txt -Fn
```

to prove that the PID belongs to this checkout's exported executable. Do not stop any
pre-existing user-owned process with the same display name.

Because this change touches resident host/render-contract code, launch a new exact exported app
process rather than claiming world-bundle hot reload is sufficient. Keep that exact process open
through all captures.

- [ ] **Step 3: Capture the assembled baseline**

Launch with:

```bash
FANTASIM_REMOTE_ENABLED=1 \
FANTASIM_NEUTRAL_CRUST_GEOMETRY=1 \
"<absolute-executable>" \
> "<absolute-log-path>" 2>&1
```

After the world mounts, invoke `camera.orbit` with:

```json
{"yawDeg":0,"pitchDeg":-20,"distance":6}
```

Capture:

```text
vault/specs/evidence/2026-07-17-focused-convergent-underlap-inspection/assembled.png
```

The assembled baseline must remain a closed globe with buried crust hidden.

- [ ] **Step 4: Capture the factor-zero focused pair**

Invoke:

```json
{"command":"render.exploded","payloadJson":"{\"factor\":0.0,\"focusConvergent\":true}"}
```

Keep the camera at zero yaw, -20 pitch, distance 6 after the common-root orientation. Capture:

```text
vault/specs/evidence/2026-07-17-focused-convergent-underlap-inspection/focused-exact.png
```

Required visible facts: exactly two complete curved plates, no core, no unrelated plates, and the
generated contact restored at factor zero.

- [ ] **Step 5: Capture the focused reveal**

Invoke:

```json
{"command":"render.exploded","payloadJson":"{\"factor\":0.15,\"focusConvergent\":true}"}
```

Capture:

```text
vault/specs/evidence/2026-07-17-focused-convergent-underlap-inspection/focused-reveal.png
```

If `0.15` leaves the relationship ambiguous, try `0.25` and then `0.35`, retaining the lowest
factor that visibly exposes the attached descending plate. Camera tuning is limited to yaw
`-25`, `0`, or `25`; pitch `-35`, `-20`, or `0`; and distance `5`, `6`, or `7`. Do not change
generation, thickness, or color to force a pass.

- [ ] **Step 6: Inspect fresh pixels and record the verdict**

Open all three PNGs at full resolution. The focused reveal passes only if:

- the overriding plate is visibly above the down-going plate;
- the down-going plate visibly continues beneath it as part of one complete curved plate;
- the reveal is caused by moving the complete overriding body;
- there is no core, unrelated plate, cell/chunk piece, crop, tongue, ribbon, shelf, or proxy mesh;
- factor zero shows the generated contact; and
- the app log reports the same `CrustVolumeState` digest and the established boundary arc/plate ids.

Write `README.md` with:

- checkout HEAD, executable, PID, log path, seed/tick/digest;
- exact command payloads and camera values;
- source paths and state-to-scene data flow;
- ESTABLISHED, DISPROVEN, and visual PASS/FAIL conclusions;
- an explicit note that no new tests were added.

If the pixels remain ambiguous, record FAIL and the concrete occlusion/framing failure. Do not
promote logs or structural proof into a visual pass.

- [ ] **Step 7: Commit evidence**

```bash
git add vault/specs/evidence/2026-07-17-focused-convergent-underlap-inspection
git commit -m "docs(evidence): verify focused underlap inspection"
```

## Plan self-review

- Spec coverage: every approved requirement is owned by Task 1, Task 2, or the Task 3 pixel gate.
- Duplicate-type guard: the only request type remains `ExplodedRequest`; the only plate-volume
  authority remains `CrustVolumeState`; focused selection is binder-private scene composition.
- Type consistency:
  `ExplodedRequest.FocusConvergent` maps to `Action<double, bool>`, then to
  `IPlanetPresentation.UpdateExploded(double, bool)`, then to `_explodedFocusConvergent`.
- No test expansion: the plan updates one existing fake signature and runs compilation/export
  only.
- No placeholders: every edit, command payload, initial factor, bounded tuning value, camera
  candidate, evidence path, and commit is explicit.
- Runtime safety: resident changes force an exact-path relaunch; other colliding processes remain
  untouched.
