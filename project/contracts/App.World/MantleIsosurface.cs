namespace FantaSim.App.World;

// Mantle x-ray view (M-A): pure, engine-free DTOs for the volumetric-anomaly isosurface meshes the
// presentation binder lifts into Godot ArrayMeshes. The engine-consuming extractor lives in
// App.World.Composition (it references Geosphere.Asthenosphere.Convection); these DTOs carry ONLY
// floats/ints so IService (T1, no engine references) can return them and the resident presentation
// seam (C1: no engine references) can bind them. Mirrors how GlobeVec3/PlateBoundaryArc stay
// contract-side while their producers sit deeper.

/// <summary>
/// One mantle isosurface mesh, pure indexed vertex/normal/triangle data in unit-sphere coordinates.
/// Vertices carry their true shell radius (in [InnerRadius, OuterRadius] of the unit sphere), so the
/// presentation seam only lifts them into Godot Vector3s (house globe scale applied node-side).
/// Normals come from the anomaly-field gradient (already outward-facing and unit length), NOT from
/// triangle geometry — smooth shading at modest grid cost per the method-lock. All arrays may be
/// empty when no cell crossed the isovalue (no surface for that channel this tick).
/// </summary>
/// <param name="Vertices">Flattened [x,y,z, ...] in unit-sphere radii. Length is a multiple of 3.</param>
/// <param name="Normals">Flattened per-vertex unit normals [x,y,z, ...]. Same length as <paramref name="Vertices"/>.</param>
/// <param name="Triangles">Vertex indices, length is a multiple of 3.</param>
public readonly record struct MantleIsosurfaceMesh(
    float[] Vertices,
    float[] Normals,
    int[] Triangles)
{
    public bool IsEmpty => Vertices.Length == 0 || Triangles.Length == 0;

    public static MantleIsosurfaceMesh Empty { get; } =
        new(Array.Empty<float>(), Array.Empty<float>(), Array.Empty<int>());
}

/// <summary>
/// The four isosurfaces of the signed volumetric mantle anomaly at one tick — TWO THRESHOLDS PER
/// POLARITY per the method-lock: a translucent outer + an opaque inner surface for each of cold
/// (negative anomaly: slabs sinking under trenches) and warm (positive: basal blanket, plumes,
/// ridge curtains). Layered translucency is what reads as volumetric. Any mesh may be empty when
/// that anomaly class/level is absent at the tick.
/// </summary>
public readonly record struct MantleIsosurfaceSet(
    long Tick,
    MantleIsosurfaceMesh ColdOuter,
    MantleIsosurfaceMesh ColdInner,
    MantleIsosurfaceMesh WarmOuter,
    MantleIsosurfaceMesh WarmInner)
{
    public bool IsEmpty => ColdOuter.IsEmpty && ColdInner.IsEmpty && WarmOuter.IsEmpty && WarmInner.IsEmpty;

    public static MantleIsosurfaceSet Empty { get; } = new(
        0L, MantleIsosurfaceMesh.Empty, MantleIsosurfaceMesh.Empty,
        MantleIsosurfaceMesh.Empty, MantleIsosurfaceMesh.Empty);
}
