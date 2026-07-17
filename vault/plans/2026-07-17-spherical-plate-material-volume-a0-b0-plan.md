# Spherical Plate-Material Volume A0/B0 Implementation Plan

> **For agentic workers:** Implement this plan task-by-task. Prefer orchestrating bounded
> per-task agents (see the `orchestrate-before-implementing` rule + the
> `external-agent-delegation` skill); otherwise execute inline with a review checkpoint per task.
> Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the radial crust substitute with one compact, non-radially deformed
`CrustVolumeState`, then render a closed assembled globe and an intact whole-plate exploded globe
from that same state identity.

**Architecture:** Each triangular globe cell is a material wedge whose three outer and three inner
control points are stored in `CrustVolumeState`. A fixed three-tetrahedron partition over a stable
lexicographic corner order is the exact material mapping and occupancy definition, so mapping and
ray queries cannot disagree and adjacent cells choose the same shared-side diagonal. Adjacent
wedges on one plate share welded control points, forming one continuous curved plate body. At a
convergent contact the shared outer hinge controls stay fixed and exactly coincident across the two
plates, while successively interior controls of the down-going plate bend inward and tangentially
beneath the overriding plate. This creates attached underlap without translating or opening the
hinge. Fixed A0/B0 cap extraction and `PlateSolid` extraction consume those same control points.

**Tech Stack:** C#/.NET 8, Godot 4.7 C#, existing FantaSim cartography contracts,
`UnifyMaths.Vector3D`, `GlobePlateSurfaces`, `PlateSolid`, `ArrayMesh`, and the repository
`dotnet unify-build` Godot export.

**Design authority:** `vault/specs/2026-07-17-spherical-plate-material-volume-design.md`

## Global Constraints

- Stop at Replacement A0/B0. Collision, divergence, transform volume deformation, chunk residency,
  and adaptive volume extraction remain outside this plan.
- Do not add a new test, test fixture, snapshot test, or test-only harness. The user explicitly
  overrode the plan skill's TDD default for this arc.
- Verification is targeted compilation, one production structural diagnostic, authority/caller
  audits, and same-state exported-window evidence. Existing tests may be mechanically updated only
  if a changed signature prevents their project from compiling.
- `CrustVolumeState` remains the only geological volume authority. Do not add
  `CrustVolumeState2`, `PlateVolume`, `CrustIsosurface`, `MaterialWedge`, `RayHit`, or a peer
  authority.
- Do not add a public geological `class`, `record`, or `struct`. Spatial query results use named
  tuples. Private scalar/vector helper methods are permitted; all vector algebra uses
  `UnifyMaths.Vector3D`.
- Keep `GlobeVec3`, `PlateBoundaryArc`, `CellBoundarySample`, `BoundaryProfileParameters`,
  `GlobePlateSurfaces`, `PlateCap`, `PlateSolid`, and `PlateSolidBuilder`; evolve them in place.
- Cells remain invisible control elements. A plate, never a cell or chunk, is the exploded unit.
- No dense Cartesian `N³` field, voxel persistence, generated render mesh, Godot resource, or
  view transform may enter `CrustVolumeState`.
- `CellElevations`, `CellCrustThickness`, and `CellFeatures` may remain compatibility projections,
  but neither default renderer may reconstruct geometry from them after Task 5.
- The assembled view uses closed contacts and ordinary depth occlusion. It must not open a joint
  gap or display buried crust.
- Color styling is not an acceptance gate. Do not retune production palettes; Task 6 uses one
  view-only neutral-gray material override so geometry can be judged without color cues.
- Visual crust thickness is deliberately independent of literal core scale.
- Use `GlobePlateSurfaces`' existing shared topology. Do not implement a second corner-deduplication
  table in the materializer or presentation.
- The fixed A0/B0 extractor is deliberate. Adaptive material-volume extraction begins only after
  the user accepts the paired proof.
- Use the existing `ArrayMesh.AddSurfaceFromArrays` publication seam. Do not add `SurfaceTool`,
  `MeshDataTool`, or a second Godot mesh publisher.
- Preserve unrelated working-tree changes. Commit each task with Conventional Commits and never use
  `--no-verify`.

## Ownership and file map

| Existing owner | A0/B0 responsibility | Disallowed duplicate |
|---|---|---|
| `CrustVolumeState` | material mapping, cell/plate bounds, exact occupancy, ordered ray intervals, identity | any peer plate-volume type |
| `WorldCrustMaterialization` | sole constructor of deformed outer/inner control points | renderer-side bending |
| `CellBoundaryField` / `CellBoundarySample` | nearest boundary frame and stable along-boundary phase | a second boundary-frame type |
| `BoundaryProfileParameters` / `BoundaryProfileShape` | one scalar/deformation grammar, including volcanic-chain geometry | a slab-only profile |
| `GlobePlateSurfaces` | one shared topology, corner-value gather/weld, state-derived outer caps | a second topology/dedupe cache |
| `PlateSolid` / `PlateSolidBuilder` | one closed per-plate mesh DTO and rigid explode transform | a volume-specific mesh DTO/builder |
| binder partials | publish the same caps/solids assembled or translated | mechanics or underlap generation |

No production `.cs` file is created by this plan. The only created files are this plan and the
runtime evidence README/images/logs in Task 6.

## Locked interfaces

These signatures are shared across tasks and must not be renamed during execution:

```csharp
// CrustVolumeState
public GlobeVec3 OuterPointAtCellCorner(int cellId, int cornerIndex);
public GlobeVec3 InnerPointAtCellCorner(int cellId, int cornerIndex);
public GlobeVec3 MapMaterialPoint(
    int cellId,
    double weight0,
    double weight1,
    double weight2,
    double depthFraction);
public bool ContainsWorldPoint(int plateId, GlobeVec3 point);
public IReadOnlyList<(int PlateId, double EnterDistance, double ExitDistance)> TraceRay(
    GlobeVec3 origin,
    GlobeVec3 direction,
    double maxDistance = 4.0);
public bool TryGetOutermostInterval(
    GlobeVec3 origin,
    GlobeVec3 direction,
    out (int PlateId, double EnterDistance, double ExitDistance) interval);
public (GlobeVec3 Min, GlobeVec3 Max) CellBounds(int cellId);
public (GlobeVec3 Min, GlobeVec3 Max) PlateBounds(int plateId);
public bool TryFindConvergentUnderlapProof(
    out (
        int BoundaryArcIndex,
        int OverridingPlateId,
        int SubductingPlateId,
        int SubductingCellId,
        GlobeVec3 RayOrigin,
        GlobeVec3 RayDirection,
        double OverridingEnter,
        double OverridingExit,
        double SubductingEnter,
        double SubductingExit) proof);

// GlobePlateSurfaces
public string TopologyDigest { get; }
public double[] BuildSharedCornerMetres(IReadOnlyList<double> elevationsByCell);
public (GlobeVec3[] Outer, GlobeVec3[] Inner) WeldPlateCorners(
    IReadOnlyList<GlobeVec3> outerCandidatesByCellCorner,
    IReadOnlyList<GlobeVec3> innerCandidatesByCellCorner);
public IReadOnlyList<PlateCap> BuildVolumeSurfaces(CrustVolumeState volume);

// CellBoundaryField
public static CellBoundarySample SampleDirection(
    GlobeVec3 direction,
    int plateId,
    IReadOnlyList<PlateBoundaryArc> arcs);

// PlateSolidBuilder
public static IReadOnlyList<PlateSolid> Build(
    IReadOnlyList<PlateCap> caps,
    CrustVolumeState volume);
```

## Source decisions

