namespace FantaSim.App.Ecs.Cells;

// Primitive globe-cell ECS components (app T3). These carry NO engine types — only ints, doubles,
// and floats — so they are ALWAYS compiled (no USE_PROJECT_REFERENCES gate). A globe cell becomes
// an ECS entity carrying { CellId, PlateId, CellUnit, CrustSample } and the derived { Elevation }.
// CellElevationSystem reads CrustSample and writes Elevation; App.World's CellElevationModel owns the
// world, populates the entities from the crust pipeline's StateByTick, and reads the per-cell elevation
// array out for the relief render (sub-project C) to upload to the GPU.

/// <summary>Stable geodesic cell id (face index). Doubles as the array index in the elevation export.</summary>
public readonly record struct CellId(int Value);

/// <summary>Owning plate id for the cell (-1 if unassigned).</summary>
public readonly record struct PlateId(int Value);

/// <summary>
/// Per-cell crust fields sampled from the engine's <c>CellCrustState</c> at the active tick.
/// Mirrors the engine field set in primitive doubles so App.Ecs stays engine-type-free:
/// <list type="bullet">
/// <item><see cref="ContinentalFraction"/> ∈ [0,1] — land (≈1) vs ocean (≈0) carried state.</item>
/// <item><see cref="OrogenicPressure"/> — accumulated collision pressure (~0..100 at collisions).</item>
/// <item><see cref="VolcanicActivity"/> — accumulated arc/ridge volcanism (~0..50).</item>
/// <item><see cref="CrustAgeTicks"/> — accumulated seafloor age in canonical ticks (~0..1e7; ridges reset toward 0).</item>
/// </list>
/// </summary>
public readonly record struct CrustSample(
    double ContinentalFraction,
    double OrogenicPressure,
    double VolcanicActivity,
    double CrustAgeTicks);

/// <summary>Unit-sphere cell center (geodesic face centroid, |v|≈1). Plain floats — Godot-free.</summary>
public readonly record struct CellUnit(float X, float Y, float Z);

/// <summary>Derived per-cell elevation scalar (output of <see cref="Systems.CellElevationSystem"/>).</summary>
public readonly record struct Elevation(double Value);