- Godot's official `ArrayMesh` procedure requires correctly sized vertex/normal/index arrays and
  publishes them with `AddSurfaceFromArrays`; the current publisher already follows that contract,
  so this plan retains it:
  [Godot 4.7 ArrayMesh tutorial](https://docs.godotengine.org/en/4.7/tutorials/3d/procedural_geometry/arraymesh.html).
- The local geometry doctrine separates spherical surface math from mesh topology. Boundary
  distance/frame math therefore stays in `CellBoundaryField` with Unify primitives, while
  `GlobePlateSurfaces` owns discrete shared-vertex topology:
  `plate-projects/unify-maths/docs/GEOMETRY-DOMAINS.md` and
  `plate-projects/unify-maths/docs/rfcs/RFC-0002-unifygeometry-spherical.md`.
- The repository `build/build.config.json` declares the Godot desktop target; the outer build is
  `dotnet unify-build BuildGodotDesktop --configuration Debug`.

## Adversarial review reconciliation

The user selected the current fresh-context review without a cross-model pass. All findings are
resolved in this plan:

- material controls are sampled individually; exact hinge controls remain fixed and a
  construction-time global-contact check rejects an opened outer envelope;
- the A0 ray is capped on the near side and must hit the named subducting cell that owns the
  convergent arc edge, proving attachment through that validated wedge;
- canonical rim diagonals use undeformed control direction, matching the state tetrahedral split;
- reference-orientation, shared-face, and tetrahedron-overlap checks reject degenerate, inverted,
  or non-injective wedges;
- a digest of ordered cells, plate ownership, and corners is checked before extraction;
- both captures use a view-only neutral-gray material gate; and
- the bundle identifier and foreground PID are verified independently before each screenshot.

---

### Task 1: Evolve the canonical boundary grammar

**Files:**

- Modify: `project/plugins/App.World/Topography/CellBoundarySample.cs`
- Modify: `project/plugins/App.World/Topography/CellBoundaryField.cs`
- Modify: `project/plugins/App.World/Topography/BoundaryProfileParameters.cs`
- Modify: `project/plugins/App.World/Topography/BoundaryProfileShape.cs`

**Interfaces:**

- Consumes: existing canonical `PlateBoundaryArc`, polarity, cell ownership, and scalar profile.
- Produces: a boundary-local frame on `CellBoundarySample`; one canonical set of visual volume and
  volcanic-chain controls on `BoundaryProfileParameters`.

- [ ] **Step 1: add non-positional frame properties to `CellBoundarySample`**

Add `using FantaSim.App.World.Dto;` and change the record terminator into this body. Keeping the
existing positional constructor prevents a repository-wide duplicate/adaptor migration:

```csharp
public readonly record struct CellBoundarySample(
    bool Found,
    double SignedDistanceRad,
    PlateBoundaryKind Kind,
    int NearestPointIndex,
    double TransformPhaseCoordinate,
    int CellPlateId,
    int ArcPlateA,
    int ArcPlateB,
    int? SubductingPlateId,
    bool IsCollision)
{
    public GlobeVec3 NearestBoundaryPoint { get; init; }

    // Unit tangent along the ordered boundary polyline at NearestBoundaryPoint.
    public GlobeVec3 AlongBoundaryDirection { get; init; }

    // Unit surface tangent from the boundary into this sample's owning plate.
    public GlobeVec3 AcrossBoundaryDirection { get; init; }

    // Stable world-space phase for all boundary kinds. TransformPhaseCoordinate remains as the
    // compatibility property and carries the same value.
    public double AlongBoundaryPhaseCoordinate { get; init; }
}
```

- [ ] **Step 2: populate the frame in `CellBoundaryField.Build`**

Compute the phase for every active boundary rather than transform boundaries only. Replace the
successful sample construction at the end of the loop with:

```csharp
var phaseCoordinate = TransformPhaseCoordinate(arc, arcVecs[bestArc][bestPoint]);
var frame = BoundaryFrame(centroid, arcVecs[bestArc], bestPoint);
result[c] = new CellBoundarySample(
    Found: true,
    signed,
    arc.Kind,
    bestPoint,
    phaseCoordinate,
    cell.PlateId,
    arc.PlateA,
    arc.PlateB,
    subductingId,
    isCollision)
{
    NearestBoundaryPoint = ToGlobeVec(frame.Point),
    AlongBoundaryDirection = ToGlobeVec(frame.Along),
    AcrossBoundaryDirection = ToGlobeVec(frame.Across),
    AlongBoundaryPhaseCoordinate = phaseCoordinate,
};
```

Add these complete helpers beside `Centroid`:

```csharp
private static (Vector3D Point, Vector3D Along, Vector3D Across) BoundaryFrame(
    Vector3D sampleDirection,
    IReadOnlyList<Vector3D> points,
    int pointIndex)
{
    var point = points[pointIndex].Normalize();
    int previousIndex = Math.Max(0, pointIndex - 1);
    int nextIndex = Math.Min(points.Count - 1, pointIndex + 1);
    var chord = points[nextIndex] - points[previousIndex];
    var along = (chord - (point * Vector3D.Dot(chord, point))).Normalize();
    if (along.Length() < 1e-12)
    {
        var reference = Math.Abs(point.X) < 0.9
            ? new Vector3D(1.0, 0.0, 0.0)
            : new Vector3D(0.0, 1.0, 0.0);
        along = Vector3D.Cross(reference, point).Normalize();
    }

    var across = Vector3D.Cross(point, along).Normalize();
    var towardSample =
        (sampleDirection - (point * Vector3D.Dot(sampleDirection, point))).Normalize();
    if (towardSample.Length() > 1e-12 && Vector3D.Dot(across, towardSample) < 0.0)
        across *= -1.0;

    return (point, along, across);
}

private static GlobeVec3 ToGlobeVec(Vector3D value)
    => new((float)value.X, (float)value.Y, (float)value.Z);
```

The two existing `Found: false` constructors require no change; the new properties correctly remain
zero-valued.

- [ ] **Step 3: expose the same boundary sampler at material-control directions**

Do not reuse one centroid sample for all three corners. Add the locked `SampleDirection` method and
factor the successful tail of `Build` through the same `CreateSample` helper:

```csharp
public static CellBoundarySample SampleDirection(
    GlobeVec3 direction,
    int plateId,
    IReadOnlyList<PlateBoundaryArc> arcs)
{
    ArgumentNullException.ThrowIfNull(arcs);
    var sampleDirection = Unit(direction);
    if (sampleDirection.Length() < 1e-15 || arcs.Count == 0)
    {
        return new CellBoundarySample(
            Found: false,
            0.0,
            PlateBoundaryKind.Inactive,
            0,
            0.0,
            plateId,
            -1,
            -1,
            null,
            IsCollision: false);
    }

    int bestArc = -1;
    int bestPoint = -1;
    double bestDot = -2.0;
    var arcVectors = new Vector3D[arcs.Count][];
    for (int a = 0; a < arcs.Count; a++)
    {
        arcVectors[a] = arcs[a].Points.Select(Unit).ToArray();
        if (arcs[a].Kind == PlateBoundaryKind.Inactive
            || (plateId != arcs[a].PlateA && plateId != arcs[a].PlateB))
        {
            continue;
        }

        int point = NearestPointIndex(sampleDirection, arcVectors[a], out double dot);
        if (dot > bestDot + 1e-15
            || (Math.Abs(dot - bestDot) <= 1e-15
                && (bestArc < 0 || CompareArcPriority(arcs[a], arcs[bestArc]) < 0)))
        {
            bestArc = a;
            bestPoint = point;
            bestDot = dot;
        }
    }

    if (bestArc < 0)
    {
        return new CellBoundarySample(
            Found: false,
            0.0,
            PlateBoundaryKind.Inactive,
            0,
            0.0,
            plateId,
            -1,
            -1,
            null,
            IsCollision: false);
    }

    return CreateSample(
        sampleDirection,
        plateId,
        arcs[bestArc],
        arcVectors[bestArc],
        bestPoint,
        Math.Acos(Math.Clamp(bestDot, -1.0, 1.0)));
}

private static CellBoundarySample CreateSample(
    Vector3D sampleDirection,
    int plateId,
    PlateBoundaryArc arc,
    IReadOnlyList<Vector3D> arcPoints,
    int pointIndex,
    double distance)
{
    int? subductingId = arc.Kind == PlateBoundaryKind.Convergent
        ? arc.SubductingPlateId
        : null;
    bool isCollision = arc.Kind == PlateBoundaryKind.Convergent && arc.IsCollision;
    double signed = distance;
    if (!isCollision && subductingId is int subducting)
    {
        signed = plateId == subducting
            ? -Math.Max(distance, double.Epsilon)
            : Math.Max(distance, double.Epsilon);
    }

    var frame = BoundaryFrame(sampleDirection, arcPoints, pointIndex);
    double phase = TransformPhaseCoordinate(arc, arcPoints[pointIndex]);
    return new CellBoundarySample(
        Found: true,
        signed,
        arc.Kind,
        pointIndex,
        phase,
        plateId,
        arc.PlateA,
        arc.PlateB,
        subductingId,
        isCollision)
    {
        NearestBoundaryPoint = ToGlobeVec(frame.Point),
        AlongBoundaryDirection = ToGlobeVec(frame.Along),
        AcrossBoundaryDirection = ToGlobeVec(frame.Across),
        AlongBoundaryPhaseCoordinate = phase,
    };
}
```

Add `using System.Linq;`. In `Build`, retain its existing topological exact-edge selection, but
replace the successful sample construction with `CreateSample(centroid, cell.PlateId, arc,
arcVecs[bestArc], bestPoint, distance)`. Thus scalar cell profiles preserve their exact-edge
behavior, while material controls get their own signed distance and frame. A corner equal to the
boundary has distance zero; the deformation in Task 3 must therefore leave it fixed.

- [ ] **Step 4: extend `BoundaryProfileParameters`, not a slab-specific profile**

Add these init-only properties inside the existing record body:

```csharp
// Visual volume scale. Thickness is intentionally not literal relative to the core.
public double VisualCrustThicknessUnitRadius { get; init; } = 0.075;

// Down-going edge displacement at the hinge. The bend-band width is derived from the existing
// trench/arc widths, so there is no second width authority.
public double ConvergentSlabUnderlapLengthUnitRadius { get; init; } = 0.18;
public double ConvergentSlabDepthUnitRadius { get; init; } = 0.14;
public double ConvergentOverridingRootDepthUnitRadius { get; init; } = 0.04;

// Medium-scale cone chain placed on the existing volcanic-arc foundation.
public double ConvergentVolcanoConeHeight { get; init; } = 1800.0;
public double ConvergentVolcanoPeriodPoints { get; init; } = 42.0;
public double ConvergentVolcanoSharpness { get; init; } = 8.0;
```

Extend `Zero` with:

```csharp
ConvergentSlabUnderlapLengthUnitRadius = 0.0,
ConvergentSlabDepthUnitRadius = 0.0,
ConvergentOverridingRootDepthUnitRadius = 0.0,
ConvergentVolcanoConeHeight = 0.0,
```

`VisualCrustThicknessUnitRadius` deliberately remains non-zero in `Zero`: a neutral world still has
a thick continuous shell.

- [ ] **Step 5: add cone-chain geometry to the existing scalar envelope**

Change the convergent dispatch and method signature:

```csharp
PlateBoundaryKind.Convergent => s.IsCollision
    ? ConvergentCollision(s.SignedDistanceRad, p)
    : ConvergentSubduction(
        s.SignedDistanceRad,
        s.AlongBoundaryPhaseCoordinate,
        p),
```

Replace `ConvergentSubduction` with:

```csharp
private static double ConvergentSubduction(
    double signed,
    double alongBoundaryPhase,
    in BoundaryProfileParameters p)
{
    if (signed <= 0.0)
    {
        double d = -signed;
        return p.ConvergentTrenchDepth * Falloff(d, p.ConvergentTrenchHalfWidthRad);
    }

    double wedge = p.ConvergentCollisionHeight
                 * Falloff(signed, p.ConvergentCollisionHalfWidthRad);
    double acrossArc = Falloff(
        signed - p.ConvergentArcSetbackRad,
        p.ConvergentArcHalfWidthRad);
    double arc = p.ConvergentArcHeight * acrossArc;
    double cones = VolcanoChain(alongBoundaryPhase, acrossArc, p);
    return wedge + arc + cones;
}

private static double VolcanoChain(
    double alongBoundaryPhase,
    double acrossArc,
    in BoundaryProfileParameters p)
{
    if (acrossArc <= 0.0
        || p.ConvergentVolcanoConeHeight == 0.0
        || p.ConvergentVolcanoPeriodPoints <= 0.0
        || p.ConvergentVolcanoSharpness <= 0.0)
    {
        return 0.0;
    }

    double phase =
        2.0 * Math.PI * alongBoundaryPhase / p.ConvergentVolcanoPeriodPoints;
    double crest = Math.Max(0.0, Math.Cos(phase));
    return p.ConvergentVolcanoConeHeight
         * Math.Pow(crest, p.ConvergentVolcanoSharpness)
         * acrossArc;
}
```

- [ ] **Step 6: compile the canonical grammar**

Run:

```bash
dotnet build project/plugins/App.World/App.World.csproj
```

Expected: exit 0. Warnings already present on the branch are acceptable; new compile errors are not.
Do not run or add tests.

- [ ] **Step 7: audit and commit**

Run:

```bash
rg -n "VisualCrustThicknessUnitRadius|ConvergentSlabUnderlapLengthUnitRadius|ConvergentVolcanoConeHeight" project --glob '*.cs'
```

Expected production declarations: only `BoundaryProfileParameters`; consumers arrive in later
tasks. There must be no newly declared profile type.

```bash
git add \
  project/plugins/App.World/Topography/CellBoundarySample.cs \
  project/plugins/App.World/Topography/CellBoundaryField.cs \
  project/plugins/App.World/Topography/BoundaryProfileParameters.cs \
  project/plugins/App.World/Topography/BoundaryProfileShape.cs
git commit -m "feat(world): extend canonical boundary deformation grammar"
```

---

### Task 2: Replace `CrustVolumeState` radial semantics with material wedges

**Files:**

- Modify: `project/contracts/App.World/CrustVolumeState.cs`

**Interfaces:**

- Consumes: welded outer/inner `GlobeVec3[cellCount * 3]` arrays supplied by Task 3.
- Produces: every locked `CrustVolumeState` interface, exact tetrahedral occupancy/ray semantics,
  per-cell/per-plate bounds, and deterministic state identity.

- [ ] **Step 1: change the stored authority and identity**

Keep the existing legacy elevation/thickness/feature/fraction arrays as compatibility projections.
Add these fields and properties, change the version, and delete
`OuterRadiusMetresAtCell`, `InnerRadiusMetresAtCell`, and `SignedDensityAtCellRadius`:

```csharp
public const string CurrentAlgorithmVersion = "crust-volume.v2";

private const double SpatialEpsilon = 1e-8;
private readonly GlobeVec3[] _outerPointsByCellCorner;
private readonly GlobeVec3[] _innerPointsByCellCorner;
private readonly IReadOnlyDictionary<int, int[]> _cellIdsByPlate;

public int Seed { get; }
public int GraphRevision { get; }
public string TopologyDigest { get; }
public string DeformationParameterDigest { get; }
```

Extend the private constructor with `seed`, `graphRevision`,
`topologyDigest`, `deformationParameterDigest`, `outerPointsByCellCorner`, and
`innerPointsByCellCorner`; assign cloned arrays and build the plate index exactly once:

```csharp
Seed = seed;
GraphRevision = graphRevision;
TopologyDigest = topologyDigest;
DeformationParameterDigest = deformationParameterDigest;
_outerPointsByCellCorner = outerPointsByCellCorner;
_innerPointsByCellCorner = innerPointsByCellCorner;
_cellIdsByPlate = new ReadOnlyDictionary<int, int[]>(
    globe.Cells
        .GroupBy(cell => cell.PlateId)
        .OrderBy(group => group.Key)
        .ToDictionary(
            group => group.Key,
            group => group.Select(cell => cell.CellId).OrderBy(id => id).ToArray()));
```

- [ ] **Step 2: replace the factory signature and validation**

Use this exact signature:

```csharp
public static CrustVolumeState Create(
    long tick,
    int seed,
    int graphRevision,
    string topologyDigest,
    string deformationParameterDigest,
    WorldGlobeSnapshot globe,
    IReadOnlyList<PlateBoundaryArc> boundaryArcs,
    IReadOnlyList<GlobeVec3> outerPointsByCellCorner,
    IReadOnlyList<GlobeVec3> innerPointsByCellCorner,
    IReadOnlyList<double> outerElevationsMetresByCell,
    IReadOnlyList<double> crustThicknessMetresByCell,
    IReadOnlyList<CellCrustFeature> featuresByCell,
    IReadOnlyDictionary<int, double> continentalFractionByCell)
```

Retain the existing tick, globe, per-cell array, fraction, and arc validation. Add these exact
guards before construction:

```csharp
if (string.IsNullOrWhiteSpace(topologyDigest))
    throw new ArgumentException(
        "A topology digest is required.",
        nameof(topologyDigest));
if (string.IsNullOrWhiteSpace(deformationParameterDigest))
    throw new ArgumentException(
        "A deformation parameter digest is required.",
        nameof(deformationParameterDigest));
if (graphRevision < 0)
    throw new ArgumentOutOfRangeException(nameof(graphRevision));
if (outerPointsByCellCorner.Count != globe.CellCount * 3)
    throw new ArgumentException(
        "Outer material controls must contain three points per cell.",
        nameof(outerPointsByCellCorner));
if (innerPointsByCellCorner.Count != globe.CellCount * 3)
    throw new ArgumentException(
        "Inner material controls must contain three points per cell.",
        nameof(innerPointsByCellCorner));
for (int cellIndex = 0; cellIndex < globe.Cells.Count; cellIndex++)
{
    if (globe.Cells[cellIndex].CellId != cellIndex)
        throw new ArgumentException(
            "Globe cells must be stored in CellId order.",
            nameof(globe));
}

var outerPoints = outerPointsByCellCorner.ToArray();
var innerPoints = innerPointsByCellCorner.ToArray();
for (int i = 0; i < outerPoints.Length; i++)
{
    if (!IsFinite(outerPoints[i]) || !IsFinite(innerPoints[i]))
        throw new ArgumentOutOfRangeException(
            nameof(outerPointsByCellCorner),
            "Every material control point must be finite.");
}
```

Replace the factory's final return with:

```csharp
var state = new CrustVolumeState(
    tick,
    seed,
    graphRevision,
    topologyDigest,
    deformationParameterDigest,
    globe,
    boundaryArcs,
    outerPoints,
    innerPoints,
    elevations,
    thickness,
    features,
    fractions);
state.ValidateMaterialWedges();
return state;
```

The private constructor parameter order must match that call. Add:

```csharp
private static bool IsFinite(GlobeVec3 point)
    => IsFinite(point.X) && IsFinite(point.Y) && IsFinite(point.Z);

private void ValidateMaterialWedges()
{
    foreach (var cell in Globe.Cells)
    {
        GetWedge(
            cell.CellId,
            out var outer0,
            out var outer1,
            out var outer2,
            out var inner0,
            out var inner1,
            out var inner2);

        Span<int> order = stackalloc int[3] { 0, 1, 2 };
        GetCanonicalCornerOrder(cell.CellId, order);
        Vector3D referenceOuter0 = ToVector(OriginalCorner(cell, order[0])).Normalize();
        Vector3D referenceOuter1 = ToVector(OriginalCorner(cell, order[1])).Normalize();
        Vector3D referenceOuter2 = ToVector(OriginalCorner(cell, order[2])).Normalize();
        Vector3D referenceInner0 = referenceOuter0 * 0.9;
        Vector3D referenceInner1 = referenceOuter1 * 0.9;
        Vector3D referenceInner2 = referenceOuter2 * 0.9;

        ValidateTetraOrientation(
            cell.CellId,
            0,
            outer0,
            outer1,
            outer2,
            inner2,
            referenceOuter0,
            referenceOuter1,
            referenceOuter2,
            referenceInner2);
        ValidateTetraOrientation(
            cell.CellId,
            1,
            outer0,
            outer1,
            inner1,
            inner2,
            referenceOuter0,
            referenceOuter1,
            referenceInner1,
            referenceInner2);
        ValidateTetraOrientation(
            cell.CellId,
            2,
            outer0,
            inner0,
            inner1,
            inner2,
            referenceOuter0,
            referenceInner0,
            referenceInner1,
            referenceInner2);

        ValidateOppositeSides(
            cell.CellId,
            "T0/T1",
            outer0,
            outer1,
            inner2,
            outer2,
            inner1);
        ValidateOppositeSides(
            cell.CellId,
            "T1/T2",
            outer0,
            inner1,
            inner2,
            outer1,
            inner0);

        var tetra0 = new[] { outer0, outer1, outer2, inner2 };
        var tetra2 = new[] { outer0, inner0, inner1, inner2 };
        if (TetrahedraHaveInteriorOverlap(tetra0, tetra2))
            throw new ArgumentException(
                $"Cell {cell.CellId} has non-injective T0/T2 material overlap.");
    }
}

private static void ValidateTetraOrientation(
    int cellId,
    int tetraIndex,
    Vector3D a,
    Vector3D b,
    Vector3D c,
    Vector3D d,
    Vector3D referenceA,
    Vector3D referenceB,
    Vector3D referenceC,
    Vector3D referenceD)
{
    double actual = SixVolume(a, b, c, d);
    double reference = SixVolume(
        referenceA,
        referenceB,
        referenceC,
        referenceD);
    if (Math.Abs(actual) < SpatialEpsilon
        || Math.Abs(reference) < SpatialEpsilon
        || Math.Sign(actual) != Math.Sign(reference))
    {
        throw new ArgumentException(
            $"Cell {cellId} tetrahedron {tetraIndex} is degenerate or inverted.");
    }
}

private static void ValidateOppositeSides(
    int cellId,
    string sharedFace,
    Vector3D a,
    Vector3D b,
    Vector3D c,
    Vector3D leftOpposite,
    Vector3D rightOpposite)
{
    Vector3D normal = Vector3D.Cross(b - a, c - a);
    double left = Vector3D.Dot(leftOpposite - a, normal);
    double right = Vector3D.Dot(rightOpposite - a, normal);
    if (Math.Abs(left) < SpatialEpsilon
        || Math.Abs(right) < SpatialEpsilon
        || Math.Sign(left) == Math.Sign(right))
    {
        throw new ArgumentException(
            $"Cell {cellId} folds across shared material face {sharedFace}.");
    }
}

private static bool TetrahedraHaveInteriorOverlap(
    IReadOnlyList<Vector3D> left,
    IReadOnlyList<Vector3D> right)
{
    ReadOnlySpan<(int A, int B, int C)> faces =
    [
        (0, 1, 2),
        (0, 1, 3),
        (0, 2, 3),
        (1, 2, 3),
    ];
    ReadOnlySpan<(int A, int B)> edges =
    [
        (0, 1),
        (0, 2),
        (0, 3),
        (1, 2),
        (1, 3),
        (2, 3),
    ];

    foreach (var face in faces)
    {
        if (IsSeparatingAxis(
            Vector3D.Cross(
                left[face.B] - left[face.A],
                left[face.C] - left[face.A]),
            left,
            right))
        {
            return false;
        }
        if (IsSeparatingAxis(
            Vector3D.Cross(
                right[face.B] - right[face.A],
                right[face.C] - right[face.A]),
            left,
            right))
        {
            return false;
        }
    }

    foreach (var leftEdge in edges)
    {
        Vector3D leftDirection = left[leftEdge.B] - left[leftEdge.A];
        foreach (var rightEdge in edges)
        {
            Vector3D axis = Vector3D.Cross(
                leftDirection,
                right[rightEdge.B] - right[rightEdge.A]);
            if (IsSeparatingAxis(axis, left, right))
                return false;
        }
    }
    return true;
}

private static bool IsSeparatingAxis(
    Vector3D axis,
    IReadOnlyList<Vector3D> left,
    IReadOnlyList<Vector3D> right)
{
    if (axis.Length() < SpatialEpsilon)
        return false;
    double leftMin = double.PositiveInfinity;
    double leftMax = double.NegativeInfinity;
    double rightMin = double.PositiveInfinity;
    double rightMax = double.NegativeInfinity;
    for (int i = 0; i < 4; i++)
    {
        double leftProjection = Vector3D.Dot(left[i], axis);
        double rightProjection = Vector3D.Dot(right[i], axis);
        leftMin = Math.Min(leftMin, leftProjection);
        leftMax = Math.Max(leftMax, leftProjection);
        rightMin = Math.Min(rightMin, rightProjection);
        rightMax = Math.Max(rightMax, rightProjection);
    }
    return leftMax <= rightMin + SpatialEpsilon
        || rightMax <= leftMin + SpatialEpsilon;
}

private static double SixVolume(
    Vector3D a,
    Vector3D b,
    Vector3D c,
    Vector3D d)
    => Vector3D.Dot(b - a, Vector3D.Cross(c - a, d - a));
```

Do not require radial ordering. A valid descending wedge may have an inner point whose radius is
greater than another cell's outer point.

- [ ] **Step 3: implement the material mapping and extraction accessors**

Add these complete methods:

```csharp
public GlobeVec3 OuterPointAtCellCorner(int cellId, int cornerIndex)
{
    ValidateCellCorner(cellId, cornerIndex);
    return _outerPointsByCellCorner[(cellId * 3) + cornerIndex];
}

public GlobeVec3 InnerPointAtCellCorner(int cellId, int cornerIndex)
{
    ValidateCellCorner(cellId, cornerIndex);
    return _innerPointsByCellCorner[(cellId * 3) + cornerIndex];
}

public GlobeVec3 MapMaterialPoint(
    int cellId,
    double weight0,
    double weight1,
    double weight2,
    double depthFraction)
{
    ValidateCellId(cellId);
    if (!IsFinite(weight0) || !IsFinite(weight1) || !IsFinite(weight2))
        throw new ArgumentOutOfRangeException(nameof(weight0));
    if (Math.Abs((weight0 + weight1 + weight2) - 1.0) > 1e-8
        || weight0 < 0.0 || weight1 < 0.0 || weight2 < 0.0)
    {
        throw new ArgumentOutOfRangeException(
            nameof(weight0),
            "Barycentric weights must be non-negative and sum to one.");
    }
    if (!IsFinite(depthFraction) || depthFraction < 0.0 || depthFraction > 1.0)
        throw new ArgumentOutOfRangeException(nameof(depthFraction));

    Span<int> order = stackalloc int[3];
    order[0] = 0;
    order[1] = 1;
    order[2] = 2;
    GetCanonicalCornerOrder(cellId, order);
    Span<double> weights = stackalloc double[3];
    weights[0] = weight0;
    weights[1] = weight1;
    weights[2] = weight2;
    double w0 = weights[order[0]];
    double w1 = weights[order[1]];
    double w2 = weights[order[2]];
    Vector3D outer0 = ToVector(OuterPointAtCellCorner(cellId, order[0]));
    Vector3D outer1 = ToVector(OuterPointAtCellCorner(cellId, order[1]));
    Vector3D outer2 = ToVector(OuterPointAtCellCorner(cellId, order[2]));
    Vector3D inner0 = ToVector(InnerPointAtCellCorner(cellId, order[0]));
    Vector3D inner1 = ToVector(InnerPointAtCellCorner(cellId, order[1]));
    Vector3D inner2 = ToVector(InnerPointAtCellCorner(cellId, order[2]));

    // Exact parameter-space partition matching GetWedge's three tetrahedra:
    // T0=(outer0,outer1,outer2,inner2),
    // T1=(outer0,outer1,inner1,inner2),
    // T2=(outer0,inner0,inner1,inner2).
    Vector3D mapped;
    if (depthFraction <= w2)
    {
        mapped =
            (outer0 * w0)
          + (outer1 * w1)
          + (outer2 * (w2 - depthFraction))
          + (inner2 * depthFraction);
    }
    else if (depthFraction >= w1 + w2)
    {
        mapped =
            (outer0 * (1.0 - depthFraction))
          + (inner0 * (depthFraction - w1 - w2))
          + (inner1 * w1)
          + (inner2 * w2);
    }
    else
    {
        mapped =
            (outer0 * w0)
          + (outer1 * (w1 + w2 - depthFraction))
          + (inner1 * (depthFraction - w2))
          + (inner2 * w2);
    }
    return ToGlobe(mapped);
}

private void ValidateCellCorner(int cellId, int cornerIndex)
{
    ValidateCellId(cellId);
    if ((uint)cornerIndex >= 3u)
        throw new ArgumentOutOfRangeException(nameof(cornerIndex));
}
```

- [ ] **Step 4: implement exact containment and ordered ray intervals**

Use the fixed three-tetrahedra decomposition of each triangular wedge:

```text
T0 = (outer0, outer1, outer2, inner2)
T1 = (outer0, outer1, inner1, inner2)
T2 = (outer0, inner0, inner1, inner2)
```

Add `using UnifyMaths;` and these methods. They contain the complete A0 query algorithm; no sampled
ray marcher is permitted:

```csharp
public bool ContainsWorldPoint(int plateId, GlobeVec3 point)
{
    if (!_cellIdsByPlate.TryGetValue(plateId, out var cellIds))
        return false;

    Vector3D p = ToVector(point);
    foreach (int cellId in cellIds)
    {
        GetWedge(cellId, out var o0, out var o1, out var o2, out var i0, out var i1, out var i2);
        if (PointInTetra(p, o0, o1, o2, i2)
            || PointInTetra(p, o0, o1, i1, i2)
            || PointInTetra(p, o0, i0, i1, i2))
        {
            return true;
        }
    }
    return false;
}

public IReadOnlyList<(int PlateId, double EnterDistance, double ExitDistance)> TraceRay(
    GlobeVec3 origin,
    GlobeVec3 direction,
    double maxDistance = 4.0)
{
    if (!IsFinite(origin) || !IsFinite(direction))
        throw new ArgumentOutOfRangeException(nameof(origin));
    if (!IsFinite(maxDistance) || maxDistance <= 0.0)
        throw new ArgumentOutOfRangeException(nameof(maxDistance));

    Vector3D rayOrigin = ToVector(origin);
    Vector3D rayDirection = ToVector(direction).Normalize();
    if (rayDirection.Length() < SpatialEpsilon)
        throw new ArgumentOutOfRangeException(nameof(direction));

    var raw = new List<(int PlateId, double EnterDistance, double ExitDistance)>();
    foreach (var cell in Globe.Cells)
    {
        GetWedge(
            cell.CellId,
            out var o0,
            out var o1,
            out var o2,
            out var i0,
            out var i1,
            out var i2);
        AddTetraInterval(raw, cell.PlateId, rayOrigin, rayDirection, maxDistance, o0, o1, o2, i2);
        AddTetraInterval(raw, cell.PlateId, rayOrigin, rayDirection, maxDistance, o0, o1, i1, i2);
        AddTetraInterval(raw, cell.PlateId, rayOrigin, rayDirection, maxDistance, o0, i0, i1, i2);
    }

    raw.Sort(static (left, right) =>
    {
        int byPlate = left.PlateId.CompareTo(right.PlateId);
        if (byPlate != 0) return byPlate;
        return left.EnterDistance.CompareTo(right.EnterDistance);
    });

    var merged = new List<(int PlateId, double EnterDistance, double ExitDistance)>();
    foreach (var current in raw)
    {
        if (merged.Count == 0)
        {
            merged.Add(current);
            continue;
        }

        var previous = merged[^1];
        if (previous.PlateId == current.PlateId
            && current.EnterDistance <= previous.ExitDistance + SpatialEpsilon)
        {
            merged[^1] = (
                previous.PlateId,
                previous.EnterDistance,
                Math.Max(previous.ExitDistance, current.ExitDistance));
        }
        else
        {
            merged.Add(current);
        }
    }

    merged.Sort(static (left, right) =>
    {
        int byEnter = left.EnterDistance.CompareTo(right.EnterDistance);
        return byEnter != 0 ? byEnter : left.PlateId.CompareTo(right.PlateId);
    });
    return merged;
}

public bool TryGetOutermostInterval(
    GlobeVec3 origin,
    GlobeVec3 direction,
    out (int PlateId, double EnterDistance, double ExitDistance) interval)
{
    var intervals = TraceRay(origin, direction);
    if (intervals.Count == 0)
    {
        interval = default;
        return false;
    }
    interval = intervals[0];
    return true;
}

private static void AddTetraInterval(
    List<(int PlateId, double EnterDistance, double ExitDistance)> target,
    int plateId,
    Vector3D origin,
    Vector3D direction,
    double maxDistance,
    Vector3D v0,
    Vector3D v1,
    Vector3D v2,
    Vector3D v3)
{
    if (TryTraceTetra(
        origin,
        direction,
        maxDistance,
        v0,
        v1,
        v2,
        v3,
        out double enter,
        out double exit))
    {
        target.Add((plateId, enter, exit));
    }
}

private static bool PointInTetra(
    Vector3D point,
    Vector3D v0,
    Vector3D v1,
    Vector3D v2,
    Vector3D v3)
{
    if (!TryBarycentric(point - v0, v1 - v0, v2 - v0, v3 - v0, out var b))
        return false;
    double w0 = 1.0 - b.X - b.Y - b.Z;
    return w0 >= -SpatialEpsilon
        && b.X >= -SpatialEpsilon
        && b.Y >= -SpatialEpsilon
        && b.Z >= -SpatialEpsilon;
}

private static bool TryTraceTetra(
    Vector3D origin,
    Vector3D direction,
    double maxDistance,
    Vector3D v0,
    Vector3D v1,
    Vector3D v2,
    Vector3D v3,
    out double enter,
    out double exit)
{
    enter = 0.0;
    exit = maxDistance;
    var c0 = v1 - v0;
    var c1 = v2 - v0;
    var c2 = v3 - v0;
    if (!TryBarycentric(origin - v0, c0, c1, c2, out var atOrigin)
        || !TryBarycentric(direction, c0, c1, c2, out var alongRay))
    {
        return false;
    }

    Span<double> intercept = stackalloc double[4]
    {
        1.0 - atOrigin.X - atOrigin.Y - atOrigin.Z,
        atOrigin.X,
        atOrigin.Y,
        atOrigin.Z,
    };
    Span<double> slope = stackalloc double[4]
    {
        -alongRay.X - alongRay.Y - alongRay.Z,
        alongRay.X,
        alongRay.Y,
        alongRay.Z,
    };

    for (int i = 0; i < 4; i++)
    {
        if (Math.Abs(slope[i]) < SpatialEpsilon)
        {
            if (intercept[i] < -SpatialEpsilon)
                return false;
            continue;
        }

        double crossing = (-SpatialEpsilon - intercept[i]) / slope[i];
        if (slope[i] > 0.0)
            enter = Math.Max(enter, crossing);
        else
            exit = Math.Min(exit, crossing);
        if (enter > exit)
            return false;
    }

    return exit >= 0.0 && enter <= maxDistance;
}

private static bool TryBarycentric(
    Vector3D rightHandSide,
    Vector3D column0,
    Vector3D column1,
    Vector3D column2,
    out (double X, double Y, double Z) value)
{
    double determinant = Vector3D.Dot(column0, Vector3D.Cross(column1, column2));
    if (Math.Abs(determinant) < SpatialEpsilon)
    {
        value = default;
        return false;
    }

    value = (
        Vector3D.Dot(rightHandSide, Vector3D.Cross(column1, column2)) / determinant,
        Vector3D.Dot(column0, Vector3D.Cross(rightHandSide, column2)) / determinant,
        Vector3D.Dot(column0, Vector3D.Cross(column1, rightHandSide)) / determinant);
    return true;
}

private void GetWedge(
    int cellId,
    out Vector3D outer0,
    out Vector3D outer1,
    out Vector3D outer2,
    out Vector3D inner0,
    out Vector3D inner1,
    out Vector3D inner2)
{
    Span<int> order = stackalloc int[3];
    order[0] = 0;
    order[1] = 1;
    order[2] = 2;
    GetCanonicalCornerOrder(cellId, order);
    outer0 = ToVector(OuterPointAtCellCorner(cellId, order[0]));
    outer1 = ToVector(OuterPointAtCellCorner(cellId, order[1]));
    outer2 = ToVector(OuterPointAtCellCorner(cellId, order[2]));
    inner0 = ToVector(InnerPointAtCellCorner(cellId, order[0]));
    inner1 = ToVector(InnerPointAtCellCorner(cellId, order[1]));
    inner2 = ToVector(InnerPointAtCellCorner(cellId, order[2]));
}

private void GetCanonicalCornerOrder(int cellId, Span<int> order)
{
    var cell = Globe.Cells[cellId];
    for (int i = 1; i < order.Length; i++)
    {
        int value = order[i];
        int j = i - 1;
        while (j >= 0
               && CompareOriginalCorner(
                   OriginalCorner(cell, value),
                   OriginalCorner(cell, order[j])) < 0)
        {
            order[j + 1] = order[j];
            j--;
        }
        order[j + 1] = value;
    }
}

private static GlobeVec3 OriginalCorner(GlobeCell cell, int cornerIndex)
    => cornerIndex switch
    {
        0 => cell.C0,
        1 => cell.C1,
        _ => cell.C2,
    };

private static int CompareOriginalCorner(GlobeVec3 left, GlobeVec3 right)
{
    int byX = left.X.CompareTo(right.X);
    if (byX != 0) return byX;
    int byY = left.Y.CompareTo(right.Y);
    return byY != 0 ? byY : left.Z.CompareTo(right.Z);
}
```

- [ ] **Step 5: implement conservative bounds**

```csharp
public (GlobeVec3 Min, GlobeVec3 Max) CellBounds(int cellId)
{
    ValidateCellId(cellId);
    return BoundsForCells(new[] { cellId });
}

public (GlobeVec3 Min, GlobeVec3 Max) PlateBounds(int plateId)
{
    if (!_cellIdsByPlate.TryGetValue(plateId, out var cellIds))
        throw new ArgumentOutOfRangeException(nameof(plateId));
    return BoundsForCells(cellIds);
}

private (GlobeVec3 Min, GlobeVec3 Max) BoundsForCells(IReadOnlyList<int> cellIds)
{
    double minX = double.PositiveInfinity;
    double minY = double.PositiveInfinity;
    double minZ = double.PositiveInfinity;
    double maxX = double.NegativeInfinity;
    double maxY = double.NegativeInfinity;
    double maxZ = double.NegativeInfinity;
    foreach (int cellId in cellIds)
    {
        for (int corner = 0; corner < 3; corner++)
        {
            Accumulate(OuterPointAtCellCorner(cellId, corner));
            Accumulate(InnerPointAtCellCorner(cellId, corner));
        }
    }

    return (
        new GlobeVec3((float)minX, (float)minY, (float)minZ),
        new GlobeVec3((float)maxX, (float)maxY, (float)maxZ));

    void Accumulate(GlobeVec3 point)
    {
        minX = Math.Min(minX, point.X);
        minY = Math.Min(minY, point.Y);
        minZ = Math.Min(minZ, point.Z);
        maxX = Math.Max(maxX, point.X);
        maxY = Math.Max(maxY, point.Y);
        maxZ = Math.Max(maxZ, point.Z);
    }
}
```

- [ ] **Step 6: implement the production underlap diagnostic**

This scan examines near-side radial rays through overriding cells close to each convergent arc. It
succeeds only when the overriding interval is followed by a distinct deeper interval from the
named down-going plate **in a non-inverted cell that owns the arc hinge itself**. The near-side
distance stops well before the centre, so a far-side occurrence of the same plate cannot pass:

```csharp
public bool TryFindConvergentUnderlapProof(
    out (
        int BoundaryArcIndex,
        int OverridingPlateId,
        int SubductingPlateId,
        int SubductingCellId,
        GlobeVec3 RayOrigin,
        GlobeVec3 RayDirection,
        double OverridingEnter,
        double OverridingExit,
        double SubductingEnter,
        double SubductingExit) proof)
{
    proof = default;
    const double candidateHalfWidthRad = 0.30;
    const double nearSideMaxDistance = 1.20;
    double minimumArcDot = Math.Cos(candidateHalfWidthRad);

    for (int arcIndex = 0; arcIndex < BoundaryArcs.Count; arcIndex++)
    {
        var arc = BoundaryArcs[arcIndex];
        if (arc.Kind != PlateBoundaryKind.Convergent
            || arc.IsCollision
            || arc.SubductingPlateId is not int subductingPlateId)
        {
            continue;
        }

        int overridingPlateId =
            arc.PlateA == subductingPlateId ? arc.PlateB : arc.PlateA;
        foreach (var cell in Globe.Cells
                     .Where(cell => cell.PlateId == overridingPlateId)
                     .OrderBy(cell => cell.CellId))
        {
            Vector3D centre = (
                ToVector(cell.C0) + ToVector(cell.C1) + ToVector(cell.C2)
            ).Normalize();
            double nearestArcDot = -1.0;
            foreach (var point in arc.Points)
                nearestArcDot = Math.Max(
                    nearestArcDot,
                    Vector3D.Dot(centre, ToVector(point).Normalize()));
            if (nearestArcDot < minimumArcDot)
                continue;

            var origin = ToGlobe(centre * 1.75);
            var direction = ToGlobe(centre * -1.0);
            var intervals = TraceCellIntervals(
                origin,
                direction,
                nearSideMaxDistance);
            for (int first = 0; first < intervals.Count; first++)
            {
                var overriding = intervals[first];
                if (overriding.PlateId != overridingPlateId)
                    continue;
                for (int second = first + 1; second < intervals.Count; second++)
                {
                    var downGoing = intervals[second];
                    if (downGoing.PlateId != subductingPlateId
                        || !CellTouchesArc(
                            Globe.Cells[downGoing.CellId],
                            arc)
                        || downGoing.EnterDistance <= overriding.ExitDistance + SpatialEpsilon)
                    {
                        continue;
                    }

                    proof = (
                        arcIndex,
                        overridingPlateId,
                        subductingPlateId,
                        downGoing.CellId,
                        origin,
                        direction,
                        overriding.EnterDistance,
                        overriding.ExitDistance,
                        downGoing.EnterDistance,
                        downGoing.ExitDistance);
                    return true;
                }
            }
        }
    }
    return false;
}

private IReadOnlyList<(
    int CellId,
    int PlateId,
    double EnterDistance,
    double ExitDistance)> TraceCellIntervals(
        GlobeVec3 origin,
        GlobeVec3 direction,
        double maxDistance)
{
    Vector3D rayOrigin = ToVector(origin);
    Vector3D rayDirection = ToVector(direction).Normalize();
    var raw = new List<(
        int CellId,
        int PlateId,
        double EnterDistance,
        double ExitDistance)>();
    foreach (var cell in Globe.Cells)
    {
        GetWedge(
            cell.CellId,
            out var o0,
            out var o1,
            out var o2,
            out var i0,
            out var i1,
            out var i2);
        Add(o0, o1, o2, i2);
        Add(o0, o1, i1, i2);
        Add(o0, i0, i1, i2);

        void Add(Vector3D a, Vector3D b, Vector3D c, Vector3D d)
        {
            if (TryTraceTetra(
                rayOrigin,
                rayDirection,
                maxDistance,
                a,
                b,
                c,
                d,
                out double enter,
                out double exit))
            {
                raw.Add((cell.CellId, cell.PlateId, enter, exit));
            }
        }
    }

    raw.Sort(static (left, right) =>
    {
        int byCell = left.CellId.CompareTo(right.CellId);
        return byCell != 0
            ? byCell
            : left.EnterDistance.CompareTo(right.EnterDistance);
    });
    var merged = new List<(
        int CellId,
        int PlateId,
        double EnterDistance,
        double ExitDistance)>();
    foreach (var current in raw)
    {
        if (merged.Count > 0
            && merged[^1].CellId == current.CellId
            && current.EnterDistance <= merged[^1].ExitDistance + SpatialEpsilon)
        {
            var previous = merged[^1];
            merged[^1] = (
                previous.CellId,
                previous.PlateId,
                previous.EnterDistance,
                Math.Max(previous.ExitDistance, current.ExitDistance));
        }
        else
        {
            merged.Add(current);
        }
    }
    merged.Sort(static (left, right) =>
        left.EnterDistance.CompareTo(right.EnterDistance));
    return merged;
}

private static bool CellTouchesArc(GlobeCell cell, PlateBoundaryArc arc)
{
    if (arc.Points.Count < 2)
        return false;
    Span<GlobeVec3> corners = stackalloc GlobeVec3[3]
    {
        cell.C0,
        cell.C1,
        cell.C2,
    };
    GlobeVec3 first = arc.Points[0];
    GlobeVec3 last = arc.Points[^1];
    for (int edge = 0; edge < 3; edge++)
    {
        GlobeVec3 a = corners[edge];
        GlobeVec3 b = corners[(edge + 1) % 3];
        if ((SameDirection(a, first) && SameDirection(b, last))
            || (SameDirection(a, last) && SameDirection(b, first)))
        {
            return true;
        }
    }
    return false;
}

private static bool SameDirection(GlobeVec3 left, GlobeVec3 right)
{
    Vector3D a = ToVector(left).Normalize();
    Vector3D b = ToVector(right).Normalize();
    return Vector3D.Dot(a, b) >= 1.0 - 1e-10;
}
```

The proof is therefore local and attached: the reported down-going interval belongs to the same
validated material wedge whose outer edge is the trench hinge. It cannot be a disconnected cell,
an appended renderer strip, or a far-side plate hit.

Add the shared conversion helpers:

```csharp
private static Vector3D ToVector(GlobeVec3 point)
    => new(point.X, point.Y, point.Z);

private static GlobeVec3 ToGlobe(Vector3D point)
    => new((float)point.X, (float)point.Y, (float)point.Z);
```

- [ ] **Step 7: include the complete material identity in `ComputeDigest`**

Immediately after `Tick`, write:

```csharp
writer.Write(Seed);
writer.Write(GraphRevision);
writer.Write(TopologyDigest);
writer.Write(DeformationParameterDigest);
```

Inside the per-cell loop, after the legacy values, write all six material points:

```csharp
for (int corner = 0; corner < 3; corner++)
{
    Write(writer, OuterPointAtCellCorner(cellId, corner));
    Write(writer, InnerPointAtCellCorner(cellId, corner));
}
```

- [ ] **Step 8: compile and audit the sole authority**

Run:

```bash
dotnet build project/contracts/App.World/App.World.csproj
rg -n "class .*CrustVolume|record .*CrustVolume|struct .*CrustVolume" project --glob '*.cs'
```

Expected: build exit 0; the authority search declares only `CrustVolumeState`.

```bash
git add project/contracts/App.World/CrustVolumeState.cs
git commit -m "feat(world): model crust as deformed material wedges"
```

---

### Task 3: Build welded material controls at the sole materialization seam

**Files:**

- Modify: `project/contracts/App.World.Rendering/Globe/GlobePlateSurfaces.cs`
- Modify: `project/plugins/App.World/Crust/WorldCrustMaterializer.cs`
- Modify: `project/plugins/App.World/Services/Service.cs`

**Interfaces:**

- Consumes: Task 1 boundary frames/parameters and Task 2 factory.
- Produces: deterministic welded outer/inner controls; one mandatory underlap proof in the
  production log before presentation sees the state.

- [ ] **Step 1: identify and expose the existing shared topology**

Add `TopologyDigest`, assigned in the existing constructor before topology construction:

```csharp
public string TopologyDigest { get; }

// Immediately after the snapshot null guard in the GlobePlateSurfaces constructor.
TopologyDigest = ComputeTopologyDigest(snapshot);
```

Add the exact ordered identity helper. It includes cell order, cell id, plate ownership, and all
original corners, so equal cell counts with different ownership or connectivity cannot pass:

```csharp
private static string ComputeTopologyDigest(WorldGlobeSnapshot snapshot)
{
    using var stream = new MemoryStream();
    using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
    {
        writer.Write(snapshot.CellCount);
        for (int index = 0; index < snapshot.Cells.Count; index++)
        {
            var cell = snapshot.Cells[index];
            writer.Write(index);
            writer.Write(cell.CellId);
            writer.Write(cell.PlateId);
            Write(cell.C0);
            Write(cell.C1);
            Write(cell.C2);
        }

        void Write(GlobeVec3 point)
        {
            writer.Write(point.X);
            writer.Write(point.Y);
            writer.Write(point.Z);
        }
    }

    return Convert.ToHexString(
            SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))))
        .ToLowerInvariant();
}
```

Add `using System.IO;`, `using System.Security.Cryptography;`, and `using System.Text;`.

Add this method to `GlobePlateSurfaces`. It reuses `BuildPlateVertexMetres`; it does not dedupe
again:

```csharp
public double[] BuildSharedCornerMetres(IReadOnlyList<double> elevationsByCell)
{
    var perPlate = BuildPlateVertexMetres(elevationsByCell);
    var result = new double[_globalCellIds.Length * 3];
    for (int p = 0; p < _plates.Count; p++)
    {
        var plate = _plates[p];
        for (int face = 0; face < plate.CellIds.Length; face++)
        {
            int cellId = plate.CellIds[face];
            for (int corner = 0; corner < 3; corner++)
            {
                int localVertex = plate.LocalTriangles[(face * 3) + corner];
                result[(cellId * 3) + corner] = perPlate[p][localVertex];
            }
        }
    }
    return result;
}
```

- [ ] **Step 2: add the one plate-local weld operation**

```csharp
public (GlobeVec3[] Outer, GlobeVec3[] Inner) WeldPlateCorners(
    IReadOnlyList<GlobeVec3> outerCandidatesByCellCorner,
    IReadOnlyList<GlobeVec3> innerCandidatesByCellCorner)
{
    int expected = _globalCellIds.Length * 3;
    if (outerCandidatesByCellCorner.Count != expected)
        throw new ArgumentException("Outer candidates must contain three points per cell.");
    if (innerCandidatesByCellCorner.Count != expected)
        throw new ArgumentException("Inner candidates must contain three points per cell.");

    var outer = new GlobeVec3[expected];
    var inner = new GlobeVec3[expected];
    foreach (var plate in _plates)
    {
        var outerSums = new Vector3D[plate.LocalVertices.Length];
        var innerSums = new Vector3D[plate.LocalVertices.Length];
        var counts = new int[plate.LocalVertices.Length];
        for (int face = 0; face < plate.CellIds.Length; face++)
        {
            int cellId = plate.CellIds[face];
            for (int corner = 0; corner < 3; corner++)
            {
                int localVertex = plate.LocalTriangles[(face * 3) + corner];
                int source = (cellId * 3) + corner;
                outerSums[localVertex] += ToVector(outerCandidatesByCellCorner[source]);
                innerSums[localVertex] += ToVector(innerCandidatesByCellCorner[source]);
                counts[localVertex]++;
            }
        }

        for (int face = 0; face < plate.CellIds.Length; face++)
        {
            int cellId = plate.CellIds[face];
            for (int corner = 0; corner < 3; corner++)
            {
                int localVertex = plate.LocalTriangles[(face * 3) + corner];
                int target = (cellId * 3) + corner;
                double inverse = 1.0 / counts[localVertex];
                outer[target] = ToGlobe(outerSums[localVertex] * inverse);
                inner[target] = ToGlobe(innerSums[localVertex] * inverse);
            }
        }
    }
    ValidateClosedOuterContacts(outer);
    return (outer, inner);
}

private void ValidateClosedOuterContacts(IReadOnlyList<GlobeVec3> outer)
{
    var assigned = new bool[_globalVertices.Length];
    var globalPoint = new Vector3D[_globalVertices.Length];
    foreach (var plate in _plates)
    {
        for (int face = 0; face < plate.CellIds.Length; face++)
        {
            int cellId = plate.CellIds[face];
            for (int corner = 0; corner < 3; corner++)
            {
                int localVertex = plate.LocalTriangles[(face * 3) + corner];
                int globalVertex = plate.LocalToGlobal[localVertex];
                Vector3D point = ToVector(outer[(cellId * 3) + corner]);
                if (!assigned[globalVertex])
                {
                    assigned[globalVertex] = true;
                    globalPoint[globalVertex] = point;
                    continue;
                }

                if ((globalPoint[globalVertex] - point).Length() > 1e-6)
                {
                    throw new InvalidOperationException(
                        $"Outer contact at global vertex {globalVertex} is open.");
                }
            }
        }
    }
}

private static Vector3D ToVector(GlobeVec3 point)
    => new(point.X, point.Y, point.Z);

private static GlobeVec3 ToGlobe(Vector3D point)
    => new((float)point.X, (float)point.Y, (float)point.Z);
```

Add `using UnifyMaths;`.

This is the construction-time closed-contact gate. It uses the existing `LocalToGlobal` map; it
does not create another corner table. Inner controls remain plate-local so the two bodies can have
different roots and underlap.

- [ ] **Step 3: replace `WorldCrustMaterialization.BuildVolumeState`**

Use this signature:

```csharp
public CrustVolumeState BuildVolumeState(
    WorldGlobeSnapshot globe,
    IReadOnlyList<PlateBoundaryArc> boundaryArcs,
    long tick,
    int seed,
    int graphRevision,
    double verticalExaggeration,
    BoundaryProfileParameters profiles,
    IReadOnlyList<double> outerElevationsMetresByCell,
    IReadOnlyList<double> crustThicknessMetresByCell,
    IReadOnlyList<CellCrustFeature> featuresByCell,
    IReadOnlyDictionary<int, double> continentalFractionByCell)
```

Retain the existing tick guard, validate the four new scalar/profile arguments, then use this exact
body after the guard:

```csharp
ArgumentNullException.ThrowIfNull(profiles);
if (!double.IsFinite(verticalExaggeration) || verticalExaggeration <= 0.0)
    throw new ArgumentOutOfRangeException(nameof(verticalExaggeration));
ValidateVisualParameters(profiles);

var topology = new GlobePlateSurfaces(
    globe,
    noise: new NoiseParams(Amplitude: 0.0));
var cornerMetres = topology.BuildSharedCornerMetres(outerElevationsMetresByCell);
double referenceThickness = MedianPositive(crustThicknessMetresByCell);
var outerCandidates = new GlobeVec3[globe.CellCount * 3];
var innerCandidates = new GlobeVec3[globe.CellCount * 3];

for (int face = 0; face < globe.Cells.Count; face++)
{
    var cell = globe.Cells[face];
    double thicknessRatio = referenceThickness <= 0.0
        ? 1.0
        : Math.Clamp(
            crustThicknessMetresByCell[cell.CellId] / referenceThickness,
            0.65,
            1.50);
    double visualThickness =
        profiles.VisualCrustThicknessUnitRadius * thicknessRatio;

    for (int corner = 0; corner < 3; corner++)
    {
        int index = (cell.CellId * 3) + corner;
        var baseCorner = corner switch
        {
            0 => cell.C0,
            1 => cell.C1,
            _ => cell.C2,
        };
        var controlSample = CellBoundaryField.SampleDirection(
            baseCorner,
            cell.PlateId,
            boundaryArcs);
        Vector3D unit = ToVector(baseCorner).Normalize();
        double radius = 1.0 + (cornerMetres[index] * verticalExaggeration);
        Vector3D outer = unit * radius;
        Vector3D inner = outer - (unit * visualThickness);

        ApplyConvergentDeformation(
            controlSample,
            profiles,
            unit,
            ref outer,
            ref inner);
        outerCandidates[index] = ToGlobe(outer);
        innerCandidates[index] = ToGlobe(inner);
    }
}

var welded = topology.WeldPlateCorners(outerCandidates, innerCandidates);
string parameterDigest =
    ComputeVolumeParameterDigest(verticalExaggeration, profiles);
return CrustVolumeState.Create(
    tick,
    seed,
    graphRevision,
    topology.TopologyDigest,
    parameterDigest,
    globe,
    boundaryArcs,
    welded.Outer,
    welded.Inner,
    outerElevationsMetresByCell,
    crustThicknessMetresByCell,
    featuresByCell,
    continentalFractionByCell);
```

`outerElevationsMetresByCell` already includes the material-frame birth roughness sampled in
`Service.cs` and the Task 1 boundary/volcanic envelope. The zero-amplitude topology helper prevents
an additional sphere-fixed renderer noise field from entering the state.

Add `using FantaSim.App.World.Globe;`, `using FantaSim.Cartography.Globe;`,
`using UnifyMaths;`, `using System.IO;`, `using System.Security.Cryptography;`, and
`using System.Text;`.

- [ ] **Step 4: add the exact non-radial deformation**

Add these helpers inside `WorldCrustMaterialization`:

```csharp
private static void ApplyConvergentDeformation(
    in CellBoundarySample sample,
    in BoundaryProfileParameters profiles,
    Vector3D unit,
    ref Vector3D outer,
    ref Vector3D inner)
{
    if (!sample.Found
        || sample.Kind != PlateBoundaryKind.Convergent
        || sample.IsCollision
        || sample.SubductingPlateId is not int subductingPlateId)
    {
        return;
    }

    double bendHalfWidth = Math.Max(
        profiles.ConvergentTrenchHalfWidthRad * 2.0,
        profiles.ConvergentArcSetbackRad + profiles.ConvergentArcHalfWidthRad);
    double distance = Math.Abs(sample.SignedDistanceRad);
    if (distance > bendHalfWidth)
        return;
    double normalizedDistance = Math.Clamp(distance / bendHalfWidth, 0.0, 1.0);

    if (sample.CellPlateId == subductingPlateId)
    {
        // Zero at the exact hinge and at the far edge of the bend band, with a smooth attached
        // descending lobe between them. The hinge outer controls therefore remain coincident with
        // the overriding plate instead of being translated away.
        double bend = Math.Sin(Math.PI * SmoothStep(normalizedDistance));
        Vector3D intoOwningPlate = ToVector(sample.AcrossBoundaryDirection);
        Vector3D towardOverridingPlate = intoOwningPlate * -1.0;
        Vector3D shift =
            (towardOverridingPlate
                * profiles.ConvergentSlabUnderlapLengthUnitRadius
                * bend)
          - (unit * profiles.ConvergentSlabDepthUnitRadius * bend * bend);
        outer += shift;
        inner += shift;
        return;
    }

    double root = SmoothStep(1.0 - normalizedDistance);
    inner -= unit * profiles.ConvergentOverridingRootDepthUnitRadius * root;
}

private static double SmoothStep(double value)
{
    double t = Math.Clamp(value, 0.0, 1.0);
    return t * t * (3.0 - (2.0 * t));
}

private static double MedianPositive(IReadOnlyList<double> values)
{
    var positive = values
        .Where(value => double.IsFinite(value) && value > 0.0)
        .OrderBy(value => value)
        .ToArray();
    if (positive.Length == 0)
        return 0.0;
    int middle = positive.Length / 2;
    return positive.Length % 2 == 1
        ? positive[middle]
        : (positive[middle - 1] + positive[middle]) * 0.5;
}

private static void ValidateVisualParameters(BoundaryProfileParameters p)
{
    double[] values =
    {
        p.VisualCrustThicknessUnitRadius,
        p.ConvergentSlabUnderlapLengthUnitRadius,
        p.ConvergentSlabDepthUnitRadius,
        p.ConvergentOverridingRootDepthUnitRadius,
        p.ConvergentVolcanoConeHeight,
        p.ConvergentVolcanoPeriodPoints,
        p.ConvergentVolcanoSharpness,
    };
    if (values.Any(value => !double.IsFinite(value) || value < 0.0))
        throw new ArgumentOutOfRangeException(
            nameof(p),
            "Visual deformation parameters must be finite and non-negative.");
}
```

- [ ] **Step 5: add a deterministic deformation-parameter digest**

```csharp
private static string ComputeVolumeParameterDigest(
    double verticalExaggeration,
    BoundaryProfileParameters p)
{
    using var stream = new MemoryStream();
    using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
    {
        writer.Write(verticalExaggeration);
        writer.Write(p.ConvergentTrenchDepth);
        writer.Write(p.ConvergentTrenchHalfWidthRad);
        writer.Write(p.ConvergentArcHeight);
        writer.Write(p.ConvergentArcSetbackRad);
        writer.Write(p.ConvergentArcHalfWidthRad);
        writer.Write(p.ConvergentCollisionHeight);
        writer.Write(p.ConvergentCollisionHalfWidthRad);
        writer.Write(p.DivergentSwellHeight);
        writer.Write(p.DivergentSwellHalfWidthRad);
        writer.Write(p.DivergentRiftNotchDepth);
        writer.Write(p.DivergentRiftHalfWidthRad);
        writer.Write(p.TransformScarpAmplitude);
        writer.Write(p.TransformHalfWidthRad);
        writer.Write(p.TransformScarpPeriodPoints);
        writer.Write(p.VisualCrustThicknessUnitRadius);
        writer.Write(p.ConvergentSlabUnderlapLengthUnitRadius);
        writer.Write(p.ConvergentSlabDepthUnitRadius);
        writer.Write(p.ConvergentOverridingRootDepthUnitRadius);
        writer.Write(p.ConvergentVolcanoConeHeight);
        writer.Write(p.ConvergentVolcanoPeriodPoints);
        writer.Write(p.ConvergentVolcanoSharpness);
    }
    return Convert.ToHexString(
            SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))))
        .ToLowerInvariant();
}

private static Vector3D ToVector(GlobeVec3 point)
    => new(point.X, point.Y, point.Z);

private static GlobeVec3 ToGlobe(Vector3D point)
    => new((float)point.X, (float)point.Y, (float)point.Z);
```

- [ ] **Step 6: pass identity/profile inputs and enforce the proof in `Service.cs`**

Change the call at the existing materialization site to:

```csharp
var crustVolume = products.Materialization.BuildVolumeState(
    currentGlobe,
    currentArcs,
    arcTick,
    renderOptions.Seed,
    family.Revision,
    renderOptions.VerticalExaggeration,
    renderOptions.BoundaryProfiles,
    cellElevations,
    cellCrustThickness,
    cellFeatures,
    sampledFractions);
```

Immediately afterward, add:

```csharp
bool requiresUnderlapProof = currentArcs.Any(arc =>
    arc.Kind == PlateBoundaryKind.Convergent
    && !arc.IsCollision
    && arc.SubductingPlateId is not null);
if (requiresUnderlapProof)
{
    if (!crustVolume.TryFindConvergentUnderlapProof(out var proof))
    {
        throw new InvalidOperationException(
            $"Crust volume {crustVolume.Digest} has convergent polarity but no ordered "
          + "overriding/down-going ray intervals.");
    }

    _logger.LogInformation(
        "Crust underlap proof: digest={Digest}, arc={BoundaryArc}, "
      + "overriding={OverridingPlate}, downGoing={SubductingPlate}, "
      + "downGoingCell={SubductingCell}, overridingInterval=[{OverEnter:R},{OverExit:R}], "
      + "downGoingInterval=[{DownEnter:R},{DownExit:R}].",
        crustVolume.Digest,
        proof.BoundaryArcIndex,
        proof.OverridingPlateId,
        proof.SubductingPlateId,
        proof.SubductingCellId,
        proof.OverridingEnter,
        proof.OverridingExit,
        proof.SubductingEnter,
        proof.SubductingExit);
}
```

Replace the existing state-materialized log with:

```csharp
_logger.LogInformation(
    "Crust volume state materialized: algorithm={Algorithm}, tick={Tick}, cells={CellCount}, "
  + "arcs={ArcCount}, digest={Digest}.",
    crustVolume.AlgorithmVersion,
    crustVolume.Tick,
    crustVolume.CellCount,
    crustVolume.BoundaryArcs.Count,
    crustVolume.Digest);
```

- [ ] **Step 7: compile, inspect, and commit**

Run:

```bash
dotnet build project/contracts/App.World.Rendering/App.World.Rendering.csproj
dotnet build project/plugins/App.World/App.World.csproj
```

Expected: both exit 0.

Run:

```bash
rg -n "new (class|record|struct).*Volume|class .*Volume|record .*Volume|struct .*Volume" \
  project/contracts project/plugins --glob '*.cs'
```

Review every match. Expected geological authority: only `CrustVolumeState`; existing unrelated
render/cache names may remain, but this task must add none.

```bash
git add \
  project/contracts/App.World.Rendering/Globe/GlobePlateSurfaces.cs \
  project/plugins/App.World/Crust/WorldCrustMaterializer.cs \
  project/plugins/App.World/Services/Service.cs
git commit -m "feat(world): materialize continuous spherical plate volumes"
```

---

### Task 4: Evolve `PlateSolidBuilder` to extract the stored volume

**Files:**

- Modify: `project/contracts/App.World.Rendering/Globe/GlobePlateSurfaces.cs`
- Modify: `project/contracts/App.World.Rendering/Globe/PlateSolidBuilder.cs`

**Interfaces:**

- Consumes: Task 2 state points and Task 3 shared topology.
- Produces: the existing `PlateCap` and `PlateSolid` types; no volume-specific render DTO.

- [ ] **Step 1: build fixed outer caps from state points**

Add this method to `GlobePlateSurfaces`:

```csharp
public IReadOnlyList<PlateCap> BuildVolumeSurfaces(CrustVolumeState volume)
{
    ArgumentNullException.ThrowIfNull(volume);
    if (!string.Equals(
            volume.TopologyDigest,
            TopologyDigest,
            StringComparison.Ordinal))
    {
        throw new ArgumentException(
            "Volume cell order, plate ownership, corners, or topology does not match this globe.",
            nameof(volume));
    }

    var caps = new PlateCap[_plates.Count];
    for (int p = 0; p < _plates.Count; p++)
    {
        var plate = _plates[p];
        var directions = new CartesianPoint3[plate.LocalVertices.Length];
        var radialOffsets = new double[plate.LocalVertices.Length];
        var assigned = new bool[plate.LocalVertices.Length];

        for (int face = 0; face < plate.CellIds.Length; face++)
        {
            int cellId = plate.CellIds[face];
            for (int corner = 0; corner < 3; corner++)
            {
                int localVertex = plate.LocalTriangles[(face * 3) + corner];
                var point = volume.OuterPointAtCellCorner(cellId, corner);
                var vector = new Vector3D(point.X, point.Y, point.Z);
                double radius = vector.Length();
                if (radius <= 1e-12)
                    throw new InvalidOperationException("A volume outer control reached the origin.");
                var direction = vector * (1.0 / radius);
                var cartesian = new CartesianPoint3(direction.X, direction.Y, direction.Z);
                if (assigned[localVertex])
                {
                    var existing = directions[localVertex];
                    double delta =
                        Math.Abs(existing.X - cartesian.X)
                      + Math.Abs(existing.Y - cartesian.Y)
                      + Math.Abs(existing.Z - cartesian.Z)
                      + Math.Abs(
                          radialOffsets[localVertex]
                        - (radius - GlobeSurfaceBuilder.DefaultRadius));
                    if (delta > 1e-5)
                        throw new InvalidOperationException(
                            "Plate-local state controls are not welded.");
                    continue;
                }

                directions[localVertex] = cartesian;
                radialOffsets[localVertex] = radius - GlobeSurfaceBuilder.DefaultRadius;
                assigned[localVertex] = true;
            }
        }

        var surface = _builder.Build(
            directions,
            plate.LocalTriangles,
            radialOffsets,
            GlobeSurfaceBuilder.DefaultRadius);
        caps[p] = new PlateCap(
            plate.PlateId,
            plate.CellIds,
            surface,
            VertexProvenance: null);
    }
    return caps;
}
```

The normalized direction plus `radius - 1` reconstructs each arbitrary deformed point exactly;
the old base ray is not reused.

- [ ] **Step 2: add the state-based `PlateSolidBuilder.Build` overload**

```csharp
public static IReadOnlyList<PlateSolid> Build(
    IReadOnlyList<PlateCap> caps,
    CrustVolumeState volume)
{
    ArgumentNullException.ThrowIfNull(caps);
    ArgumentNullException.ThrowIfNull(volume);
    var result = new PlateSolid[caps.Count];
    for (int p = 0; p < caps.Count; p++)
    {
        if (caps[p].VertexProvenance is not null)
            throw new ArgumentException(
                "A0/B0 volume extraction accepts fixed caps only.",
                nameof(caps));
        result[p] = BuildOneSolid(caps[p], volume);
    }
    return result;
}

private static PlateSolid BuildOneSolid(PlateCap cap, CrustVolumeState volume)
{
    var surface = cap.Surface;
    int vertexCount = surface.VertexCount;
    var positions = new CartesianPoint3[vertexCount * 2];
    Array.Copy(surface.Positions, positions, vertexCount);
    var assigned = new bool[vertexCount];
    var originalDirections = new GlobeVec3[vertexCount];

    for (int face = 0; face < cap.CellIds.Length; face++)
    {
        int cellId = cap.CellIds[face];
        for (int corner = 0; corner < 3; corner++)
        {
            int localVertex = surface.Triangles[(face * 3) + corner];
            var point = volume.InnerPointAtCellCorner(cellId, corner);
            var inner = new CartesianPoint3(point.X, point.Y, point.Z);
            var sourceCell = volume.Globe.Cells[cellId];
            var original = corner switch
            {
                0 => sourceCell.C0,
                1 => sourceCell.C1,
                _ => sourceCell.C2,
            };
            if (assigned[localVertex])
            {
                var existing = positions[vertexCount + localVertex];
                double delta =
                    Math.Abs(existing.X - inner.X)
                  + Math.Abs(existing.Y - inner.Y)
                  + Math.Abs(existing.Z - inner.Z);
                if (delta > 1e-5)
                    throw new InvalidOperationException(
                        "Plate-local inner controls are not welded.");
                continue;
            }

            positions[vertexCount + localVertex] = inner;
            originalDirections[localVertex] = original;
            assigned[localVertex] = true;
        }
    }

    return new PlateSolid(
        cap.PlateId,
        positions,
        BuildSolidTriangles(
            surface.Triangles,
            vertexCount,
            originalDirections));
}
```

- [ ] **Step 3: factor the existing topology concatenation once**

Replace the existing radial `BuildOneSolid` lines that construct bottom/wall triangles with a call
to `BuildSolidTriangles(topTriangles, n, originalDirections: null)`. Add this helper:

```csharp
private static int[] BuildSolidTriangles(
    int[] topTriangles,
    int vertexCount,
    GlobeVec3[]? originalDirections)
{
    int faceCount = topTriangles.Length / 3;
    var bottomTriangles = new int[topTriangles.Length];
    for (int face = 0; face < faceCount; face++)
    {
        int a = topTriangles[(face * 3) + 0];
        int b = topTriangles[(face * 3) + 1];
        int c = topTriangles[(face * 3) + 2];
        bottomTriangles[(face * 3) + 0] = vertexCount + a;
        bottomTriangles[(face * 3) + 1] = vertexCount + c;
        bottomTriangles[(face * 3) + 2] = vertexCount + b;
    }

    var rimEdges = ExtractRimEdges(topTriangles, vertexCount);
    var wallTriangles = BuildWallTriangles(
        rimEdges,
        vertexCount,
        originalDirections);
    var triangles =
        new int[topTriangles.Length + bottomTriangles.Length + wallTriangles.Length];
    Array.Copy(topTriangles, 0, triangles, 0, topTriangles.Length);
    Array.Copy(
        bottomTriangles,
        0,
        triangles,
        topTriangles.Length,
        bottomTriangles.Length);
    Array.Copy(
        wallTriangles,
        0,
        triangles,
        topTriangles.Length + bottomTriangles.Length,
        wallTriangles.Length);
    return triangles;
}
```

Change its signature to:

```csharp
private static int[] BuildWallTriangles(
    List<DirectedRimEdge> rimEdges,
    int vertexCount,
    GlobeVec3[]? originalDirections)
```

Keep its existing loop chaining, but replace the final six-index emission with:

```csharp
bool canonicalStateWall = originalDirections is not null;
var triangles = new int[ordered.Count * 6];
for (int i = 0; i < ordered.Count; i++)
{
    int u = ordered[i].U;
    int v = ordered[i].V;
    int b = i * 6;
    bool uIsCanonicalLower = !canonicalStateWall
        || CompareOriginalDirection(originalDirections![u], originalDirections[v]) <= 0;
    if (uIsCanonicalLower)
    {
        // Diagonal outer-u -> inner-v.
        triangles[b + 0] = u;
        triangles[b + 1] = v;
        triangles[b + 2] = vertexCount + v;
        triangles[b + 3] = u;
        triangles[b + 4] = vertexCount + v;
        triangles[b + 5] = vertexCount + u;
    }
    else
    {
        // Diagonal outer-v -> inner-u, with the same outward quad winding.
        triangles[b + 0] = u;
        triangles[b + 1] = v;
        triangles[b + 2] = vertexCount + u;
        triangles[b + 3] = v;
        triangles[b + 4] = vertexCount + v;
        triangles[b + 5] = vertexCount + u;
    }
}
return triangles;
```

Add:

```csharp
private static int CompareOriginalDirection(GlobeVec3 left, GlobeVec3 right)
{
    int byX = left.X.CompareTo(right.X);
    if (byX != 0) return byX;
    int byY = left.Y.CompareTo(right.Y);
    return byY != 0 ? byY : left.Z.CompareTo(right.Z);
}
```

The old radial overload may remain for legacy tests and non-default diagnostics in this A0/B0
slice, but mark it:

```csharp
[Obsolete(
    "Migration-only radial extrusion. Default assembled/exploded projections must use CrustVolumeState.")]
```

- [ ] **Step 4: compile and commit**

```bash
dotnet build project/contracts/App.World.Rendering/App.World.Rendering.csproj
git add \
  project/contracts/App.World.Rendering/Globe/GlobePlateSurfaces.cs \
  project/contracts/App.World.Rendering/Globe/PlateSolidBuilder.cs
git commit -m "feat(world): extract plate solids from crust volume state"
```

Expected: build exit 0; no new DTO declaration.

---

### Task 5: Mount the same state in assembled and exploded views

**Files:**

- Modify: `project/plugins/App.Presentation/PlanetPresentationBinder.CutawayExploded.cs`
- Modify: `project/plugins/App.Presentation/PlanetPresentationBinder.WorldSlabAssembly.cs`

**Interfaces:**

- Consumes: Task 4 caps/solids and existing `ApplyExplodedFactor`.
- Produces: factor-0 closed assembly and factor-N rigid whole-plate explosion from one digest.

- [ ] **Step 1: make `BuildSlabTopCaps` state-derived**

Add `using FantaSim.Cartography.Globe;` to
`PlanetPresentationBinder.CutawayExploded.cs`.

Replace the method body with:

```csharp
private (
    IReadOnlyList<PlateCap> Caps,
    IReadOnlyDictionary<int, RampColor[]> VertexColors) BuildSlabTopCaps(
    PlanetPresentationDocument document,
    WorldGlobeSnapshot snapshot)
{
    var volume = document.CrustVolume;
    if (volume is null)
        return (
            Array.Empty<PlateCap>(),
            new Dictionary<int, RampColor[]>());

    _plateSurfaces ??= new GlobePlateSurfaces(
        snapshot,
        noise: new NoiseParams(Amplitude: 0.0));
    var caps = _plateSurfaces.BuildVolumeSurfaces(volume);
    var vertexColors = _lastIsTerrain && _lastPerCellColor is { Count: > 0 }
        ? PlateSurfaceMeshFactory.BuildPerPlateVertexColors(
            _plateSurfaces,
            _lastPerCellColor as RampColor[] ?? Array.Empty<RampColor>())
        : new Dictionary<int, RampColor[]>();
    return (caps, vertexColors);
}
```

Delete `ResolveSlabTopElevations`; it is no longer called. Keep `BuildExplodedTopDto`,
`BuildExplodedSolidDto`, and the existing Godot `ArrayMesh` publishers unchanged.

- [ ] **Step 2: replace `BuildExplodedSolidCrust` radial construction**

After the document/snapshot guards, require the state and build its solids:

```csharp
var volume = document.CrustVolume;
if (volume is null)
    return root;
var centroids = _lastCentroids ?? PlateSolidBuilder.ComputeCentroids(snapshot);
var (slabCaps, slabPerPlateVertexColors) = BuildSlabTopCaps(document, snapshot);
var solids = PlateSolidBuilder.Build(slabCaps, volume);
double factor = factorOverride ?? _explodedFactor;
var exploded = PlateSolidBuilder.ApplyExplodedFactor(solids, centroids, factor);
```

Delete the old thickness resolution, radial `Build`, `ShapeSlabJoints`, and associated arc branch
from this method. Keep the existing core mesh and call:

```csharp
AddSlabMeshInstances(
    root,
    slabCaps,
    exploded,
    centroids,
    factor * PlateSolidBuilder.DefaultMaxOffset,
    slabPerPlateVertexColors);
_log.LogInformation(
    "Exploded crust volume mounted: digest={Digest}, factor={Factor:R}, plates={PlateCount}.",
    volume.Digest,
    factor,
    exploded.Count);
```

- [ ] **Step 3: replace `BuildWorldSlabAssemblyRoot`**

Replace the method body with:

```csharp
private Node3D BuildWorldSlabAssemblyRoot()
{
    var root = new Node3D { Name = "WorldCrustVolumeAssembly" };
    var document = _currentDocument;
    var snapshot = document?.GlobeSnapshot;
    var volume = document?.CrustVolume;
    if (document is null || snapshot is null || volume is null)
        return root;

    var centroids = _lastCentroids ?? PlateSolidBuilder.ComputeCentroids(snapshot);
    var (caps, perPlateVertexColors) = BuildSlabTopCaps(document, snapshot);
    var solids = PlateSolidBuilder.Build(caps, volume);

    var interior = new MeshInstance3D
    {
        Name = "InteriorContext",
        Mesh = new SphereMesh
        {
            Radius = 0.86f,
            Height = 1.72f,
            RadialSegments = 48,
            Rings = 24,
        },
        MaterialOverride = PlanetShaderLibrary.BuildMoltenInteriorMaterial(),
        Scale = Vector3.One * 2.0f,
    };
    root.AddChild(interior);
    AddSlabMeshInstances(
        root,
        caps,
        solids,
        centroids,
        offsetMag: 0.0,
        slabPerPlateVertexColors: perPlateVertexColors);
    _log.LogInformation(
        "Assembled crust volume mounted: digest={Digest}, plates={PlateCount}, contactGap=0.",
        volume.Digest,
        solids.Count);
    return root;
}
```

In `RebuildWorldSlabAssembly`, replace the joint-gap log with:

```csharp
_log.LogInformation(
    "World crust volume assembly mounted: childNodes={ChildNodeCount}, contactGap=0.",
    _worldSlabAssemblyRoot.GetChildCount());
```

Delete `_jointMechanicsProfile`, `CountConvergent`, all uses of
`WorldSlabAssemblyComposer`, and the log that reports `SlabJointGapUnitRadius`. Update the file
comments to say that ordinary contacts are closed and depth testing hides the stored underlap.

- [ ] **Step 4: add a view-only neutral-gray evidence override**

In `PlanetPresentationBinder.CutawayExploded.cs`, add one cached material field and resolver:

```csharp
private Material? _neutralCrustEvidenceMaterial;

private Material ResolveCrustGeometryMaterial(Material productionMaterial)
{
    if (!string.Equals(
            Environment.GetEnvironmentVariable("FANTASIM_NEUTRAL_CRUST_GEOMETRY"),
            "1",
            StringComparison.Ordinal))
    {
        return productionMaterial;
    }

    return _neutralCrustEvidenceMaterial ??= new StandardMaterial3D
    {
        AlbedoColor = new Color(0.64f, 0.64f, 0.64f, 1.0f),
        Roughness = 0.92f,
        Metallic = 0.0f,
        VertexColorUseAsAlbedo = false,
    };
}
```

In `AddSlabMeshInstances`, pass
`ResolveCrustGeometryMaterial(HypsoPlateMaterialOverride)` to top instances and
`ResolveCrustGeometryMaterial(PlanetShaderLibrary.SlabWallStrataMaterial)` to solid instances.
This does not change any production palette or state; it only removes color cues from the two
required evidence captures when the launch environment variable is set.

- [ ] **Step 5: compile the presentation**

```bash
dotnet build project/plugins/App.Presentation/App.Presentation.csproj
```

Expected: exit 0.

- [ ] **Step 6: prove the default production path has no radial/joint mechanic**

Run:

```bash
rg -n \
  "WorldSlabAssemblyComposer|ShapeSlabJoints|BuildAssembly|ThicknessDepthScale|SlabJointGapUnitRadius" \
  project/plugins/App.Presentation/PlanetPresentationBinder.WorldSlabAssembly.cs \
  project/plugins/App.Presentation/PlanetPresentationBinder.CutawayExploded.cs
```

Expected: no matches.

Run:

```bash
rg -n "PlateSolidBuilder\\.Build" \
  project/plugins/App.Presentation/PlanetPresentationBinder.WorldSlabAssembly.cs \
  project/plugins/App.Presentation/PlanetPresentationBinder.CutawayExploded.cs
```

Expected: both callers use the two-argument `(caps, volume)` overload.

```bash
git add \
  project/plugins/App.Presentation/PlanetPresentationBinder.CutawayExploded.cs \
  project/plugins/App.Presentation/PlanetPresentationBinder.WorldSlabAssembly.cs
git commit -m "feat(presentation): pair assembled and exploded crust volumes"
```

---

### Task 6: Export, prove identity, and deposit the user visual gate

**Files:**

- Create:
  `vault/specs/evidence/2026-07-17-spherical-plate-material-volume-a0-b0/README.md`
- Create:
  `vault/specs/evidence/2026-07-17-spherical-plate-material-volume-a0-b0/assembled.png`
- Create:
  `vault/specs/evidence/2026-07-17-spherical-plate-material-volume-a0-b0/exploded.png`
- Create:
  `vault/specs/evidence/2026-07-17-spherical-plate-material-volume-a0-b0/run.log`

**Interfaces:**

- Consumes: Tasks 1–5.
- Produces: the falsifiable A0/B0 evidence package and the user's accept/reject decision.

- [ ] **Step 1: perform the duplicate-authority and forbidden-allocation audit**

Run:

```bash
rg -n \
  "CrustVolumeState2|class PlateVolume|record PlateVolume|struct PlateVolume|CrustIsosurface|MaterialWedge|RayHit" \
  project --glob '*.cs'
rg -n \
  "new .*[[][^]]*\\*[^]]*\\*[^]]*[]]|nx.*ny.*nz|N\\^3" \
  project/contracts/App.World project/plugins/App.World project/plugins/App.Presentation \
  --glob '*.cs'
```

Expected: no new peer authority and no global dense 3D allocation. Review any comment-only match
manually.

- [ ] **Step 2: build the exported app through UnifyBuild**

```bash
dotnet tool restore
dotnet unify-build BuildGodotDesktop --configuration Debug
```

Expected: exit 0 and an app under:

```text
build/_artifacts/0.1.2/godot/osx/complete-app.app
```

If GitVersion selects a different artifact version, use the exact value printed by:

```bash
task --silent version:artifacts
```

- [ ] **Step 3: bind the verification process to this worktree and HEAD**

Because Tasks 2, 4, and 5 change resident/shared assemblies, this gate uses the fresh full export,
not a world-PCK-only reload. Do not terminate a pre-existing user process with the same title.

```bash
repo_root="$(pwd)"
head_sha="$(git rev-parse HEAD)"
artifact_version="$(task --silent version:artifacts)"
app_path="$repo_root/build/_artifacts/$artifact_version/godot/osx/complete-app.app"
exe_path="$app_path/Contents/MacOS/complete-app"
bundle_id="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' \
  "$app_path/Contents/Info.plist")"
evidence_dir="$repo_root/vault/specs/evidence/2026-07-17-spherical-plate-material-volume-a0-b0"
mkdir -p "$evidence_dir"
FANTASIM_NEUTRAL_CRUST_GEOMETRY=1 \
  "$exe_path" --remote__enabled=1 >"$evidence_dir/run.log" 2>&1 &
verified_pid=$!
lsof -a -p "$verified_pid" -d txt -Fn
ps -p "$verified_pid" -o pid=,command=
```

Expected: `lsof` reports the exact `exe_path`. Record `repo_root`, `head_sha`, `app_path`,
`exe_path`, `bundle_id`, `verified_pid`, and `run.log` in the evidence README. The environment
variable is evidence-only and must not be written to production configuration.

- [ ] **Step 4: establish A0 from the production log**

Wait until the actual planet is interactive, then run:

```bash
rg -n \
  "Crust volume state materialized|Crust underlap proof|Assembled crust volume mounted" \
  "$evidence_dir/run.log"
```

Expected:

- one `crust-volume.v2` state digest;
- an overriding interval whose exit is less than the down-going interval's enter;
- the assembled mount log carries the same digest.

Any missing proof or exception is A0 failure. Diagnose the state/deformation; do not continue to
visual polish.

- [ ] **Step 5: capture the normal assembled view**

Bring the verified app to the foreground by its exact bundle id, then bind the capture to its PID:

```bash
open -b "$bundle_id"
foreground_pid="$(osascript -e \
  'tell application "System Events" to unix id of first process whose frontmost is true')"
test "$foreground_pid" = "$verified_pid"
```

Only after that check passes, capture a fresh OS-level screenshot of the interactive default World
view to `assembled.png`. It must show:

- one spherical globe;
- completely closed ordinary plate contacts;
- amplified broad trench and overriding mountain/arc belt;
- readable medium peaks/cone-chain/roughness;
- no buried slab, cell grid, chunk grid, artificial gap, or detached strip.

Use the `screenshot-result-check` skill during execution so the screenshot, not the code/log, is
judged.

- [ ] **Step 6: invoke the real whole-globe exploded command**

```bash
curl -sS http://127.0.0.1:19292/command \
  -H 'content-type: application/json' \
  -d '{"command":"render.exploded","payloadJson":"{\"factor\":1.0}"}'
```

Expected: successful command response and an interactive exploded globe. Capture
`exploded.png` only after repeating the foreground binding:

```bash
open -b "$bundle_id"
foreground_pid="$(osascript -e \
  'tell application "System Events" to unix id of first process whose frontmost is true')"
test "$foreground_pid" = "$verified_pid"
```

The exploded capture must show:

- complete curved plates moved as intact bodies;
- sidewalls, undersides, roots, and a continuous attached down-going plate edge;
- a readable over/under relationship;
- no independently exploded cell/chunk, appended tongue, shelf, ribbon, or renderer-authored
  overlap.

Run:

```bash
rg -n "Exploded crust volume mounted" "$evidence_dir/run.log"
```

Expected: the exploded mount log carries the same digest as the assembled mount.

- [ ] **Step 7: prove the explosion is view-only**

```bash
curl -sS http://127.0.0.1:19292/command \
  -H 'content-type: application/json' \
  -d '{"command":"render.exploded","payloadJson":"{\"factor\":0.0}"}'
rg -n \
  "Assembled crust volume mounted|Exploded crust volume mounted" \
  "$evidence_dir/run.log"
```

Expected: factor 0 reuses the same digest and restores the assembled relationships without another
geological materialization.

- [ ] **Step 8: write the evidence README with exact conclusions**

Use this complete structure. For the identity fields, copy the literal values already captured in
Step 3; do not leave explanatory text in the finished README. Do not change the verdict fields
before the screenshots have been inspected:

```markdown
# Spherical plate-material volume A0/B0 evidence

Repository: `/Users/apprenticegc/Work/lunar-horse/yokan-projects/fantasim-app-godot`
HEAD: copy the literal `git rev-parse HEAD` output recorded in Step 3
App: copy the literal absolute `app_path` recorded in Step 3
Executable: copy the literal absolute `exe_path` recorded in Step 3
Bundle identifier: copy the literal `bundle_id` recorded in Step 3
PID: copy the literal decimal `verified_pid` recorded in Step 3
Log: `run.log`
State digest: copy the literal digest from `run.log`
Material gate: `FANTASIM_NEUTRAL_CRUST_GEOMETRY=1`

## A0 structural result

ESTABLISHED:
- The production state logged distinct ordered overriding and down-going intervals.
- Both intervals existed before either presentation mounted.
- The state algorithm was `crust-volume.v2`.

DISPROVEN:
- Record every failed deformation/query attempt made during this task. Write `none` only when no
  attempt failed.

## B0 paired visual result

Assembled: `assembled.png`
Exploded: `exploded.png`

ESTABLISHED:
- Record only properties visible in the two deposited screenshots.

DISPROVEN:
- Record every visible mismatch; do not turn it into a clamp or a passing claim.

## Authority audit

- `CrustVolumeState` was the only geological plate-volume type.
- Both default binder paths used `PlateSolidBuilder.Build(caps, volume)`.
- The default binder paths had no radial extrusion, joint gap, or slab-joint mechanic caller.

## User verdict

Pending.
```

- [ ] **Step 9: ask the user for the binding paired-image verdict**

Show both deposited images together. Ask the user whether A0/B0 passes the reference comparison.
Do not begin A1/B1 or adaptive extraction before an explicit pass.

- [ ] **Step 10: commit the evidence only after it truthfully records the observed result**

```bash
git add vault/specs/evidence/2026-07-17-spherical-plate-material-volume-a0-b0
git commit -m "docs(evidence): record spherical plate volume A0 B0 gate"
```

Leave the verified exported app open at handoff and report its PID/log path.

## Completion gate

This plan is complete only when all of the following are true:

- the production underlap proof returns separate ordered overriding/down-going intervals;
- `CrustVolumeState` is still the only geological volume authority;
- assembled and exploded mounts log the same digest;
- the assembled screenshot shows a closed globe with readable broad and medium relief;
- the exploded screenshot reveals intact curved plates and stored attached underlap;
- the user's paired-image verdict is an explicit pass.
